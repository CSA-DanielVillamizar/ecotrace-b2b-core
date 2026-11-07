using System.Security.Claims;
using EcoTrace.Identity.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace EcoTrace.Identity.Api.Seguridad;

public static class Roles
{
    public const string Conductor = "Conductor";
    public const string Supervisor = "Supervisor";
    public const string Administrador = "Administrador";
    public const string Auditor = "Auditor";

    /// <summary>Rol de los tokens de servicio. Ningún usuario lo tiene.</summary>
    public const string Servicio = "Servicio";
}

public static class Claims
{
    public const string Rol = "role";
    public const string TenantId = "tenant_id";
}

public static class Politicas
{
    /// <summary>Administrador de cualquier organización (gestiona su propia organización).</summary>
    public const string Administrador = nameof(Administrador);

    /// <summary>Administrador de la organización operadora: gestiona todas las organizaciones.</summary>
    public const string AdministradorPlataforma = nameof(AdministradorPlataforma);

    /// <summary>Solo tokens de servicio: rutas internas que la app nunca llama.</summary>
    public const string Servicio = nameof(Servicio);

    /// <summary>Cualquier persona autenticada, pero no un servicio: datos de usuarios que ningún servicio necesita.</summary>
    public const string Usuario = nameof(Usuario);
}

public static class PrincipalExtensions
{
    /// <summary>La organización sale del token, nunca de un parámetro del cliente.</summary>
    public static Guid? TenantId(this ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(Claims.TenantId), out var id) ? id : null;

    public static bool EsServicio(this ClaimsPrincipal usuario) => usuario.IsInRole(Roles.Servicio);
}

/// <summary>Reglas de aislamiento por organización (Trabajo 3: el <c>tenant_id</c> manda).</summary>
public sealed class AccesoOrganizacion(IOptions<OpcionesOperador> operador)
{
    public bool EsPlataforma(ClaimsPrincipal usuario) =>
        usuario.IsInRole(Roles.Administrador) && usuario.TenantId() == operador.Value.TenantId;

    /// <summary>Ve cualquier organización: el administrador de la plataforma y los servicios.</summary>
    public bool VeTodo(ClaimsPrincipal usuario) => EsPlataforma(usuario) || usuario.EsServicio();

    public void ExigirAcceso(ClaimsPrincipal usuario, Guid tenantId)
    {
        if (!VeTodo(usuario) && usuario.TenantId() != tenantId)
        {
            throw DomainException.Forbidden("No tiene acceso a esa organización.");
        }
    }
}

internal sealed class RequisitoPlataforma : IAuthorizationRequirement;

internal sealed class ManejadorPlataforma(AccesoOrganizacion acceso) : AuthorizationHandler<RequisitoPlataforma>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext contexto, RequisitoPlataforma requisito)
    {
        if (acceso.EsPlataforma(contexto.User))
        {
            contexto.Succeed(requisito);
        }

        return Task.CompletedTask;
    }
}
