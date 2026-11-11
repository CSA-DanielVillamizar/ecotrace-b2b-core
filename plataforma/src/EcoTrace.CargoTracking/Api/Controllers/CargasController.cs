using System.Text.Json;
using EcoTrace.CargoTracking.Api.Contracts;
using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.CargoTracking.Api.Security;
using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcoTrace.CargoTracking.Api.Controllers;

/// <summary>
/// Las rutas para trabajar con cargas: crearlas, asignarles vehículo y conductor, registrar su seguimiento (incluida la
/// entrega) y consultarlas.
/// <para>
/// Todas exigen un token válido (ADR 0004). Además, cada ruta dice qué roles pueden usarla y de qué organización tienen que
/// ser. La organización se lee <b>siempre del token</b>, nunca de lo que mande el cliente.
/// </para>
/// <list type="bullet">
/// <item>Quien crea la carga es de la organización generadora.</item>
/// <item>Quien la asigna y quien registra su seguimiento (incluida la entrega) es de la organización transportista.</item>
/// <item>Quien la consulta es de una de las dos organizaciones.</item>
/// </list>
/// Un token válido de otra organización recibe 403, y un rol sin permiso también.
/// </summary>
/// <param name="db">La base de datos de Cargo.</param>
/// <param name="reloj">El reloj del servidor (las pruebas pueden cambiarlo).</param>
/// <param name="opcionesJson">
/// Las mismas opciones de JSON con las que la API responde (nombres en camelCase y estados escritos como texto). Sirven para
/// guardar la respuesta de una operación exactamente como se le dio al cliente, y así devolverla igual si la operación se repite.
/// </param>
[ApiController]
[Authorize]
[Route("api/cargas")]
[Produces("application/json")]
public sealed class CargasController(
    CargoTrackingDbContext db,
    TimeProvider reloj,
    IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> opcionesJson) : ControllerBase
{
    /// <summary>
    /// El nombre del encabezado donde la app manda el número de la operación (<c>OperationId</c>, ADR 0005). Lo agrega solo el
    /// motor de sincronización del teléfono. Quien no lo manda (Swagger, la consola web) funciona como siempre.
    /// </summary>
    private const string EncabezadoIdempotencia = "Idempotency-Key";

    /// <summary>
    /// Crea una carga nueva, en estado Pendiente, con su organización generadora y su transportista. La crea alguien de la
    /// organización generadora: la organización que dice el cuerpo tiene que ser la misma del token (si no, 403).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.Supervisor + "," + Roles.Administrador)]
    [ProducesResponseType<CargaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CargaResponse>> Crear(CrearCargaRequest solicitud, CancellationToken ct)
    {
        if (!User.PerteneceA(solicitud.GeneradorTenantId!.Value))
        {
            return Forbid();
        }

        var carga = Carga.Crear(
            solicitud.GeneradorTenantId!.Value,
            solicitud.TransportistaTenantId!.Value,
            solicitud.Descripcion,
            solicitud.Origen,
            solicitud.Destino,
            solicitud.PesoKg!.Value,
            reloj.GetUtcNow().UtcDateTime);

        db.Cargas.Add(carga);
        await db.GuardarAsync("Ya existe una carga con ese identificador.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = carga.CargaId }, CargaResponse.De(carga));
    }

    /// <summary>
    /// Lista las cargas de <b>la organización del token</b>, sea como generadora o como transportista. El filtro
    /// <c>tenantId</c> solo se acepta si es esa misma organización: nadie puede pedir las cargas de otra (403).
    /// También se puede filtrar por estado.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<CargaResponse>>> Listar(
        [FromQuery] Guid? tenantId, [FromQuery] EstadoCarga? estado, CancellationToken ct)
    {
        // Sin organización en el token (el de un servicio, por ejemplo) no hay nada que listar: es 403, no una lista de todo.
        if (User.TenantId() is not { } propia || (tenantId is not null && tenantId != propia))
        {
            return Forbid();
        }

        var consulta = db.Cargas.AsNoTracking()
            .Where(c => c.GeneradorTenantId == propia || c.TransportistaTenantId == propia);

        if (estado is not null)
        {
            consulta = consulta.Where(c => c.Estado == estado);
        }

        var cargas = await consulta.OrderByDescending(c => c.CreadoEn).ToListAsync(ct);
        IReadOnlyList<CargaResponse> respuesta = cargas.Select(CargaResponse.De).ToList();
        return Ok(respuesta);
    }

    /// <summary>
    /// Devuelve una carga con su asignación y su línea de seguimiento. Solo si quien pregunta es de una de las dos
    /// organizaciones de la carga (si no, 403). Si la carga no existe, 404.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CargaDetalleResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: false, ct);
        if (!EsDeLaCarga(carga))
        {
            return Forbid();
        }

        return CargaDetalleResponse.De(carga);
    }

    /// <summary>
    /// Le asigna a la carga un vehículo y un conductor (identificadores de Fleet Management). Solo se puede desde Pendiente y
    /// una sola vez, y solo la organización transportista de esa carga.
    /// </summary>
    [HttpPost("{id:guid}/asignacion")]
    [Authorize(Roles = Roles.Supervisor + "," + Roles.Administrador)]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargaDetalleResponse>> Asignar(
        Guid id, AsignarCargaRequest solicitud, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: true, ct);
        if (!User.PerteneceA(carga.TransportistaTenantId))
        {
            return Forbid();
        }

        carga.Asignar(solicitud.VehiculoId!.Value, solicitud.ConductorId!.Value, reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("La carga ya tiene una asignación.", ct);

        return CreatedAtAction(nameof(Obtener), new { id }, CargaDetalleResponse.De(carga));
    }

    /// <summary>
    /// Registra un cambio de estado de la carga (en tránsito, con novedad, entregada). Lo hace un Conductor o un Supervisor de
    /// la organización transportista de esa carga. Con el estado Entregado se confirma la entrega, la operación más crítica:
    /// de ahí arranca el Saga que libera el pago.
    /// <para>
    /// Es idempotente (ADR 0005): repetirla con el mismo número no repite el efecto. Si llega el encabezado
    /// <c>Idempotency-Key</c>:
    /// </para>
    /// <list type="bullet">
    /// <item>La primera vez se procesa normal y responde <b>201</b>. Junto con el cambio se guarda un apunte de la operación,
    /// en la misma transacción.</item>
    /// <item>Si el mismo número llega otra vez (por ejemplo, un reintento después de cortarse la señal), responde <b>200</b>
    /// con la respuesta de la primera vez y no hace nada más: no crea otro seguimiento ni otro mensaje para Billing, así que
    /// el pago no se dispara dos veces.</item>
    /// </list>
    /// Sin el encabezado funciona como antes: una entrega repetida responde 409.
    /// </summary>
    [HttpPost("{id:guid}/seguimientos")]
    [Authorize(Roles = Roles.Conductor + "," + Roles.Supervisor)]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargaDetalleResponse>> RegistrarSeguimiento(
        Guid id,
        RegistrarSeguimientoRequest solicitud,
        [FromHeader(Name = EncabezadoIdempotencia)] string? claveIdempotencia,
        CancellationToken ct)
    {
        // PASO 1. Se lee el número de la operación (Idempotency-Key) antes de hacer nada. Si viene mal escrito, se rechaza con 400.
        var operacionId = LeerClaveDeIdempotencia(claveIdempotencia);

        // PASO 2. ¿Esta operación ya se procesó? Entonces no se repite nada: se devuelve la respuesta de la primera vez.
        //    Esto va antes de mirar la carga a propósito: aunque la carga ya esté Entregada, quien reintenta una acción
        //    que SÍ se hizo recibe su 200 y no un 409.
        if (operacionId is { } clave)
        {
            var previa = await db.OperacionesProcesadas
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OperacionId == clave, ct);

            if (previa is not null)
            {
                return await RespuestaDeOperacionRepetidaAsync(previa, id, ct);
            }
        }

        // PASO 3. Es la primera vez que llega esta operación: se procesa normal. Todo va dentro de un try porque un
        //    reintento puede chocar en cualquier punto (ver el catch al final).
        try
        {
            var carga = await CargarAsync(id, tracking: true, ct);

            // Solo la organización transportista de esta carga registra su seguimiento y su entrega.
            if (!User.PerteneceA(carga.TransportistaTenantId))
            {
                return Forbid();
            }

            var ahora = reloj.GetUtcNow().UtcDateTime;
            carga.RegistrarSeguimiento(solicitud.Estado!.Value, solicitud.Ubicacion, solicitud.Nota, ahora);

            // Si se entregó, se guarda también el mensaje para Billing (el Outbox, ADR 0002) en la MISMA transacción
            // que el cambio de estado: quedan guardados los dos o ninguno.
            if (carga.Estado == EstadoCarga.Entregado)
            {
                db.Outbox.Add(OutboxMensaje.EntregaConfirmada(carga, HttpContext.CorrelationId(), ahora));
            }

            var respuesta = CargaDetalleResponse.De(carga);

            // PASO 4. Se anota la operación como procesada (con la respuesta que se va a dar) en la misma transacción.
            //    Así el cambio, el mensaje para Billing y el apunte se guardan juntos o ninguno.
            if (operacionId is { } claveNueva)
            {
                var resultado = JsonSerializer.Serialize(respuesta, opcionesJson.Value.JsonSerializerOptions);
                db.OperacionesProcesadas.Add(OperacionProcesada.Registrar(claveNueva, id, resultado, ahora));
            }

            await db.GuardarAsync("No se pudo registrar el seguimiento.", ct);

            return CreatedAtAction(nameof(Obtener), new { id }, respuesta);
        }
        catch (DomainException conflicto) when (conflicto.Kind == DomainErrorKind.Conflict && operacionId is not null)
        {
            // Caso raro: llegó OTRA solicitud con el MISMO número casi al mismo tiempo y terminó primero.
            // Se nota en uno de dos momentos: al revisar el estado (la carga ya aparece entregada) o al guardar (la base
            // de datos solo deja pasar una). Si esta solicitud era un reintento, la operación SÍ se hizo: se le responde
            // 200 con el resultado, y no un 409 que la app tomaría por un rechazo.
            db.ChangeTracker.Clear();
            var ganadora = await db.OperacionesProcesadas
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OperacionId == operacionId!.Value, ct);

            if (ganadora is null)
            {
                // El conflicto NO venía de un número repetido (por ejemplo, otra operación con otro número ya entregó
                // la carga): es un conflicto de verdad y se informa como tal.
                throw;
            }

            return await RespuestaDeOperacionRepetidaAsync(ganadora, id, ct);
        }
    }

    /// <summary>
    /// Devuelve la línea de tiempo de la carga, del evento más antiguo al más reciente. Solo para quien sea de una de las
    /// dos organizaciones de la carga (si no, 403).
    /// </summary>
    [HttpGet("{id:guid}/seguimientos")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SeguimientoResponse>>> ListarSeguimientos(Guid id, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: false, ct);
        if (!EsDeLaCarga(carga))
        {
            return Forbid();
        }

        return Ok(CargaDetalleResponse.De(carga).Seguimientos);
    }

    /// <summary>Busca la carga con su asignación y sus seguimientos. Si no existe, lanza un error que se convierte en 404.</summary>
    private async Task<Carga> CargarAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var consulta = db.Cargas.Include(c => c.Asignacion).Include(c => c.Seguimientos).AsQueryable();
        if (!tracking)
        {
            consulta = consulta.AsNoTracking();
        }

        return await consulta.FirstOrDefaultAsync(c => c.CargaId == id, ct)
            ?? throw DomainException.NotFound("No existe una carga con ese identificador.");
    }

    /// <summary>Dice si quien llama es de la organización generadora o de la transportista de esta carga.</summary>
    private bool EsDeLaCarga(Carga carga) =>
        User.PerteneceA(carga.GeneradorTenantId, carga.TransportistaTenantId);

    /// <summary>
    /// Lee el número de la operación que llega en <c>Idempotency-Key</c>. Si el encabezado no vino, devuelve null (el cliente
    /// no pidió idempotencia). El número es un Guid inventado por el teléfono, así que cualquier otro valor se rechaza con 400.
    /// </summary>
    private static Guid? LeerClaveDeIdempotencia(string? encabezado)
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

    /// <summary>
    /// Arma la respuesta para una operación que ya se había procesado: 200 con la respuesta de la primera vez, tal como se
    /// guardó. El 200 (y no el 201 de la primera vez) es la señal de «esto ya estaba hecho». Rechaza si el número se usó en
    /// otra carga (400) o si quien pregunta no es de la organización transportista (403).
    /// </summary>
    private async Task<ActionResult> RespuestaDeOperacionRepetidaAsync(OperacionProcesada previa, Guid cargaId, CancellationToken ct)
    {
        // Un mismo número no puede servir para dos cargas distintas: sería un error del cliente, y devolver el
        // resultado de otra carga mostraría datos que no corresponden.
        if (previa.CargaId != cargaId)
        {
            throw DomainException.Validation("La clave de idempotencia ya se usó con otra carga.");
        }

        // El resultado guardado trae datos de la carga, así que repetir la operación tampoco se salta la regla de
        // organización: solo la transportista de esa carga recibe su resultado. Si no, bastaría con adivinar un número.
        var transportista = await db.Cargas.AsNoTracking()
            .Where(c => c.CargaId == previa.CargaId)
            .Select(c => c.TransportistaTenantId)
            .FirstOrDefaultAsync(ct);

        if (!User.PerteneceA(transportista))
        {
            return Forbid();
        }

        return new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "application/json",
            Content = previa.Resultado
        };
    }
}
