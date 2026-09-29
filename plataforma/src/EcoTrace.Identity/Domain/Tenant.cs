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

    public EstadoTenant Estado { get; private set; } = EstadoTenant.Activo;

    /// <summary>Sube solo cuando Estado cambia de verdad (Trabajo 2: contrato de la especificacion).</summary>
    public int Version { get; private set; } = 1;

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

    /// <summary>Idempotente: si ya estaba Suspendido, no cambia nada ni sube la version.</summary>
    public void Suspender()
    {
        if (Estado == EstadoTenant.Suspendido)
        {
            return;
        }

        Estado = EstadoTenant.Suspendido;
        Version++;
    }

    /// <summary>Idempotente: si ya estaba Activo, no cambia nada ni sube la version.</summary>
    public void Reactivar()
    {
        if (Estado == EstadoTenant.Activo)
        {
            return;
        }

        Estado = EstadoTenant.Activo;
        Version++;
    }

    /// <summary>Regla del paso 1 del Saga (ADR 0003): una organizacion suspendida no puede autorizar pagos nuevos.</summary>
    public void AsegurarPuedeAutorizarPago()
    {
        if (TenantType != TenantType.Transportista)
        {
            throw DomainException.Validation("Solo un Transportista puede autorizar un pago.");
        }

        if (Estado == EstadoTenant.Suspendido)
        {
            throw DomainException.Conflict("La organización está suspendida y no puede autorizar pagos.");
        }
    }
}
