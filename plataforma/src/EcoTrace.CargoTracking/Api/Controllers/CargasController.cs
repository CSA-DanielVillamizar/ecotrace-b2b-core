using EcoTrace.CargoTracking.Api.Contracts;
using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Api.Controllers;

[ApiController]
[Route("api/cargas")]
[Produces("application/json")]
public sealed class CargasController(CargoTrackingDbContext db, TimeProvider reloj) : ControllerBase
{
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
    ///
    /// Trabajo 2 (patrón Outbox, ADR 0002): cuando el nuevo estado es Entregado, este endpoint
    /// también crea un MensajeOutbox -- en la MISMA transacción que el Seguimiento, gracias a
    /// que ambos cambios se guardan en un solo GuardarAsync al final del método. Esto es lo que
    /// garantiza que nunca exista una entrega registrada sin su evento correspondiente, ni
    /// viceversa: si algo falla a mitad de camino, PostgreSQL/SQLite revierte los dos cambios
    /// juntos.
    ///
    /// Por qué la idempotencia de "Entregado repetido" no se valida aquí explícitamente: ya la
    /// resuelve Carga.TransicionPermitida, que no tiene un caso (Entregado, Entregado) -- un
    /// segundo intento de RegistrarSeguimiento con Estado = Entregado ya lanza
    /// DomainException.Conflict (409) ANTES de llegar a este método, sin que el controlador
    /// tenga que repetir esa comprobación.
    /// </summary>
    [HttpPost("{id:guid}/seguimientos")]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargaDetalleResponse>> RegistrarSeguimiento(
        Guid id, RegistrarSeguimientoRequest solicitud, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: true, ct);
        var ahoraUtc = reloj.GetUtcNow().UtcDateTime;

        // Si la transición no es válida (por ejemplo, un Entregado repetido, o
        // saltarse un estado), esto ya lanza DomainException.Conflict -- 409 --
        // y el método termina aquí: nunca se llega a crear un MensajeOutbox
        // para una transición que en realidad no ocurrió.
        carga.RegistrarSeguimiento(solicitud.Estado!.Value, solicitud.Ubicacion, solicitud.Nota, ahoraUtc);

        if (solicitud.Estado == EstadoCarga.Entregado)
        {
            // Estructuralmente garantizado, no una suposición: la única forma de
            // que Carga.Estado llegue a valer algo distinto de Pendiente es a
            // través de Asignar() (ver Carga.TransicionPermitida), que es quien
            // establece Asignacion. Por lo tanto, si el código llegó hasta acá
            // sin que RegistrarSeguimiento lanzara una excepción, Asignacion
            // nunca puede ser null.
            var asignacion = carga.Asignacion!;

            // Aquí es donde nace el evento que dispara el Saga "Liberar Pago en
            // Escrow" (ADR 0003) del lado de Billing. Cargo & Tracking no sabe
            // nada de pagos ni ejecuta ningún paso del Saga -- solo garantiza
            // que este evento se genere de forma confiable.
            db.MensajesOutbox.Add(MensajeOutbox.Crear(
                carga.CargaId,
                asignacion.VehiculoId,
                asignacion.ConductorId,
                carga.GeneradorTenantId,
                carga.TransportistaTenantId,
                ObtenerOGenerarCorrelationId(),
                ahoraUtc));
        }

        // Una sola llamada: el Seguimiento (ya trackeado dentro de carga, por
        // la lista interna de Carga) y el MensajeOutbox (si se creó arriba) se
        // guardan en la MISMA transacción. Esta línea es, literalmente, la
        // definición del patrón Transactional Outbox.
        await db.GuardarAsync("No se pudo registrar el seguimiento.", ct);

        return CreatedAtAction(nameof(Obtener), new { id }, CargaDetalleResponse.De(carga));
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
    /// Lee X-Correlation-Id de la petición entrante, o genera uno nuevo si no viene o no es
    /// válido. La especificación limita su formato ("hasta 64 caracteres: letras, números,
    /// guion y guion bajo") y exige que todo servicio lo devuelva en la respuesta y lo use en
    /// sus registros -- así, todas las llamadas de un mismo Saga pueden rastrearse con el mismo
    /// identificador de punta a punta, sin importar por cuál de los cuatro servicios haya
    /// empezado la traza.
    /// </summary>
    private string ObtenerOGenerarCorrelationId()
    {
        if (Request.Headers.TryGetValue("X-Correlation-Id", out var valor))
        {
            var texto = valor.ToString();
            var esValido = !string.IsNullOrWhiteSpace(texto)
                && texto.Length <= 64
                && texto.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');

            if (esValido)
            {
                Response.Headers["X-Correlation-Id"] = texto;
                return texto;
            }
        }

        var nuevo = Guid.NewGuid().ToString();
        Response.Headers["X-Correlation-Id"] = nuevo;
        return nuevo;
    }
}
