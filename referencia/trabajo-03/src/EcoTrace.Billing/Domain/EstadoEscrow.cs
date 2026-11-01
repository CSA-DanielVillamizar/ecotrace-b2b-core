namespace EcoTrace.Billing.Domain;

/// <summary>Estado de los fondos retenidos (ADR 0001, Issue #8).</summary>
public enum EstadoEscrow
{
    EnCustodia = 1,
    Liberado = 2,
    Reembolsado = 3,

    /// <summary>
    /// La liberación se revirtió porque el Saga falló más adelante (ADR 0003, compensación de Billing).
    /// Los fondos siguen retenidos hasta que una persona resuelva la disputa.
    /// </summary>
    EnDisputa = 4
}
