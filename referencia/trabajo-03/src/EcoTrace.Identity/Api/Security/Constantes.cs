using System.Security.Claims;

namespace EcoTrace.Identity.Api.Security;

/// <summary>Las audiencias (<c>aud</c>) del ADR 0004: un token solo sirve para los módulos que lista.</summary>
public static class Audiencias
{
    public const string Identity = "ecotrace-identity";
    public const string Fleet = "ecotrace-fleet";
    public const string Cargo = "ecotrace-cargo";
    public const string Billing = "ecotrace-billing";

    public static readonly string[] Todas = [Identity, Fleet, Cargo, Billing];
}

public static class Roles
{
    public const string Conductor = "Conductor";
    public const string Supervisor = "Supervisor";
    public const string Administrador = "Administrador";
    public const string Auditor = "Auditor";

    /// <summary>Rol de las llamadas entre servicios. Ninguna persona lo tiene.</summary>
    public const string Servicio = "Servicio";
}

/// <summary>Nombres de los claims del token. Se usan tal cual (sin el mapeo de nombres de .NET).</summary>
public static class Reclamos
{
    public const string Sub = "sub";
    public const string TenantId = "tenant_id";
    public const string Rol = "role";

    /// <summary>"true" solo para el administrador de la organización que opera la plataforma.</summary>
    public const string Plataforma = "plataforma";
}

public static class Politicas
{
    /// <summary>Solo llamadas entre servicios (rol Servicio).</summary>
    public const string Servicio = "Servicio";

    /// <summary>Administrador de la organización que opera la plataforma. Es el único con poder sobre todas las organizaciones.</summary>
    public const string Operador = "Operador";

    /// <summary>Operador o llamada entre servicios: las rutas de operación que también usa un servicio.</summary>
    public const string OperadorOServicio = "OperadorOServicio";
}

/// <summary>Lectura de los datos de quien llama, siempre desde el token y nunca desde un parámetro del cliente.</summary>
public static class UsuarioActual
{
    public static string? Sub(this ClaimsPrincipal usuario) => usuario.FindFirstValue(Reclamos.Sub);

    public static string? Rol(this ClaimsPrincipal usuario) => usuario.FindFirstValue(Reclamos.Rol);

    public static Guid? TenantId(this ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(Reclamos.TenantId), out var id) ? id : null;

    public static bool EsServicio(this ClaimsPrincipal usuario) => usuario.Rol() == Roles.Servicio;

    public static bool EsOperador(this ClaimsPrincipal usuario) =>
        usuario.Rol() == Roles.Administrador && usuario.FindFirstValue(Reclamos.Plataforma) == "true";

    public static bool PerteneceA(this ClaimsPrincipal usuario, params Guid[] organizaciones) =>
        usuario.TenantId() is { } propia && organizaciones.Contains(propia);
}
