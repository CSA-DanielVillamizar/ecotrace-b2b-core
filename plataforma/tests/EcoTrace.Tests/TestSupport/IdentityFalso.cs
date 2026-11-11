using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Hace de Identity para el módulo que se prueba. Contesta dos rutas:
/// <list type="bullet">
/// <item><c>/.well-known/jwks.json</c>: la clave pública de prueba, con la que Cargo comprueba las firmas.</item>
/// <item><c>/api/auth/token-servicio</c>: entrega un token de servicio si las credenciales son las de Cargo.</item>
/// </list>
/// Cuenta las descargas (para comprobar que Cargo no baja la clave en cada solicitud) y se puede «caer» (para comprobar que un
/// token válido sigue validándose, y que Cargo no inventa un token, si Identity no responde).
/// </summary>
public sealed class IdentityFalso : HttpMessageHandler
{
    private readonly ConcurrentQueue<string> _audiencias = new();
    private int _llamadasJwks;
    private int _llamadasToken;

    /// <summary>Con qué clave se firman los tokens que este Identity de mentira publica. Cambiarla simula una rotación.</summary>
    public RSA Clave { get; set; } = SeguridadDePrueba.ClaveDeIdentity;

    /// <summary>Mientras sea true, Identity «no responde».</summary>
    public bool Caido { get; set; }

    /// <summary>El reloj con el que se calcula cuándo vence un token de servicio. Las pruebas del proveedor ponen uno manual.</summary>
    public TimeProvider Reloj { get; set; } = TimeProvider.System;

    public int LlamadasJwks => Volatile.Read(ref _llamadasJwks);

    public int LlamadasToken => Volatile.Read(ref _llamadasToken);

    /// <summary>Los módulos de destino para los que se entregó un token de servicio, sin repetir.</summary>
    public IReadOnlyList<string> AudienciasPedidas => _audiencias.Distinct().ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        if (Caido)
        {
            throw new HttpRequestException("Identity no responde (simulado en la prueba).");
        }

        var ruta = solicitud.RequestUri?.AbsolutePath;
        if (ruta == "/.well-known/jwks.json")
        {
            Interlocked.Increment(ref _llamadasJwks);
            return Respuesta200(SeguridadDePrueba.JwksDe(Clave));
        }

        if (ruta == "/api/auth/token-servicio" && solicitud.Method == HttpMethod.Post)
        {
            return await EntregarTokenDeServicioAsync(solicitud, ct);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> EntregarTokenDeServicioAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        using var documento = JsonDocument.Parse(await solicitud.Content!.ReadAsStringAsync(ct));
        var cuerpo = documento.RootElement;
        var clientId = cuerpo.GetProperty("clientId").GetString();
        var secreto = cuerpo.GetProperty("clientSecret").GetString();
        var audiencia = cuerpo.GetProperty("audiencia").GetString()!;

        // Igual que el Identity real: credenciales que no son las del cliente, 401.
        if (clientId != "cargo" || secreto != SeguridadDePrueba.SecretoCargo)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        Interlocked.Increment(ref _llamadasToken);
        _audiencias.Enqueue(audiencia);

        var vence = Reloj.GetUtcNow().UtcDateTime.AddMinutes(5);
        var token = SeguridadDePrueba.Emitir(Rol.Servicio, sub: clientId, audiencias: [audiencia]);
        return Respuesta200(JsonSerializer.Serialize(new { accessToken = token, expiraEn = vence }));
    }

    private static HttpResponseMessage Respuesta200(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override void Dispose(bool disposing)
    {
        // Lo libera quien lo creó: el generador de clientes HTTP solo lo usa y puede pedir que se descarte.
    }
}
