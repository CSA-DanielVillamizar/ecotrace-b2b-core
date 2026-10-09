namespace EcoTrace.Identity.Domain;

/// <summary>
/// Estado operativo de una organización. Solo una organización Activa puede recibir una
/// autorización de pago (ADR 0002, "Billing → Identity.ValidateTenantStatus").
/// </summary>
public enum EstadoTenant
{
    Activo = 1,
    Suspendido = 2
}
