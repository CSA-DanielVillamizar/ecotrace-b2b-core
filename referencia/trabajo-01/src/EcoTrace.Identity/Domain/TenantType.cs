namespace EcoTrace.Identity.Domain;

/// <summary>
/// Lado del marketplace al que pertenece una organizacion (ADR 0001, seccion de multi-tenencia).
/// Es extensible: Auditor o EntidadFinanciera pueden sumarse mas adelante sin tocar el resto del modelo.
/// </summary>
public enum TenantType
{
    Generador = 1,
    Transportista = 2
}
