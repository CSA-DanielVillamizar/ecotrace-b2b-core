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

    /// <summary>Teléfono de contacto, normalizado (solo dígitos, con + opcional). Null si no lo ha dado.</summary>
    public string? Telefono { get; private set; }

    public DateTime CreadoEn { get; private set; }

    /// <summary>
    /// Hash de la contraseña (PBKDF2, lo calcula la capa Api). Null si el usuario aún no tiene contraseña:
    /// en ese caso no puede iniciar sesión. La contraseña en claro nunca llega al dominio ni a la base.
    /// </summary>
    public string? ContrasenaHash { get; private set; }

    public void AsignarContrasenaHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            throw DomainException.Validation("El hash de la contraseña no puede estar vacío.");
        }

        ContrasenaHash = hash;
    }

    /// <summary>Valida la política mínima de contraseña antes de calcular su hash.</summary>
    public static void ValidarContrasena(string? contrasena)
    {
        if (contrasena is null || contrasena.Length is < 8 or > 128)
        {
            throw DomainException.Validation("La contraseña debe tener entre 8 y 128 caracteres.");
        }
    }
    public static User Crear(Guid tenantId, int roleId, string? nombre, string? email, DateTime ahoraUtc)
    {
        var nombreLimpio = ValidarNombre(nombre);

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

    /// <summary>
    /// Cambia los datos de perfil que el propio usuario puede editar desde la app (ADR 0005). El correo no se
    /// cambia aquí porque es el usuario del inicio de sesión. Un teléfono vacío lo borra.
    /// </summary>
    public void ActualizarPerfil(string? nombre, string? telefono)
    {
        var nombreLimpio = ValidarNombre(nombre);
        var telefonoLimpio = NormalizarTelefono(telefono);

        Nombre = nombreLimpio;
        Telefono = telefonoLimpio;
    }

    private static string ValidarNombre(string? nombre)
    {
        var limpio = (nombre ?? string.Empty).Trim();
        if (limpio.Length is < 2 or > 120)
        {
            throw DomainException.Validation("El nombre del usuario debe tener entre 2 y 120 caracteres.");
        }

        return limpio;
    }

    private static string? NormalizarTelefono(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono))
        {
            return null;
        }

        // Se aceptan espacios, guiones y paréntesis al escribirlo; se guarda solo con dígitos y el + inicial.
        var compacto = SeparadoresTelefono().Replace(telefono.Trim(), string.Empty);
        if (!FormatoTelefono().IsMatch(compacto))
        {
            throw DomainException.Validation("El teléfono debe tener entre 7 y 15 dígitos, con un + opcional al inicio.");
        }

        return compacto;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex FormatoEmail();

    [GeneratedRegex(@"^\+?[0-9]{7,15}$")]
    private static partial Regex FormatoTelefono();

    [GeneratedRegex(@"[\s\-\(\)\.]")]
    private static partial Regex SeparadoresTelefono();
}
