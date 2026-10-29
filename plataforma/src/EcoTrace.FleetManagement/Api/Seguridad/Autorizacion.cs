using System.Security.Claims;

namespace EcoTrace.FleetManagement.Api.Seguridad;

/// <summary>Roles que emite Identity en el claim <c>role</c>.</summary>
public static class Roles
{
    public const string Conductor = "Conductor";
    public const string Supervisor = "Supervisor";
    public const string Administrador = "Administrador";

    /// <summary>Rol de los tokens de servicio (llamadas entre servicios). Ninguna persona lo tiene.</summary>
    public const string Servicio = "Servicio";
}

/// <summary>Políticas de autorización de Fleet (matriz de permisos de la especificación del Trabajo 3).</summary>
public static class Politicas
{
    /// <summary>Registrar vehículos y conductores: Supervisor o Administrador del transportista.</summary>
    public const string GestionaFlota = nameof(GestionaFlota);

    /// <summary>Rutas internas que solo llama otro servicio (el Saga de Billing), nunca la app.</summary>
    public const string Servicio = nameof(Servicio);
}

public static class UsuarioActual
{
    /// <summary>La organización sale del token, nunca de un parámetro del cliente.</summary>
    public static Guid? TenantId(this ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue("tenant_id"), out var id) ? id : null;

    /// <summary>El usuario de Identity que hace la solicitud (claim <c>sub</c>).</summary>
    public static Guid? UserId(this ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue("sub"), out var id) ? id : null;

    public static bool EsServicio(this ClaimsPrincipal usuario) => usuario.IsInRole(Roles.Servicio);

    /// <summary>
    /// Una persona solo ve los recursos de su organización. Un servicio los ve todos, porque el Saga
    /// trabaja con recursos de cualquier transportista y su token no trae organización.
    /// </summary>
    public static bool PuedeVer(this ClaimsPrincipal usuario, Guid tenantId) =>
        usuario.EsServicio() || usuario.TenantId() == tenantId;
}
