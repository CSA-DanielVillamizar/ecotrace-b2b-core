namespace EcoTrace.Billing.Domain;

public sealed class EventoEntregaConfirmada
{
    private EventoEntregaConfirmada()
    {
        EventId = string.Empty;
        EventType = string.Empty;
        CorrelationId = string.Empty;
    }

    public string EventId { get; private set; }

    public string EventType { get; private set; }

    public DateTime? OccurredAt { get; private set; }

    public Guid SagaId { get; private set; }

    public string CorrelationId { get; private set; }

    public DateTime RecibidoEn { get; private set; }

    public static EventoEntregaConfirmada Crear(
        string eventId,
        string eventType,
        DateTime? occurredAt,
        Guid sagaId,
        string correlationId,
        DateTime recibidoEn) => new()
    {
        EventId = eventId,
        EventType = eventType,
        OccurredAt = occurredAt,
        SagaId = sagaId,
        CorrelationId = correlationId,
        RecibidoEn = recibidoEn
    };
}