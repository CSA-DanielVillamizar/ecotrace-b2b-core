namespace EcoTrace.Billing.Domain;

/// <summary>
/// Bandeja de entrada (Inbox): un registro por cada evento ya procesado. Si el mismo eventId llega
/// otra vez, porque el productor reenvió o porque la red duplicó la entrega, se reconoce aquí y no
/// se vuelve a hacer nada. Es la mitad de la idempotencia; la otra mitad es la clave única por pago.
/// </summary>
public sealed class EventoRecibido
{
    private EventoRecibido()
    {
        Tipo = string.Empty;
    }

    public Guid EventoId { get; private set; }

    public string Tipo { get; private set; }

    public DateTime RecibidoEn { get; private set; }

    public static EventoRecibido Registrar(Guid eventoId, string tipo, DateTime ahoraUtc) =>
        new() { EventoId = eventoId, Tipo = tipo, RecibidoEn = ahoraUtc };
}
