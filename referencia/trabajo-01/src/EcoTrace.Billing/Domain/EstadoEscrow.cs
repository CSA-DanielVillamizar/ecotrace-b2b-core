namespace EcoTrace.Billing.Domain;

/// <summary>Estado de los fondos retenidos (ADR 0001, Issue #8).</summary>
public enum EstadoEscrow
{
    EnCustodia = 1,
    Liberado = 2,
    Reembolsado = 3
}
