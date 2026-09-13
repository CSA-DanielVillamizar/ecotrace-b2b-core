namespace EcoTrace.Identity.Domain;

// Refleja la distinción de multi-tenencia definida en ADR 0001 (dos lados del marketplace).
public enum TenantType
{
    Generador,
    Transportista
}
