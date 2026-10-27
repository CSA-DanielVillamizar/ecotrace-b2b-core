using System.Security.Cryptography;
using System.Text;
using EcoTrace.Identity.Api.Contracts;
using EcoTrace.Identity.Api.Seguridad;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Usuario = EcoTrace.Identity.Domain.User;

namespace EcoTrace.Identity.Api.Controllers;

/// <summary>Authorization Server del Trabajo 3 (ADR 0004): login, refresh, logout y token de servicio.</summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(
    IdentityDbContext db,
    EmisorDeTokens emisor,
    Contrasenas contrasenas,
    IOptions<OpcionesJwt> jwt,
    IConfiguration configuracion,
    TimeProvider reloj,
    ILogger<AuthController> registro) : ControllerBase
{
    private const string CredencialesInvalidas = "Credenciales inválidas";

    /// <summary>Inicia sesión. Responde el mismo 401 genérico sin importar qué falló.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<SesionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SesionResponse>> Login(LoginRequest solicitud, CancellationToken ct)
    {
        var email = (solicitud.Email ?? string.Empty).Trim().ToLowerInvariant();
        var usuario = email.Length == 0
            ? null
            : await db.Users.Include(u => u.Role).Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Email == email, ct);

        var verificacion = contrasenas.Verificar(usuario, solicitud.Password);
        if (usuario is null || verificacion == ResultadoVerificacion.Invalida)
        {
            registro.LogInformation("Inicio de sesión rechazado.");
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: CredencialesInvalidas);
        }

        // Solo después de comprobar la contraseña se revela que la organización está suspendida.
        ExigirOrganizacionActiva(usuario.Tenant!);

        if (verificacion == ResultadoVerificacion.ValidaConRehash)
        {
            usuario.AsignarContrasenaHash(contrasenas.Calcular(usuario, solicitud.Password!));
        }

        var refresco = EmisorDeTokens.NuevoTokenRefresco();
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var sesion = TokenRefresco.Crear(
            usuario.UserId, EmisorDeTokens.Hash(refresco), ahora, TimeSpan.FromDays(jwt.Value.DiasRefresco));
        db.TokensRefresco.Add(sesion);
        await db.GuardarAsync("No se pudo iniciar la sesión.", ct);

        var acceso = emisor.EmitirDeUsuario(usuario.UserId, usuario.TenantId, usuario.Role!.Nombre, sesion.TokenRefrescoId);
        registro.LogInformation("Sesión iniciada para {UserId}.", usuario.UserId);
        return new SesionResponse(acceso.Token, refresco, acceso.ExpiraEn);
    }

    /// <summary>Cambia un refresh token vigente por un access token nuevo. 401 si está vencido, revocado o no existe.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<SesionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SesionResponse>> Refrescar(RefrescoRequest solicitud, CancellationToken ct)
    {
        var token = await BuscarTokenRefrescoAsync(solicitud.RefreshToken, tracking: false, ct);
        if (token is null || !token.EstaVigente(reloj.GetUtcNow().UtcDateTime))
        {
            registro.LogInformation("Refresh token rechazado.");
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Refresh token inválido");
        }

        var usuario = await db.Users.AsNoTracking().Include(u => u.Role).Include(u => u.Tenant)
            .FirstAsync(u => u.UserId == token.UserId, ct);
        ExigirOrganizacionActiva(usuario.Tenant!);

        var acceso = emisor.EmitirDeUsuario(usuario.UserId, usuario.TenantId, usuario.Role!.Nombre, token.TokenRefrescoId);
        return new SesionResponse(acceso.Token, solicitud.RefreshToken!, acceso.ExpiraEn);
    }

    /// <summary>Revoca el refresh token. Idempotente: uno ya revocado (o desconocido) también responde 200.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> CerrarSesion(RefrescoRequest solicitud, CancellationToken ct)
    {
        var token = await BuscarTokenRefrescoAsync(solicitud.RefreshToken, tracking: true, ct);
        if (token?.Revocar(reloj.GetUtcNow().UtcDateTime) == true)
        {
            await db.GuardarAsync("No se pudo cerrar la sesión.", ct);
            registro.LogInformation("Sesión cerrada para {UserId}.", token.UserId);
        }

        return Ok();
    }

    /// <summary>
    /// Token para llamadas entre servicios: 5 minutos, <c>role = Servicio</c> y <c>aud</c> del módulo destino.
    /// </summary>
    [HttpPost("token-servicio")]
    [AllowAnonymous]
    [ProducesResponseType<TokenServicioResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public ActionResult<TokenServicioResponse> TokenServicio(TokenServicioRequest solicitud)
    {
        var clientes = configuracion.GetSection(ClienteServicio.Seccion).Get<ClienteServicio[]>() ?? [];
        var cliente = clientes.FirstOrDefault(c => string.Equals(c.ClientId, solicitud.ClientId, StringComparison.Ordinal));

        if (cliente is null || !SecretoCoincide(cliente, solicitud.ClientSecret))
        {
            registro.LogInformation("Token de servicio rechazado.");
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: CredencialesInvalidas);
        }

        var audiencia = solicitud.Audiencia?.Trim();
        if (string.IsNullOrEmpty(audiencia) || !cliente.Audiencias.Contains(audiencia, StringComparer.Ordinal))
        {
            throw DomainException.Forbidden("Este servicio no puede pedir token para esa audiencia.");
        }

        var token = emisor.EmitirDeServicio(cliente.ClientId, audiencia);
        registro.LogInformation("Token de servicio emitido para {ClientId} hacia {Audiencia}.", cliente.ClientId, audiencia);
        return new TokenServicioResponse(token.Token, token.ExpiraEn);
    }

    /// <summary>Lo que Identity leyó del token recibido (útil para comprobar un token sin decodificarlo a mano).</summary>
    [HttpGet("sesion")]
    [Authorize]
    [ProducesResponseType<SesionActualResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public SesionActualResponse Sesion()
    {
        var exp = long.Parse(User.FindFirst(JwtRegisteredClaimNames.Exp)!.Value);
        return new SesionActualResponse(
            User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value,
            User.TenantId(),
            User.FindFirst(Claims.Rol)!.Value,
            User.FindAll(JwtRegisteredClaimNames.Aud).Select(c => c.Value).ToArray(),
            DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime);
    }

    private async Task<TokenRefresco?> BuscarTokenRefrescoAsync(string? valor, bool tracking, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var hash = EmisorDeTokens.Hash(valor);
        var consulta = tracking ? db.TokensRefresco : db.TokensRefresco.AsNoTracking();
        return await consulta.FirstOrDefaultAsync(t => t.Hash == hash, ct);
    }

    private static void ExigirOrganizacionActiva(Tenant tenant)
    {
        if (tenant.Estado != EstadoTenant.Activo)
        {
            throw DomainException.Forbidden("La organización está suspendida.");
        }
    }

    private bool SecretoCoincide(ClienteServicio cliente, string? secreto)
    {
        byte[] esperado;
        try
        {
            esperado = Convert.FromHexString(cliente.SecretoSha256);
        }
        catch (FormatException)
        {
            registro.LogError("El secreto configurado para {ClientId} no es un SHA-256 en hexadecimal.", cliente.ClientId);
            return false;
        }

        var recibido = SHA256.HashData(Encoding.UTF8.GetBytes(secreto ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(recibido, esperado);
    }
}
