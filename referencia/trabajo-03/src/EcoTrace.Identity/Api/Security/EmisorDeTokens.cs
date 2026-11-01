using System.Security.Cryptography;
using EcoTrace.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Identity.Api.Security;

/// <summary>Configuración de los tokens (sección <c>Jwt</c>). La clave privada no tiene valor por defecto.</summary>
public sealed class OpcionesJwt
{
    /// <summary>Quién emite (<c>iss</c>). Distinto en cada entorno: un token de desarrollo no sirve en producción.</summary>
    public string Issuer { get; set; } = string.Empty;

    public int AccessTokenMinutos { get; set; } = 15;

    public int RefreshTokenDias { get; set; } = 7;

    public int TokenServicioMinutos { get; set; } = 5;

    public string? PrivateKeyPem { get; set; }
}

public sealed record TokenEmitido(string Token, DateTime ExpiraEnUtc);

/// <summary>Firma los tokens de acceso de las personas y los de servicio con la clave RS256 de Identity.</summary>
public sealed class EmisorDeTokens(ClavesDeFirma claves, IOptions<OpcionesJwt> opciones, TimeProvider reloj)
{
    private static readonly JsonWebTokenHandler Manejador = new();

    /// <summary>Token de una persona: sirve para los cuatro módulos y dura 15 minutos.</summary>
    public TokenEmitido EmitirAcceso(User usuario, string rol, bool esOperador)
    {
        var claims = new Dictionary<string, object>
        {
            [Reclamos.Sub] = usuario.UserId.ToString(),
            [Reclamos.TenantId] = usuario.TenantId.ToString(),
            [Reclamos.Rol] = rol
        };

        if (esOperador)
        {
            claims[Reclamos.Plataforma] = "true";
        }

        return Firmar(claims, Audiencias.Todas, TimeSpan.FromMinutes(opciones.Value.AccessTokenMinutos));
    }

    /// <summary>Token de una llamada entre servicios: un solo módulo de destino, rol Servicio y 5 minutos.</summary>
    public TokenEmitido EmitirServicio(string clientId, string audiencia)
    {
        var claims = new Dictionary<string, object>
        {
            [Reclamos.Sub] = clientId,
            [Reclamos.Rol] = Roles.Servicio
        };

        return Firmar(claims, [audiencia], TimeSpan.FromMinutes(opciones.Value.TokenServicioMinutos));
    }

    /// <summary>Un refresh token nuevo: 32 bytes aleatorios. Lo que se guarda es su hash, no el token.</summary>
    public static string NuevoRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static string HashDe(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();

    private TokenEmitido Firmar(Dictionary<string, object> claims, string[] audiencias, TimeSpan duracion)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var expira = ahora + duracion;

        claims[JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N");
        claims[JwtRegisteredClaimNames.Aud] = audiencias;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = opciones.Value.Issuer,
            IssuedAt = ahora,
            NotBefore = ahora,
            Expires = expira,
            Claims = claims,
            SigningCredentials = new SigningCredentials(claves.Clave, SecurityAlgorithms.RsaSha256)
        };

        return new TokenEmitido(Manejador.CreateToken(descriptor), expira);
    }
}
