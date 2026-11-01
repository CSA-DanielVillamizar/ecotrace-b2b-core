using System.Security.Cryptography;

namespace EcoTrace.Identity.Domain;

/// <summary>
/// Contraseñas con PBKDF2-SHA256 de la biblioteca base de .NET (el dominio no puede depender de paquetes).
/// Lo que se guarda lleva su propia sal y su número de iteraciones, así que subir las iteraciones más adelante
/// no invalida los hashes viejos: cada uno se verifica con los parámetros con los que se creó.
/// Formato: <c>pbkdf2-sha256$iteraciones$sal$hash</c> (sal y hash en Base64).
/// </summary>
public static class Contrasenas
{
    public const int LongitudMinima = 10;
    public const int IteracionesPorDefecto = 210_000;

    private const string Prefijo = "pbkdf2-sha256";
    private const int BytesDeSal = 16;
    private const int BytesDeHash = 32;

    /// <summary>Valida la política: 10 caracteres como mínimo y un máximo para no regalar un vector de denegación de servicio.</summary>
    public static string Validar(string? contrasena)
    {
        if (string.IsNullOrWhiteSpace(contrasena) || contrasena.Length < LongitudMinima)
        {
            throw DomainException.Validation($"La contraseña debe tener al menos {LongitudMinima} caracteres.");
        }

        if (contrasena.Length > 200)
        {
            throw DomainException.Validation("La contraseña no puede pasar de 200 caracteres.");
        }

        return contrasena;
    }

    public static string Hashear(string contrasena, int iteraciones = IteracionesPorDefecto)
    {
        var sal = RandomNumberGenerator.GetBytes(BytesDeSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iteraciones, HashAlgorithmName.SHA256, BytesDeHash);
        return $"{Prefijo}${iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Compara en tiempo constante. Un hash ausente o mal formado nunca verifica.</summary>
    public static bool Verificar(string contrasena, string? guardado)
    {
        if (string.IsNullOrEmpty(guardado))
        {
            return false;
        }

        var partes = guardado.Split('$');
        if (partes.Length != 4 || partes[0] != Prefijo || !int.TryParse(partes[1], out var iteraciones) || iteraciones < 1)
        {
            return false;
        }

        try
        {
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Refresh token emitido a un usuario. En la base solo vive su hash SHA-256: quien lea la base no puede usarlo.
/// Se puede revocar (cierre de sesión) y vence a los 7 días.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public Guid RefreshTokenId { get; private set; }

    /// <summary>Usuario dueño. Clave foránea real: User vive en este mismo contexto.</summary>
    public Guid UserId { get; private set; }

    /// <summary>SHA-256 del token, en hexadecimal.</summary>
    public string TokenHash { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ExpiraEn { get; private set; }

    public DateTime? RevocadoEn { get; private set; }

    public static RefreshToken Emitir(Guid userId, string tokenHash, DateTime ahoraUtc, TimeSpan duracion) => new()
    {
        RefreshTokenId = Guid.NewGuid(),
        UserId = userId,
        TokenHash = tokenHash,
        CreadoEn = ahoraUtc,
        ExpiraEn = ahoraUtc + duracion
    };

    public bool EstaVigente(DateTime ahoraUtc) => RevocadoEn is null && ahoraUtc < ExpiraEn;

    /// <summary>Revoca el token. Es idempotente: devuelve true solo si cambió algo.</summary>
    public bool Revocar(DateTime ahoraUtc)
    {
        if (RevocadoEn is not null)
        {
            return false;
        }

        RevocadoEn = ahoraUtc;
        return true;
    }
}
