namespace EcoTrace.Identity.Domain;

/// <summary>
/// Refresh token emitido en un login (ADR 0004). Se guarda solo su hash: quien lea la base no puede
/// usarlo. Vive 7 días y se puede revocar (logout); el access token que renueva no se revoca.
/// </summary>
public sealed class TokenRefresco
{
    private TokenRefresco()
    {
        Hash = string.Empty;
    }

    public Guid TokenRefrescoId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 del token en hexadecimal. El token en claro solo lo conoce el cliente.</summary>
    public string Hash { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ExpiraEn { get; private set; }

    public DateTime? RevocadoEn { get; private set; }

    public static TokenRefresco Crear(Guid userId, string hash, DateTime ahoraUtc, TimeSpan duracion) =>
        new()
        {
            TokenRefrescoId = Guid.NewGuid(),
            UserId = userId,
            Hash = hash,
            CreadoEn = ahoraUtc,
            ExpiraEn = ahoraUtc.Add(duracion)
        };

    public bool EstaVigente(DateTime ahoraUtc) => RevocadoEn is null && ahoraUtc < ExpiraEn;

    /// <summary>Revoca el token. Es idempotente: devuelve true solo si cambió.</summary>
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
