using System.Text.Json;

namespace EcoTrace.CargoTracking.Domain;

public enum EstadoOutbox
{
    /// <summary>Guardado junto con el cambio de negocio y todavía sin entregar al consumidor.</summary>
    Pendiente = 1,

    Publicado = 2,

    /// <summary>
    /// Agotó los reintentos o el consumidor lo rechazó de forma permanente (Dead Letter Queue del
    /// ADR 0002). Necesita a una persona: se inspecciona y se reprocesa a mano.
    /// </summary>
    Muerto = 3
}

/// <summary>
/// Mensaje pendiente de publicar (Transactional Outbox, ADR 0002). Se guarda en la misma transacción
/// que el cambio de negocio que lo origina: o quedan los dos o no queda ninguno. Un publicador
/// aparte lo entrega después, así que registrar la entrega nunca depende de que Billing esté arriba.
/// </summary>
public sealed class OutboxMensaje
{
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    private OutboxMensaje()
    {
        Tipo = string.Empty;
        Contenido = string.Empty;
        CorrelationId = string.Empty;
    }

    /// <summary>Identifica al mensaje y viaja como eventId: el consumidor lo usa para detectar duplicados.</summary>
    public Guid EventoId { get; private set; }

    public string Tipo { get; private set; }

    public string Contenido { get; private set; }

    /// <summary>Hilo de trazabilidad de la operación. Es texto y no un Guid: identifica una traza, no una entidad.</summary>
    public string CorrelationId { get; private set; }

    public EstadoOutbox Estado { get; private set; }

    public int Intentos { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ProximoIntentoEn { get; private set; }

    public DateTime? PublicadoEn { get; private set; }

    public string? UltimoError { get; private set; }

    /// <summary>Arma el evento EntregaConfirmada (ADR 0002, sección 4) a partir de una carga entregada.</summary>
    public static OutboxMensaje EntregaConfirmada(Carga carga, string correlationId, DateTime ahoraUtc)
    {
        if (carga.Estado != EstadoCarga.Entregado || carga.Asignacion is null)
        {
            throw DomainException.Conflict("Solo una carga entregada y asignada puede confirmar su entrega.");
        }

        var eventoId = Guid.NewGuid();
        var contenido = JsonSerializer.Serialize(new
        {
            eventId = eventoId,
            eventType = "EntregaConfirmada",
            occurredAt = ahoraUtc,
            cargaId = carga.CargaId,
            vehiculoId = carga.Asignacion.VehiculoId,
            conductorId = carga.Asignacion.ConductorId,
            generadorTenantId = carga.GeneradorTenantId,
            transportistaTenantId = carga.TransportistaTenantId,
            correlationId
        }, OpcionesJson);

        return new OutboxMensaje
        {
            EventoId = eventoId,
            Tipo = "EntregaConfirmada",
            Contenido = contenido,
            CorrelationId = correlationId,
            Estado = EstadoOutbox.Pendiente,
            CreadoEn = ahoraUtc,
            ProximoIntentoEn = ahoraUtc
        };
    }

    public void MarcarPublicado(DateTime ahoraUtc)
    {
        Estado = EstadoOutbox.Publicado;
        PublicadoEn = ahoraUtc;
        UltimoError = null;
    }

    /// <summary>
    /// Anota un intento fallido. Si el fallo es permanente, o ya se gastaron los intentos, el mensaje
    /// pasa a Muerto. Si no, se agenda otro intento con espera exponencial: base, 2·base, 4·base…
    /// </summary>
    public void RegistrarFallo(string error, bool permanente, int maximoIntentos, TimeSpan esperaBase, DateTime ahoraUtc)
    {
        Intentos++;
        UltimoError = error.Length > 300 ? error[..300] : error;

        if (permanente || Intentos >= maximoIntentos)
        {
            Estado = EstadoOutbox.Muerto;
            return;
        }

        ProximoIntentoEn = ahoraUtc + (esperaBase * Math.Pow(2, Intentos - 1));
    }

    /// <summary>Reproceso manual de un mensaje Muerto: vuelve a Pendiente con los intentos en cero.</summary>
    public void Reprocesar(DateTime ahoraUtc)
    {
        if (Estado != EstadoOutbox.Muerto)
        {
            throw DomainException.Conflict($"Solo un mensaje muerto se puede reprocesar. Este está {Estado}.");
        }

        Estado = EstadoOutbox.Pendiente;
        Intentos = 0;
        ProximoIntentoEn = ahoraUtc;
    }
}
