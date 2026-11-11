using System.Net;
using System.Text.Json;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>Cómo terminó el intento de pedirle las cargas al servidor.</summary>
public enum ResultadoConsulta
{
    /// <summary>El servidor respondió con la lista de cargas.</summary>
    Exito = 1,

    /// <summary>No hay un token vigente: hay que iniciar sesión.</summary>
    SinSesion = 2,

    /// <summary>El servidor no aceptó el token (401).</summary>
    NoAutorizado = 3,

    /// <summary>No hubo red o el servidor no respondió a tiempo.</summary>
    SinConexion = 4,

    /// <summary>El servidor respondió, pero con un error o con un formato que no se entiende.</summary>
    Error = 5
}

/// <summary>Lo que devuelve el cliente: cómo terminó, las cargas (si salió bien) y un mensaje para el conductor (si salió mal).</summary>
public sealed record RespuestaDeCargas(ResultadoConsulta Resultado, IReadOnlyList<CargaEnCache> Cargas, string Mensaje);

/// <summary>
/// Le pide al servidor de Cargo la lista de cargas (<c>GET /api/cargas</c>). Solo lee: nunca cambia nada.
/// <para>
/// El token lo da <see cref="IProveedorDeSesion"/>, que implementa el módulo de Identity. Este módulo no guarda tokens por
/// su cuenta (ADR 0004 y 0005). Sin token no se pregunta nada.
/// </para>
/// </summary>
/// <param name="http">El cliente HTTP de la app.</param>
/// <param name="servicios">Las direcciones de los servicios (de dónde sale la de Cargo).</param>
/// <param name="sesion">De donde sale el token vigente.</param>
/// <param name="reloj">El reloj, para poner la hora de la copia.</param>
public sealed class ClienteDeCargas(
    HttpClient http, ConfiguracionServicios servicios, IProveedorDeSesion sesion, TimeProvider reloj)
{
    /// <summary>Cuánto se espera la respuesta. Pasado ese tiempo se trata como falta de conexión.</summary>
    public TimeSpan TiempoMaximo { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Pide la lista de cargas al servidor con el token del conductor. Nunca lanza un error por falta de red: devuelve el
    /// resultado (éxito, sin sesión, sin conexión...) para que la pantalla decida qué mostrar.
    /// </summary>
    public async Task<RespuestaDeCargas> ObtenerAsync(CancellationToken ct = default)
    {
        var token = await sesion.ObtenerTokenVigenteAsync(ct);
        if (string.IsNullOrEmpty(token))
        {
            return Falla(ResultadoConsulta.SinSesion, "Inicia sesión para actualizar tus cargas.");
        }

        using var solicitud = new HttpRequestMessage(HttpMethod.Get, new Uri(servicios.Cargo, "/api/cargas"));
        solicitud.Headers.Authorization = new("Bearer", token);
        solicitud.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoMaximo);

        try
        {
            using var respuesta = await http.SendAsync(solicitud, limite.Token);

            if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            {
                await sesion.NotificarTokenRechazadoAsync(ct);
                return Falla(ResultadoConsulta.NoAutorizado, "El servidor no aceptó tu sesión.");
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                return Falla(ResultadoConsulta.Error, $"El servidor respondió con un error ({(int)respuesta.StatusCode}).");
            }

            var cuerpo = await respuesta.Content.ReadAsStringAsync(limite.Token);
            return new RespuestaDeCargas(ResultadoConsulta.Exito, Leer(cuerpo, reloj.GetUtcNow().UtcDateTime), string.Empty);
        }
        catch (HttpRequestException)
        {
            return Falla(ResultadoConsulta.SinConexion, "No se pudo conectar con el servidor.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Falla(ResultadoConsulta.SinConexion, "El servidor no respondió a tiempo.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return Falla(ResultadoConsulta.Error, "El servidor respondió con un formato que no se entiende.");
        }
    }

    /// <summary>Arma la respuesta para cuando algo salió mal (sin cargas y con un mensaje).</summary>
    private static RespuestaDeCargas Falla(ResultadoConsulta resultado, string mensaje) => new(resultado, [], mensaje);

    /// <summary>Convierte la lista que devolvió el servidor en cargas para guardar. Todas llevan la misma hora de actualización.</summary>
    private static List<CargaEnCache> Leer(string cuerpo, DateTime ahoraUtc)
    {
        using var documento = JsonDocument.Parse(cuerpo);
        if (documento.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Se esperaba una lista de cargas.");
        }

        var cargas = new List<CargaEnCache>();
        foreach (var elemento in documento.RootElement.EnumerateArray())
        {
            cargas.Add(new CargaEnCache(
                elemento.GetProperty("cargaId").GetGuid(),
                elemento.GetProperty("descripcion").GetString() ?? string.Empty,
                elemento.GetProperty("origen").GetString() ?? string.Empty,
                elemento.GetProperty("destino").GetString() ?? string.Empty,
                elemento.GetProperty("pesoKg").GetInt32(),
                elemento.GetProperty("estado").GetString() ?? string.Empty,
                ahoraUtc));
        }

        return cargas;
    }
}
