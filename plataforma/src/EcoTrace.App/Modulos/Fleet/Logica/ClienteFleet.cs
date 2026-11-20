using System.Net;
using System.Text.Json;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Fleet;

/// <summary>Lo que respondió Fleet: la asignación si salió bien, o el motivo para mostrarle al conductor.</summary>
public sealed record ConsultaFleet(bool Exito, AsignacionEnCache? Asignacion, string Mensaje);

/// <summary>
/// Pide a Fleet Management el conductor y el vehículo de quien inició sesión (<c>GET /api/conductores/yo</c>).
/// Solo lee. El token lo da <see cref="IProveedorDeSesion"/>, que implementa Identity: este módulo no guarda
/// tokens por su cuenta (ADR 0004 y 0005).
/// </summary>
public sealed class ClienteFleet(
    HttpClient http, ConfiguracionServicios servicios, IProveedorDeSesion sesion, TimeProvider reloj)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Pasado este tiempo sin respuesta se trata como falta de conexión.</summary>
    public TimeSpan TiempoMaximo { get; init; } = TimeSpan.FromSeconds(10);

    public async Task<ConsultaFleet> ConsultarAsync(CancellationToken ct = default)
    {
        var token = await sesion.ObtenerTokenVigenteAsync(ct);
        if (string.IsNullOrEmpty(token))
        {
            return Falla("Inicia sesión para actualizar tu vehículo.");
        }

        using var solicitud = new HttpRequestMessage(HttpMethod.Get, new Uri(servicios.Fleet, "/api/conductores/yo"));
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
                return Falla("El servidor no aceptó tu sesión.");
            }

            if (respuesta.StatusCode == HttpStatusCode.NotFound)
            {
                return Falla("Tu usuario no está registrado como conductor en Fleet.");
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                return Falla($"Fleet respondió con un error ({(int)respuesta.StatusCode}).");
            }

            var cuerpo = await respuesta.Content.ReadAsStringAsync(limite.Token);
            var asignacion = Leer(cuerpo, reloj.GetUtcNow().UtcDateTime);
            return asignacion is null
                ? Falla("Fleet respondió algo que la app no entiende.")
                : new ConsultaFleet(true, asignacion, string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return Falla("No hay conexión con Fleet.");
        }
        catch (JsonException)
        {
            return Falla("Fleet respondió algo que la app no entiende.");
        }
    }

    private static AsignacionEnCache? Leer(string cuerpo, DateTime ahoraUtc)
    {
        var datos = JsonSerializer.Deserialize<RespuestaYo>(cuerpo, Json);
        if (datos?.Conductor is null)
        {
            return null;
        }

        return new AsignacionEnCache(
            datos.Conductor.ConductorId, datos.Conductor.Nombre, datos.Conductor.Licencia, datos.Conductor.Estado,
            datos.Vehiculo?.VehiculoId, datos.Vehiculo?.Placa, datos.Vehiculo?.CapacidadKg, datos.Vehiculo?.Estado,
            datos.CargaId, ahoraUtc);
    }

    // Forma de la respuesta de Fleet (AsignacionDelConductorResponse). Solo los campos que la app usa.
    private sealed record RespuestaYo(ConductorJson? Conductor, VehiculoJson? Vehiculo, Guid? CargaId);

    private sealed record ConductorJson(Guid ConductorId, string Nombre, string Licencia, string Estado);

    private sealed record VehiculoJson(Guid VehiculoId, string Placa, int CapacidadKg, string Estado);

    private static ConsultaFleet Falla(string mensaje) => new(false, null, mensaje);
}
