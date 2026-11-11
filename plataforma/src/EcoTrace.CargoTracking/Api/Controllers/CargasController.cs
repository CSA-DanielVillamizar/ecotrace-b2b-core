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
/// Cargas y su seguimiento. Todas las rutas exigen un token válido (ADR 0004). Cada una, además, dice qué roles pueden usarla
/// y de qué organización tienen que ser: la organización se lee <b>del token</b>, nunca de lo que mande el cliente.
/// <list type="bullet">
/// <item>Quien crea la carga es de la organización generadora.</item>
/// <item>Quien la asigna y quien registra su seguimiento (incluida la entrega) es de la organización transportista.</item>
/// <item>Quien consulta una carga es de una de las dos organizaciones.</item>
/// </list>
/// Un token válido de otra organización recibe 403, y un rol sin permiso también.
/// </summary>
/// <param name="opcionesJson">
/// Las mismas opciones de JSON que usa la API para responder (nombres en camelCase y enums como texto).
/// Se usan para guardar el resultado de una operación con exactamente la forma en que se le respondió al
/// cliente, así una repetición devuelve lo mismo. Se escribe el nombre completo del tipo porque existe otro
/// <c>JsonOptions</c> para endpoints mínimos y no debe confundirse.
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
    /// Encabezado con el que la app manda el <c>OperationId</c> de la acción (ADR 0005). Lo agrega el motor de
    /// sincronización del teléfono; un cliente que no lo manda (Swagger, la consola web) funciona como antes.
    /// </summary>
    private const string EncabezadoIdempotencia = "Idempotency-Key";

    /// <summary>
    /// Crea una carga en estado Pendiente, con generador y transportista. La crea alguien de la organización generadora:
    /// la organización que dice el cuerpo tiene que ser la del token.
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
    /// <c>tenantId</c> se acepta solo si coincide con esa organización: nadie puede pedir las cargas de otra.
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

    /// <summary>Devuelve la carga con su asignación y su línea de seguimiento, si quien pregunta es de una de sus dos organizaciones.</summary>
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

    /// <summary>Asigna vehículo y conductor (identificadores de Fleet Management). Solo desde Pendiente, y solo la organización transportista.</summary>
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
    /// Registra un evento de seguimiento y actualiza el estado de la carga. Lo hace un Conductor o un Supervisor de la
    /// organización transportista de esa carga. Es la ruta con la que se confirma una entrega, la operación más crítica:
    /// de ahí arranca el Saga que libera el pago.
    /// <para>
    /// Es idempotente (ADR 0005, Trabajo 3). Si llega el encabezado <c>Idempotency-Key</c>:
    /// </para>
    /// <list type="bullet">
    /// <item>La primera vez se procesa normal y responde <b>201</b>. Junto con el cambio se guarda un
    /// registro de la operación, en la misma transacción.</item>
    /// <item>Si la misma clave llega otra vez (un reintento tras cortarse la red), responde <b>200</b> con
    /// el resultado de la primera vez y no hace nada más: no crea otro seguimiento y, sobre todo, no crea
    /// otro mensaje en el Outbox, así que el Saga de pago no se dispara dos veces.</item>
    /// </list>
    /// <para>
    /// Sin el encabezado funciona como antes: un "Entregado" repetido responde 409.
    /// </para>
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
        // 1. Se lee la clave antes de tocar nada. Si viene mal formada se rechaza con 400 (tipo A).
        var operacionId = LeerClaveDeIdempotencia(claveIdempotencia);

        // 2. ¿Esta operación ya se procesó? Entonces no se repite nada: se devuelve el resultado anterior.
        //    Esto va antes de cargar la carga a propósito: aunque la carga ya haya cambiado de estado
        //    (por ejemplo, está Entregada), el reintento de una acción que SÍ se hizo recibe su 200 y no un 409.
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

        // 3. Primera vez que llega esta operación: se procesa igual que siempre. Todo el bloque va dentro del
        //    try porque un reintento puede chocar en cualquier punto de él (ver el catch más abajo).
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

            // Transactional Outbox (ADR 0002): el evento se guarda en la misma transaccion que el cambio
            // de estado. Si la entrega queda registrada, el evento tambien; si no, ninguno de los dos.
            if (carga.Estado == EstadoCarga.Entregado)
            {
                db.Outbox.Add(OutboxMensaje.EntregaConfirmada(carga, HttpContext.CorrelationId(), ahora));
            }

            var respuesta = CargaDetalleResponse.De(carga);

            // 4. Se anota la operación como procesada, con la respuesta que se va a dar, en la MISMA
            //    transacción que el cambio y el Outbox. Los tres quedan guardados juntos o ninguno.
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
            // Caso de carrera: llegó otra solicitud con la MISMA clave casi al mismo tiempo y terminó antes.
            // Se nota en uno de dos momentos. O al evaluar la transición, porque la carga ya aparece como
            // Entregada y el dominio lanza el conflicto. O al guardar, porque la base de datos deja pasar
            // solo una (la clave primaria es el OperationId y el estado de la carga es un token de
            // concurrencia). En ambos casos, si esta perdedora era un reintento, la operación SÍ se hizo, así
            // que se le responde con ese resultado (200) y no con un 409 que la app tomaría por un rechazo.
            db.ChangeTracker.Clear();
            var ganadora = await db.OperacionesProcesadas
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.OperacionId == operacionId!.Value, ct);

            if (ganadora is null)
            {
                // El conflicto no venía de una clave repetida (por ejemplo, la carga ya estaba entregada por
                // otra operación con otra clave): es un conflicto real y se informa como tal.
                throw;
            }

            return await RespuestaDeOperacionRepetidaAsync(ganadora, id, ct);
        }
    }

    /// <summary>Línea de tiempo de la carga, del evento más antiguo al más reciente, para quien sea de una de sus dos organizaciones.</summary>
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

    /// <summary>True si quien llama es de la organización generadora o de la transportista de esta carga.</summary>
    private bool EsDeLaCarga(Carga carga) =>
        User.PerteneceA(carga.GeneradorTenantId, carga.TransportistaTenantId);

    /// <summary>
    /// Convierte el encabezado <c>Idempotency-Key</c> en un identificador. Devuelve null si el encabezado no
    /// vino (el cliente no pidió idempotencia). El <c>OperationId</c> es un Guid generado en el teléfono, así
    /// que cualquier otro valor se rechaza con 400 en lugar de aceptarse y guardarse sin sentido.
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
    /// Respuesta para una operación que ya se había procesado: 200 con el JSON de la primera vez, tal cual
    /// se guardó. El 200 (en vez del 201 de la primera vez) es la señal de "ya estaba hecho".
    /// </summary>
    private async Task<ActionResult> RespuestaDeOperacionRepetidaAsync(OperacionProcesada previa, Guid cargaId, CancellationToken ct)
    {
        // Una misma clave no puede servir para dos cargas distintas: sería un error del cliente y devolver
        // el resultado de otra carga mostraría datos que no corresponden.
        if (previa.CargaId != cargaId)
        {
            throw DomainException.Validation("La clave de idempotencia ya se usó con otra carga.");
        }

        // El resultado guardado trae los datos de la carga, así que repetir la operación tampoco se salta la regla de
        // organización: solo la transportista de esa carga recibe su resultado. Si no, bastaría con adivinar una clave.
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
