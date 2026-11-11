using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Tests.TestSupport;

/// <summary>Los nombres de rol, para que las pruebas no escriban textos sueltos.</summary>
public static class Rol
{
    /// <summary>Quien maneja y entrega la carga.</summary>
    public const string Conductor = "Conductor";
    /// <summary>Quien coordina a los conductores.</summary>
    public const string Supervisor = "Supervisor";
    /// <summary>Quien administra una organización.</summary>
    public const string Administrador = "Administrador";
    /// <summary>Quien solo consulta.</summary>
    public const string Auditor = "Auditor";
    /// <summary>Un servicio interno, no una persona.</summary>
    public const string Servicio = "Servicio";
}

/// <summary>
/// Quién es quién en las pruebas de seguridad. Las pruebas conocen la clave privada del Identity de prueba, así que pueden
/// fabricar el token de cualquier persona (rápido, sin pasar por el login) y también tokens mal hechos, para comprobar que
/// Cargo los rechaza. La clave la genera cada ejecución: no hay ninguna clave guardada en el repositorio.
/// </summary>
public static class SeguridadDePrueba
{
    /// <summary>El emisor del Identity de prueba. Distinto del de desarrollo y del de producción.</summary>
    public const string Emisor = "https://identity.ecotrace.local/prueba";

    /// <summary>La contraseña con la que Cargo se identifica ante el Identity de prueba para pedir tokens de servicio.</summary>
    public const string SecretoCargo = "secreto-cargo-de-prueba-0123";

    /// <summary>El nombre del cliente HTTP con el que Cargo pide la clave pública. Las pruebas lo reemplazan por el Identity de mentira.</summary>
    public const string ClienteJwks = "identity-jwks";

    /// <summary>El nombre del cliente HTTP con el que Cargo pide tokens de servicio. Las pruebas lo reemplazan por el Identity de mentira.</summary>
    public const string ClienteToken = "identity-token";

    /// <summary>Audiencia del módulo de Identity.</summary>
    public const string AudIdentity = "ecotrace-identity";
    /// <summary>Audiencia del módulo de Fleet.</summary>
    public const string AudFleet = "ecotrace-fleet";
    /// <summary>Audiencia del módulo de Cargo.</summary>
    public const string AudCargo = "ecotrace-cargo";
    /// <summary>Audiencia del módulo de Billing.</summary>
    public const string AudBilling = "ecotrace-billing";

    /// <summary>Los cuatro módulos. Un token normal sirve para todos.</summary>
    public static readonly string[] TodasLasAudiencias = [AudIdentity, AudFleet, AudCargo, AudBilling];

    /// <summary>La pieza que fabrica y firma los tokens.</summary>
    private static readonly JsonWebTokenHandler Manejador = new();

    /// <summary>La clave con la que el Identity de prueba firma sus tokens.</summary>
    public static RSA ClaveDeIdentity { get; } = RSA.Create(2048);

    /// <summary>El identificador de esa clave. Va en la cabecera de cada token y en la lista de claves públicas.</summary>
    public static string Kid { get; } = KidDe(ClaveDeIdentity);

    /// <summary>Configura una API de prueba: a qué emisor le cree y con qué nombre y contraseña pide tokens de servicio.</summary>
    public static void Configurar(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Emisor", Emisor);
        builder.UseSetting("Identity:ClientId", "cargo");
        builder.UseSetting("Identity:ClientSecret", SecretoCargo);
    }

    /// <summary>
    /// Fabrica un token como lo haría Identity. Cada parámetro permite estropear una sola cosa (el emisor, la audiencia, el
    /// vencimiento, la clave de firma...), para probar un ataque a la vez.
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

    /// <summary>Fabrica un token sin firma (<c>alg: none</c>) que dice ser de un administrador: el ataque clásico.</summary>
    public static string EmitirSinFirma()
    {
        static string B64(string json) => Base64UrlEncoder.Encode(System.Text.Encoding.UTF8.GetBytes(json));

        var exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var cabecera = B64("""{"alg":"none","typ":"JWT"}""");
        var cuerpo = B64(
            $$"""{"sub":"{{Guid.NewGuid()}}","role":"Administrador","tenant_id":"{{Guid.NewGuid()}}","iss":"{{Emisor}}","aud":["ecotrace-identity","ecotrace-fleet","ecotrace-cargo","ecotrace-billing"],"exp":{{exp}}}""");
        return $"{cabecera}.{cuerpo}.";
    }

    /// <summary>Cambia un carácter de la firma de un token. El resto del token queda igual.</summary>
    public static string ConFirmaAlterada(string token)
    {
        var partes = token.Split('.');
        var firma = partes[2].ToCharArray();
        var medio = firma.Length / 2;
        firma[medio] = firma[medio] == 'A' ? 'B' : 'A';
        return $"{partes[0]}.{partes[1]}.{new string(firma)}";
    }

    /// <summary>La lista de claves públicas que publicaría Identity para esta clave (solo la parte pública).</summary>
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

    /// <summary>Calcula el identificador de una clave a partir de su parte pública.</summary>
    public static string KidDe(RSA rsa)
    {
        var p = rsa.ExportParameters(false);
        var jwk = new JsonWebKey { Kty = "RSA", N = Base64UrlEncoder.Encode(p.Modulus), E = Base64UrlEncoder.Encode(p.Exponent) };
        return Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint());
    }
}

/// <summary>Un atajo para que un cliente HTTP llame siempre con un token.</summary>
public static class ClientesConToken
{
    /// <summary>Hace que todas las llamadas de este cliente lleven el token dado. Con null, el cliente llama sin token.</summary>
    public static HttpClient ConToken(this HttpClient cliente, string? token)
    {
        cliente.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
        return cliente;
    }
}
