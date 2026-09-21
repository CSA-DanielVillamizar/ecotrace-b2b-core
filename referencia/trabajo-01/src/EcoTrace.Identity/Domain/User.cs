using System.Text.RegularExpressions;

namespace EcoTrace.Identity.Domain;

/// <summary>
/// Cuenta de una persona. Pertenece a exactamente un Tenant (ADR 0001).
/// TenantId y RoleId son claves foraneas reales porque Tenant y Role viven en este mismo contexto.
/// </summary>
public sealed partial class User
{
    private User()
    {
        Nombre = string.Empty;
        Email = string.Empty;
    }

    public Guid UserId { get; private set; }

    public Guid TenantId { get; private set; }

    public Tenant? Tenant { get; private set; }

    public int RoleId { get; private set; }

    public Role? Role { get; private set; }

    public string Nombre { get; private set; }

    public string Email { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public static User Crear(Guid tenantId, int roleId, string? nombre, string? email, DateTime ahoraUtc)
    {
        var nombreLimpio = (nombre ?? string.Empty).Trim();
        if (nombreLimpio.Length is < 2 or > 120)
        {
            throw DomainException.Validation("El nombre del usuario debe tener entre 2 y 120 caracteres.");
        }

        var emailNormalizado = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (emailNormalizado.Length > 200 || !FormatoEmail().IsMatch(emailNormalizado))
        {
            throw DomainException.Validation("El correo electrónico no tiene un formato válido.");
        }

        return new User
        {
            UserId = Guid.NewGuid(),
            TenantId = tenantId,
            RoleId = roleId,
            Nombre = nombreLimpio,
            Email = emailNormalizado,
            CreadoEn = ahoraUtc
        };
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex FormatoEmail();
}
