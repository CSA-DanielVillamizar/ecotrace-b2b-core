using System.Net;
using System.Text.Json;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

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

public sealed record RespuestaDeCargas(ResultadoConsulta Resultado, IReadOnlyList<CargaEnCache> Cargas, string Mensaje);

/// <summary>
/// Pide al servidor de Cargo &amp; Tracking la lista de cargas (<c>GET /api/cargas</c>). Solo lee: nunca cambia nada.
/// El token lo da <see cref="IProveedorDeSesion"/>, que implementa el módulo de Identity; este módulo no guarda tokens
/// por su cuenta (ADR 0004 y 0005).
/// </summary>
public sealed class ClienteDeCargas(
    HttpClient http, ConfiguracionServicios servicios, IProveedorDeSesion sesion, TimeProvider reloj)
{
    /// <summary>Tiempo máximo de la consulta. Pasado ese tiempo se trata como falta de conexión.</summary>
    public TimeSpan TiempoMaximo { get; init; } = TimeSpan.FromSeconds(10);

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

    private static RespuestaDeCargas Falla(ResultadoConsulta resultado, string mensaje) => new(resultado, [], mensaje);

    /// <summary>Lee la lista que devuelve <c>GET /api/cargas</c>. Todas las cargas llevan la misma hora de actualización.</summary>
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
