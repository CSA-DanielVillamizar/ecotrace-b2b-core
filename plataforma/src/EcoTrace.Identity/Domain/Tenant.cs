namespace EcoTrace.Identity.Domain;

/// <summary>
/// Cuenta de una empresa en la plataforma. Es la unica entidad que otros contextos
/// referencian (por TenantId) y nunca al reves.
/// </summary>
public sealed class Tenant
{
    private Tenant()
    {
        Nombre = string.Empty;
    }

    public Guid TenantId { get; private set; }

    public string Nombre { get; private set; }

    public TenantType TenantType { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public static Tenant Crear(string? nombre, TenantType tenantType, DateTime ahoraUtc)
    {
        var limpio = (nombre ?? string.Empty).Trim();
        if (limpio.Length is < 2 or > 120)
        {
            throw DomainException.Validation("El nombre de la organización debe tener entre 2 y 120 caracteres.");
        }

        if (!Enum.IsDefined(tenantType))
        {
            throw DomainException.Validation("El tipo de organización debe ser Generador o Transportista.");
        }

        return new Tenant
        {
            TenantId = Guid.NewGuid(),
            Nombre = limpio,
            TenantType = tenantType,
            CreadoEn = ahoraUtc
        };
    }
}
