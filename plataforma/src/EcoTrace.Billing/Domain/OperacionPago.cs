namespace EcoTrace.Billing.Domain;

public sealed class OperacionPago
{
    public Guid OperacionPagoId { get; private set; }

    public Guid PagoId { get; private set; }

    public string IdempotencyKey { get; private set; } = string.Empty;

    public string Operacion { get; private set; } = string.Empty;

    public DateTime CreadaEn { get; private set; }

    public static OperacionPago Registrar(Guid pagoId, string idempotencyKey, string operacion, DateTime ahoraUtc)
    {
        if (pagoId == Guid.Empty)
        {
            throw DomainException.Validation("La operación requiere un identificador de pago.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw DomainException.Validation("La operación requiere una clave de idempotencia.");
        }

        return new OperacionPago
        {
            OperacionPagoId = Guid.NewGuid(),
            PagoId = pagoId,
            IdempotencyKey = idempotencyKey.Trim(),
            Operacion = operacion,
            CreadaEn = ahoraUtc
        };
    }
}
