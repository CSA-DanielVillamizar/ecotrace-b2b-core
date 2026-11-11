using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Tests.TestSupport;

/// <summary>Los nombres de rol, para que las pruebas no escriban cadenas sueltas.</summary>
public static class Rol
{
    public const string Conductor = "Conductor";
    public const string Supervisor = "Supervisor";
    public const string Administrador = "Administrador";
    public const string Auditor = "Auditor";
    public const string Servicio = "Servicio";
}

/// <summary>
/// Quién es quién en las pruebas de seguridad. Las pruebas conocen la clave privada del Identity de prueba, así que pueden
/// emitir el token de cualquier persona (rápido, sin pasar por el inicio de sesión) y también tokens mal hechos, para
/// comprobar que Cargo &amp; Tracking los rechaza. La clave la genera cada ejecución: no hay ninguna clave en el repositorio.
/// </summary>
public static class SeguridadDePrueba
{
    /// <summary>El emisor del Identity de prueba. Distinto del de desarrollo y del de producción.</summary>
    public const string Emisor = "https://identity.ecotrace.local/prueba";

    /// <summary>El secreto con el que Cargo se identifica ante el Identity de prueba para pedir tokens de servicio.</summary>
    public const string SecretoCargo = "secreto-cargo-de-prueba-0123";

    /// <summary>Nombres de los clientes HTTP de Cargo que las pruebas reemplazan por el Identity de mentira.</summary>
    public const string ClienteJwks = "identity-jwks";

    public const string ClienteToken = "identity-token";

    public const string AudIdentity = "ecotrace-identity";
    public const string AudFleet = "ecotrace-fleet";
    public const string AudCargo = "ecotrace-cargo";
    public const string AudBilling = "ecotrace-billing";

    public static readonly string[] TodasLasAudiencias = [AudIdentity, AudFleet, AudCargo, AudBilling];

    private static readonly JsonWebTokenHandler Manejador = new();

    /// <summary>La clave con la que el Identity de prueba firma sus tokens.</summary>
    public static RSA ClaveDeIdentity { get; } = RSA.Create(2048);

    /// <summary>El identificador de esa clave, que va en la cabecera de cada token y en el JWKS.</summary>
    public static string Kid { get; } = KidDe(ClaveDeIdentity);

    /// <summary>Configura una API de prueba: a quién le cree (el emisor de prueba) y con qué credenciales pide tokens de servicio.</summary>
    public static void Configurar(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Emisor", Emisor);
        builder.UseSetting("Identity:ClientId", "cargo");
        builder.UseSetting("Identity:ClientSecret", SecretoCargo);
    }

    /// <summary>
    /// Emite un token como lo haría Identity. Cada parámetro permite estropear una sola cosa, para el ataque que se prueba.
    /// </summary>
    public static string Emitir(
        string rol, Guid? tenantId = null, string? sub = null, string[]? audiencias = null, string? emisor = null,
        DateTime? expira = null, DateTime? emitido = null, RSA? firmarCon = null, string? kid = null)
    {
        var ahora = emitido ?? DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["sub"] = sub ?? Guid.NewGuid().ToString(),
            ["role"] = rol,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["aud"] = audiencias ?? TodasLasAudiencias
        };

        if (tenantId is not null)
        {
            claims["tenant_id"] = tenantId.Value.ToString();
        }

        var clave = new RsaSecurityKey(firmarCon ?? ClaveDeIdentity) { KeyId = kid ?? Kid };
        return Manejador.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emisor ?? Emisor,
            IssuedAt = ahora,
            NotBefore = ahora,
            Expires = expira ?? ahora.AddMinutes(15),
            Claims = claims,
            SigningCredentials = new SigningCredentials(clave, SecurityAlgorithms.RsaSha256)
        });
    }

    /// <summary>Un token sin firma (<c>alg: none</c>) que dice ser de un administrador: el ataque clásico.</summary>
    public static string EmitirSinFirma()
    {
        static string B64(string json) => Base64UrlEncoder.Encode(System.Text.Encoding.UTF8.GetBytes(json));

        var exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var cabecera = B64("""{"alg":"none","typ":"JWT"}""");
        var cuerpo = B64(
            $$"""{"sub":"{{Guid.NewGuid()}}","role":"Administrador","tenant_id":"{{Guid.NewGuid()}}","iss":"{{Emisor}}","aud":["ecotrace-identity","ecotrace-fleet","ecotrace-cargo","ecotrace-billing"],"exp":{{exp}}}""");
        return $"{cabecera}.{cuerpo}.";
    }

    /// <summary>Cambia un carácter de la firma. El resto del token queda intacto.</summary>
    public static string ConFirmaAlterada(string token)
    {
        var partes = token.Split('.');
        var firma = partes[2].ToCharArray();
        var medio = firma.Length / 2;
        firma[medio] = firma[medio] == 'A' ? 'B' : 'A';
        return $"{partes[0]}.{partes[1]}.{new string(firma)}";
    }

    /// <summary>El JWKS que publicaría Identity para esta clave: solo la parte pública.</summary>
    public static string JwksDe(RSA rsa)
    {
        var p = rsa.ExportParameters(false);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = "RSA", use = "sig", alg = "RS256", kid = KidDe(rsa), n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) }
            }
        });
    }

    public static string KidDe(RSA rsa)
    {
        var p = rsa.ExportParameters(false);
        var jwk = new JsonWebKey { Kty = "RSA", N = Base64UrlEncoder.Encode(p.Modulus), E = Base64UrlEncoder.Encode(p.Exponent) };
        return Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint());
    }
}

public static class ClientesConToken
{
    /// <summary>Hace que todas las llamadas de este cliente lleven el token dado. Con null, el cliente llama sin token.</summary>
    public static HttpClient ConToken(this HttpClient cliente, string? token)
    {
        cliente.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
        return cliente;
    }
}
