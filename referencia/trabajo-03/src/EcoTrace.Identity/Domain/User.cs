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

    /// <summary>Hash PBKDF2 de la contraseña (ver <see cref="Contrasenas"/>). Nulo en cuentas anteriores al Trabajo 3: no pueden iniciar sesión.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>Teléfono de contacto. Es el dato de perfil que el conductor cambia desde la app.</summary>
    public string? Telefono { get; private set; }

    /// <summary>
    /// Cambia el teléfono. Un formato inválido es un conflicto de tipo A (se puede corregir y reintentar); una organización
    /// suspendida mientras la app no tenía señal es de tipo B (el servidor decidió).
    /// </summary>
    public void ActualizarPerfil(string? telefono, bool organizacionActiva)
    {
        if (!organizacionActiva)
        {
            throw DomainException.Conflict("Tu organización fue suspendida mientras no había conexión; el perfil no se puede cambiar.");
        }

        var limpio = new string((telefono ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '(' or ')')).ToArray());
        if (!FormatoTelefono().IsMatch(limpio))
        {
            throw DomainException.Validation("El teléfono debe tener entre 7 y 15 dígitos, con un + opcional al inicio.");
        }

        Telefono = limpio;
    }

    [GeneratedRegex(@"^\+?[0-9]{7,15}$")]
    private static partial Regex FormatoTelefono();

    public static User Crear(
        Guid tenantId, int roleId, string? nombre, string? email, DateTime ahoraUtc, string? passwordHash = null)
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
            CreadoEn = ahoraUtc,
            PasswordHash = passwordHash
        };
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex FormatoEmail();
}
