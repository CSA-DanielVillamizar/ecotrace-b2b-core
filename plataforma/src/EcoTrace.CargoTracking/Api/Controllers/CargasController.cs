using System.Text.Json;
using EcoTrace.CargoTracking.Api.Contracts;
using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EcoTrace.CargoTracking.Api.Controllers;

/// <param name="opcionesJson">
/// Las mismas opciones de JSON que usa la API para responder (nombres en camelCase y enums como texto).
/// Se usan para guardar el resultado de una operación con exactamente la forma en que se le respondió al
/// cliente, así una repetición devuelve lo mismo. Se escribe el nombre completo del tipo porque existe otro
/// <c>JsonOptions</c> para endpoints mínimos y no debe confundirse.
/// </param>
[ApiController]
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

    /// <summary>Crea una carga en estado Pendiente, con generador y transportista.</summary>
    [HttpPost]
    [ProducesResponseType<CargaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CargaResponse>> Crear(CrearCargaRequest solicitud, CancellationToken ct)
    {
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

    /// <summary>Lista las cargas. El filtro por organización coincide con generador o transportista.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<CargaResponse>> Listar(
        [FromQuery] Guid? tenantId, [FromQuery] EstadoCarga? estado, CancellationToken ct)
    {
        var consulta = db.Cargas.AsNoTracking();
        if (tenantId is not null)
        {
            consulta = consulta.Where(c => c.GeneradorTenantId == tenantId || c.TransportistaTenantId == tenantId);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(c => c.Estado == estado);
        }

        var cargas = await consulta.OrderByDescending(c => c.CreadoEn).ToListAsync(ct);
        return cargas.Select(CargaResponse.De).ToList();
    }

    /// <summary>Devuelve la carga con su asignación y su línea de seguimiento.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CargaDetalleResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: false, ct);
        return CargaDetalleResponse.De(carga);
    }

    /// <summary>Asigna vehículo y conductor (identificadores de Fleet Management). Solo desde Pendiente.</summary>
    [HttpPost("{id:guid}/asignacion")]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargaDetalleResponse>> Asignar(
        Guid id, AsignarCargaRequest solicitud, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: true, ct);
        carga.Asignar(solicitud.VehiculoId!.Value, solicitud.ConductorId!.Value, reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("La carga ya tiene una asignación.", ct);

        return CreatedAtAction(nameof(Obtener), new { id }, CargaDetalleResponse.De(carga));
    }

    /// <summary>
    /// Registra un evento de seguimiento y actualiza el estado de la carga.
    /// <para>
    /// Es la ruta que usa la app del conductor para confirmar una entrega, así que es idempotente
    /// (ADR 0005, Trabajo 3). Si llega el encabezado <c>Idempotency-Key</c>:
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
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
                return RespuestaDeOperacionRepetida(previa, id);
            }
        }

        // 3. Primera vez que llega esta operación: se procesa igual que siempre. Todo el bloque va dentro del
        //    try porque un reintento puede chocar en cualquier punto de él (ver el catch más abajo).
        try
        {
            var carga = await CargarAsync(id, tracking: true, ct);
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

            return RespuestaDeOperacionRepetida(ganadora, id);
        }
    }

    /// <summary>Línea de tiempo de la carga, del evento más antiguo al más reciente.</summary>
    [HttpGet("{id:guid}/seguimientos")]
    public async Task<IReadOnlyList<SeguimientoResponse>> ListarSeguimientos(Guid id, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: false, ct);
        return CargaDetalleResponse.De(carga).Seguimientos;
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
    private ContentResult RespuestaDeOperacionRepetida(OperacionProcesada previa, Guid cargaId)
    {
        // Una misma clave no puede servir para dos cargas distintas: sería un error del cliente y devolver
        // el resultado de otra carga mostraría datos que no corresponden.
        if (previa.CargaId != cargaId)
        {
            throw DomainException.Validation("La clave de idempotencia ya se usó con otra carga.");
        }

        return new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "application/json",
            Content = previa.Resultado
        };
    }
}
