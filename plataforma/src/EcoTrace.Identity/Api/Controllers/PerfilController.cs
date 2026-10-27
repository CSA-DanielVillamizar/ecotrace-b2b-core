using System.Text.Json;
using EcoTrace.Identity.Api.Contracts;
using EcoTrace.Identity.Api.Seguridad;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Usuario = EcoTrace.Identity.Domain.User;

namespace EcoTrace.Identity.Api.Controllers;

/// <summary>
/// El perfil del usuario del token: lo que el módulo Identity de la app muestra, guarda en caché y cambia con
/// acciones encoladas (ADR 0005). Las acciones aceptan <c>Idempotency-Key</c>:
/// <list type="bullet">
/// <item>Sin el encabezado, la acción se aplica normalmente.</item>
/// <item>Con una clave nueva, se aplica y se guarda su resultado en la misma transacción.</item>
/// <item>Con una clave ya procesada (un reintento tras un corte de red), responde 200 con el resultado de la
/// primera vez y no aplica nada.</item>
/// <item>Con una clave mal formada, o ya usada para otra acción u otro usuario, responde 400 (tipo A).</item>
/// </list>
/// Un rechazo lleva <c>tipoConflicto</c>: A si el dato se puede corregir, B si el servidor decidió.
/// </summary>
[ApiController]
[Authorize(Policy = Politicas.Usuario)]
[Route("api/perfil")]
[Produces("application/json")]
public sealed class PerfilController(IdentityDbContext db, TimeProvider reloj, IOptions<JsonOptions> json) : ControllerBase
{
    public const string EncabezadoIdempotencia = "Idempotency-Key";
    public const string TipoActualizarPerfil = "ActualizarPerfil";
    public const string TipoCerrarOtrasSesiones = "CerrarOtrasSesiones";

    [HttpGet]
    [ProducesResponseType<PerfilResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PerfilResponse>> Obtener(CancellationToken ct)
    {
        var usuario = await BuscarUsuarioAsync(tracking: false, ct);
        return PerfilResponse.De(usuario);
    }

    /// <summary>Cambia nombre y teléfono. 400 (A) si un dato no es válido; 409 (B) si la organización está suspendida.</summary>
    [HttpPut]
    [ProducesResponseType<PerfilResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IActionResult> Actualizar(
        ActualizarPerfilRequest solicitud,
        [FromHeader(Name = EncabezadoIdempotencia)] string? claveIdempotencia,
        CancellationToken ct) =>
        ConIdempotenciaAsync(claveIdempotencia, TipoActualizarPerfil, usuario =>
        {
            // El estado de la organización es autoritativo: si la suspendieron mientras el usuario estaba sin
            // señal, el servidor manda y el cambio no se aplica (conflicto B).
            if (usuario.Tenant!.Estado != EstadoTenant.Activo)
            {
                throw DomainException.Conflict("La organización está suspendida: no se pueden cambiar los datos del perfil.");
            }

            usuario.ActualizarPerfil(solicitud.Nombre, solicitud.Telefono);
            return Task.FromResult<object>(PerfilResponse.De(usuario));
        }, ct);

    /// <summary>
    /// Revoca los refresh tokens de las demás sesiones del usuario y deja viva la actual (la del claim <c>sid</c>).
    /// Los access tokens ya emitidos en esos dispositivos siguen sirviendo hasta que venzan (15 minutos).
    /// </summary>
    [HttpPost("cierre-otras-sesiones")]
    [ProducesResponseType<CierreOtrasSesionesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IActionResult> CerrarOtrasSesiones(
        [FromHeader(Name = EncabezadoIdempotencia)] string? claveIdempotencia, CancellationToken ct) =>
        ConIdempotenciaAsync(claveIdempotencia, TipoCerrarOtrasSesiones, async usuario =>
        {
            var actual = User.SesionId()
                ?? throw DomainException.Validation("El token no identifica la sesión actual. Inicia sesión de nuevo.");

            var ahora = reloj.GetUtcNow().UtcDateTime;
            var otras = await db.TokensRefresco
                .Where(t => t.UserId == usuario.UserId && t.TokenRefrescoId != actual && t.RevocadoEn == null)
                .ToListAsync(ct);

            return new CierreOtrasSesionesResponse(otras.Count(t => t.Revocar(ahora)));
        }, ct);

    private async Task<IActionResult> ConIdempotenciaAsync(
        string? encabezado, string tipo, Func<Usuario, Task<object>> accion, CancellationToken ct)
    {
        var operacionId = LeerClave(encabezado);
        var userId = User.UserId() ?? throw DomainException.NotFound("El token no identifica a un usuario.");

        if (operacionId is { } clave)
        {
            var previa = await db.OperacionesProcesadas.AsNoTracking().FirstOrDefaultAsync(o => o.OperacionId == clave, ct);
            if (previa is not null)
            {
                return Repetida(previa, userId, tipo);
            }
        }

        var usuario = await BuscarUsuarioAsync(tracking: true, ct);
        var resultado = JsonSerializer.Serialize(await accion(usuario), json.Value.JsonSerializerOptions);

        if (operacionId is { } nueva)
        {
            db.OperacionesProcesadas.Add(
                OperacionProcesada.Registrar(nueva, userId, tipo, resultado, reloj.GetUtcNow().UtcDateTime));
        }

        try
        {
            await db.GuardarAsync("Esa acción ya se estaba procesando.", ct);
        }
        catch (DomainException conflicto) when (conflicto.Kind == DomainErrorKind.Conflict && operacionId is not null)
        {
            // Dos envíos simultáneos con la misma clave: el otro ganó. Se responde con su resultado.
            db.ChangeTracker.Clear();
            var ganadora = await db.OperacionesProcesadas.AsNoTracking()
                .FirstOrDefaultAsync(o => o.OperacionId == operacionId.Value, ct);
            if (ganadora is null)
            {
                throw;
            }

            return Repetida(ganadora, userId, tipo);
        }

        return Json(resultado);
    }

    private static Guid? LeerClave(string? encabezado)
    {
        if (string.IsNullOrWhiteSpace(encabezado))
        {
            return null;
        }

        if (!Guid.TryParse(encabezado.Trim(), out var clave) || clave == Guid.Empty)
        {
            throw DomainException.Validation($"El encabezado {EncabezadoIdempotencia} debe ser un identificador (Guid) válido.");
        }

        return clave;
    }

    private static ContentResult Repetida(OperacionProcesada previa, Guid userId, string tipo)
    {
        if (!previa.EsLaMisma(userId, tipo))
        {
            throw DomainException.Validation("La clave de idempotencia ya se usó para otra acción.");
        }

        return Json(previa.Resultado);
    }

    private static ContentResult Json(string cuerpo) =>
        new() { Content = cuerpo, ContentType = "application/json; charset=utf-8", StatusCode = StatusCodes.Status200OK };

    private async Task<Usuario> BuscarUsuarioAsync(bool tracking, CancellationToken ct)
    {
        var userId = User.UserId() ?? throw DomainException.NotFound("El token no identifica a un usuario.");
        var consulta = tracking ? db.Users : db.Users.AsNoTracking();
        return await consulta.Include(u => u.Role).Include(u => u.Tenant).FirstOrDefaultAsync(u => u.UserId == userId, ct)
            ?? throw DomainException.NotFound("El usuario del token ya no existe.");
    }
}
