using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Quién es quién en las pruebas de seguridad. Las pruebas conocen la clave privada de Identity de prueba, así que
/// pueden emitir tokens de cualquier persona (rápido, sin pasar por el inicio de sesión) y también tokens mal hechos
/// para comprobar que cada Resource Server los rechaza.
/// </summary>
public static class SeguridadDePrueba
{
    public const string Issuer = "ecotrace-identity-prueba";
    public const string AdminEmail = "admin@prueba.ecotrace";
    public const string AdminPassword = "Plataforma-2026-prueba";
    public const string SecretoBilling = "secreto-billing-de-prueba-0123";
    public const string SecretoCargo = "secreto-cargo-de-prueba-0123";

    public const string AudIdentity = "ecotrace-identity";
    public const string AudFleet = "ecotrace-fleet";
    public const string AudCargo = "ecotrace-cargo";
    public const string AudBilling = "ecotrace-billing";

    public static readonly string[] TodasLasAudiencias = [AudIdentity, AudFleet, AudCargo, AudBilling];

    private static readonly JsonWebTokenHandler Manejador = new();

    /// <summary>La clave de firma de Identity en las pruebas. La generan las pruebas; no hay ninguna clave en el repositorio.</summary>
    public static RSA ClaveDeIdentity { get; } = RSA.Create(2048);

    public static string PemPrivado { get; } = ClaveDeIdentity.ExportRSAPrivateKeyPem();

    /// <summary>El <c>kid</c> que Identity pone a su clave: la huella RFC 7638 de la clave pública.</summary>
    public static string Kid { get; } = KidDe(ClaveDeIdentity);

    /// <summary>Configura una API con la clave, el emisor, el administrador de plataforma y los clientes de servicio de prueba.</summary>
    public static void Configurar(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:PrivateKeyPem", PemPrivado);
        builder.UseSetting("Plataforma:AdminEmail", AdminEmail);
        builder.UseSetting("Plataforma:AdminPassword", AdminPassword);
        builder.UseSetting("Seguridad:Pbkdf2Iteraciones", "1000");

        builder.UseSetting("Servicios:Clientes:0:ClientId", "billing");
        builder.UseSetting("Servicios:Clientes:0:ClientSecret", SecretoBilling);
        builder.UseSetting("Servicios:Clientes:0:Audiencias:0", AudIdentity);
        builder.UseSetting("Servicios:Clientes:0:Audiencias:1", AudFleet);
        builder.UseSetting("Servicios:Clientes:1:ClientId", "cargo");
        builder.UseSetting("Servicios:Clientes:1:ClientSecret", SecretoCargo);
        builder.UseSetting("Servicios:Clientes:1:Audiencias:0", AudBilling);
    }

    /// <summary>Emite un token como lo haría Identity. Cada parámetro permite estropear una cosa para el ataque que se prueba.</summary>
    public static string Emitir(
        string rol, Guid? tenantId = null, bool plataforma = false, string? sub = null,
        string[]? audiencias = null, string? issuer = null,
        DateTime? expira = null, DateTime? emitido = null,
        RSA? firmarCon = null, string? kid = null)
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

        if (plataforma)
        {
            claims["plataforma"] = "true";
        }

        var clave = new RsaSecurityKey(firmarCon ?? ClaveDeIdentity) { KeyId = kid ?? Kid };
        return Manejador.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            IssuedAt = ahora,
            NotBefore = ahora,
            Expires = expira ?? ahora.AddMinutes(15),
            Claims = claims,
            SigningCredentials = new SigningCredentials(clave, SecurityAlgorithms.RsaSha256)
        });
    }

    /// <summary>Token de una llamada entre servicios, como el que emite <c>/api/auth/token-servicio</c>.</summary>
    public static string EmitirServicio(string audiencia, string clientId = "prueba") =>
        Emitir(Rol.Servicio, sub: clientId, audiencias: [audiencia]);

    /// <summary>Token de un administrador de la organización que opera la plataforma.</summary>
    public static string EmitirOperador() => Emitir(Rol.Administrador, Guid.NewGuid(), plataforma: true);

    /// <summary>Un token sin firma (<c>alg: none</c>) con los claims de un operador: el ataque clásico.</summary>
    public static string EmitirSinFirma()
    {
        static string B64(string json) => Base64UrlEncoder.Encode(System.Text.Encoding.UTF8.GetBytes(json));
        var exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var cabecera = B64("""{"alg":"none","typ":"JWT"}""");
        var cuerpo = B64(
            $$"""{"sub":"{{Guid.NewGuid()}}","role":"Administrador","plataforma":"true","tenant_id":"{{Guid.NewGuid()}}","iss":"{{Issuer}}","aud":["ecotrace-identity","ecotrace-fleet","ecotrace-cargo","ecotrace-billing"],"exp":{{exp}}}""");
        return $"{cabecera}.{cuerpo}.";
    }

    /// <summary>Cambia un carácter de la firma: el resto del token queda intacto.</summary>
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

/// <summary>Los nombres de rol, para que las pruebas no escriban cadenas sueltas.</summary>
public static class Rol
{
    public const string Conductor = "Conductor";
    public const string Supervisor = "Supervisor";
    public const string Administrador = "Administrador";
    public const string Auditor = "Auditor";
    public const string Servicio = "Servicio";
}
