namespace EcoTrace.Identity.Domain;

/// <summary>Estado operativo de una organizacion. Suspendida no puede autorizar nuevos pagos (Trabajo 2).</summary>
public enum EstadoTenant
{
    Activo,
    Suspendido
}
