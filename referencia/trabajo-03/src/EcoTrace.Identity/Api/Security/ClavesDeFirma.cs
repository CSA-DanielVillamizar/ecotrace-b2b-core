using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Identity.Api.Security;

/// <summary>
/// La clave con la que Identity firma los tokens (RS256). La privada solo vive aquí: los demás módulos conocen
/// únicamente la pública, que sale por <c>/.well-known/jwks.json</c>.
/// <para>
/// La privada viene de configuración (<c>Jwt:PrivateKeyPem</c>, en texto PEM o en Base64 del PEM) o de una variable
/// de entorno, nunca del repositorio. Solo en desarrollo, si no está configurada, se genera una y se guarda en
/// <c>App_Data</c> (que git ignora). Fuera de desarrollo, sin clave el servicio no arranca: falla cerrado.
/// </para>
/// </summary>
public sealed class ClavesDeFirma
{
    private const int BitsMinimos = 2048;

    private ClavesDeFirma(RSA rsa)
    {
        if (rsa.KeySize < BitsMinimos)
        {
            throw new InvalidOperationException($"La clave de firma debe ser RSA de al menos {BitsMinimos} bits.");
        }

        var publica = rsa.ExportParameters(false);
        var jwk = new JsonWebKey
        {
            Kty = "RSA",
            N = Base64UrlEncoder.Encode(publica.Modulus),
            E = Base64UrlEncoder.Encode(publica.Exponent)
        };

        // El kid es la huella de la clave pública (RFC 7638): cambia solo cuando cambia la clave.
        Kid = Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint());
        jwk.Kid = Kid;
        jwk.Use = "sig";
        jwk.Alg = SecurityAlgorithms.RsaSha256;

        PublicaJwk = jwk;
        Clave = new RsaSecurityKey(rsa) { KeyId = Kid };
    }

    public string Kid { get; }

    /// <summary>Clave con la parte privada: solo para firmar.</summary>
    public RsaSecurityKey Clave { get; }

    private JsonWebKey PublicaJwk { get; }

    /// <summary>El JWKS (RFC 7517) con la clave pública.</summary>
    public string Jwks() =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new { kty = PublicaJwk.Kty, use = PublicaJwk.Use, alg = PublicaJwk.Alg, kid = PublicaJwk.Kid, n = PublicaJwk.N, e = PublicaJwk.E }
            }
        });

    public static ClavesDeFirma Cargar(string? pem, IHostEnvironment entorno)
    {
        var rsa = RSA.Create();

        if (!string.IsNullOrWhiteSpace(pem))
        {
            rsa.ImportFromPem(ComoPem(pem));
        }
        else if (entorno.IsDevelopment())
        {
            var carpeta = Path.Combine(entorno.ContentRootPath, "App_Data");
            Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, "jwt-dev-key.pem");
            if (File.Exists(ruta))
            {
                rsa.ImportFromPem(File.ReadAllText(ruta));
            }
            else
            {
                rsa.KeySize = BitsMinimos;
                File.WriteAllText(ruta, rsa.ExportRSAPrivateKeyPem());
            }
        }
        else
        {
            throw new InvalidOperationException(
                "Jwt:PrivateKeyPem es obligatorio fuera de desarrollo. La clave privada de firma sale de una variable de entorno, nunca del repositorio.");
        }

        return new ClavesDeFirma(rsa);
    }

    private static string ComoPem(string valor)
    {
        var texto = valor.Trim();
        return texto.Contains("-----BEGIN", StringComparison.Ordinal)
            ? texto
            : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(texto));
    }
}
