using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Identity.Api.Seguridad;

public sealed record TokenEmitido(string Token, DateTime ExpiraEn);

/// <summary>
/// Emite los JWT firmados con RS256. Claims del contrato del Trabajo 3: <c>sub</c>, <c>tenant_id</c>,
/// <c>role</c>, <c>iss</c>, <c>aud</c> (lista), <c>iat</c>, <c>exp</c> y <c>jti</c>.
/// </summary>
public sealed class EmisorDeTokens(ClavesDeFirma claves, IOptions<OpcionesJwt> opciones, TimeProvider reloj)
{
    private static readonly JsonWebTokenHandler Manejador = new();

    public TokenEmitido EmitirDeUsuario(Guid userId, Guid tenantId, string rol) =>
        Emitir(userId.ToString(), tenantId, rol, opciones.Value.Audiencias, TimeSpan.FromMinutes(opciones.Value.MinutosAcceso));

    /// <summary>Token de 5 minutos para llamadas entre servicios: <c>role = Servicio</c>, sin organización.</summary>
    public TokenEmitido EmitirDeServicio(string clientId, string audiencia) =>
        Emitir($"servicio:{clientId}", tenantId: null, Roles.Servicio, [audiencia], TimeSpan.FromMinutes(opciones.Value.MinutosServicio));

    /// <summary>Valor opaco y aleatorio (256 bits). Solo el cliente lo conoce en claro.</summary>
    public static string NuevoTokenRefresco() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));

    private TokenEmitido Emitir(string sub, Guid? tenantId, string rol, string[] audiencias, TimeSpan duracion)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var expira = ahora.Add(duracion);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = sub,
            [Claims.Rol] = rol,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            [JwtRegisteredClaimNames.Aud] = audiencias
        };

        if (tenantId is not null)
        {
            claims[Claims.TenantId] = tenantId.Value.ToString();
        }

        var token = Manejador.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = opciones.Value.Emisor,
            IssuedAt = ahora,
            NotBefore = ahora,
            Expires = expira,
            Claims = claims,
            SigningCredentials = claves.Credenciales
        });

        return new TokenEmitido(token, expira);
    }
}
