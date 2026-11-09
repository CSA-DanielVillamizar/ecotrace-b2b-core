using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Hace de Identity para un Resource Server que se prueba solo: contesta <c>/.well-known/jwks.json</c> con la clave pública
/// de prueba. Cuenta las descargas (para comprobar que el módulo no baja el JWKS en cada solicitud) y se puede "caer"
/// (para comprobar que un token válido sigue validándose si Identity no responde).
/// </summary>
public sealed class JwksFalso : HttpMessageHandler
{
    private int _llamadas;

    /// <summary>Con qué clave se firman los tokens que este Identity de mentira publica. Cambiarla simula una rotación.</summary>
    public RSA Clave { get; set; } = SeguridadDePrueba.ClaveDeIdentity;

    /// <summary>Mientras sea true, Identity "no responde".</summary>
    public bool Caido { get; set; }

    public int Llamadas => Volatile.Read(ref _llamadas);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        Interlocked.Increment(ref _llamadas);

        if (Caido)
        {
            throw new HttpRequestException("Identity no responde (simulado en la prueba).");
        }

        if (solicitud.RequestUri?.AbsolutePath != "/.well-known/jwks.json")
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SeguridadDePrueba.JwksDe(Clave), Encoding.UTF8, "application/json")
        });
    }
}
