namespace EcoTrace.Billing.Domain.Constants;

public static class ErrorMessages
{
    public static class Generic
    {
        public const string AmountMustBeGreaterThanZero = "El monto debe ser mayor a cero.";
        public const string TenantsMustBeDifferent = "El tenant generador y el transportista deben ser distintos.";
    }

    public static class Payment
    {
        public const string InvalidStatusTransitionFormat = "Transición de estado inválida: {0} -> {1}.";
    }

    public static class FinancialAudit
    {
        public const string ActionRequired = "La acción de auditoría es obligatoria.";
    }
}
