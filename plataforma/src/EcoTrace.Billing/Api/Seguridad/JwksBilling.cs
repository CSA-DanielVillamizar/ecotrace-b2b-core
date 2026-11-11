using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Billing.Api.Seguridad;

/// <summary>Convierte el JWKS publicado por Identity en la configuración que usa JwtBearer.</summary>
public sealed class JwksBillingRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancellationToken)
    {
        var documento = await retriever.GetDocumentAsync(address, cancellationToken);
        var configuracion = new OpenIdConnectConfiguration();

        foreach (var clave in new JsonWebKeySet(documento).GetSigningKeys())
        {
            configuracion.SigningKeys.Add(clave);
        }

        return configuracion;
    }
}
