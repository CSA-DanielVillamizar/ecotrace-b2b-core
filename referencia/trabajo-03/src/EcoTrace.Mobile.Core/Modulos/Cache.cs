using System.Net.Http.Headers;
using System.Text.Json;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Core.Modulos;

/// <summary>
/// El último dato que el servidor devolvió, guardado en SQLite para poder mostrarlo sin conexión (ADR 0005: «la pantalla nunca
/// queda en blanco»). Guarda datos del negocio, nunca tokens.
/// </summary>
public interface ICacheLocal
{
    Task GuardarCacheAsync(string clave, string json, DateTime ahoraUtc, CancellationToken ct = default);

    Task<EntradaDeCache?> LeerCacheAsync(string clave, CancellationToken ct = default);
}

public sealed record EntradaDeCache(string Json, DateTime ActualizadaUtc);

/// <summary>Lo que una pantalla muestra: los datos, si vinieron del servidor o del caché, y de cuándo son.</summary>
public sealed record DatosConOrigen<T>(T? Datos, bool DelServidor, DateTime? ActualizadosUtc, string? Aviso)
{
    public bool HayDatos => Datos is not null;
}

/// <summary>El texto que dice de cuándo son los datos de una pantalla. Vive aquí para probarlo sin emulador.</summary>
public static class TextosDeDatos
{
    /// <summary>«Actualizado hoy 4:10 p. m.», «Datos guardados de hoy 4:10 p. m.» o «Datos guardados del 14/10 3:30 p. m.».</summary>
    public static string Actualizacion(bool delServidor, DateTime utc, DateTime ahoraUtc, TimeZoneInfo zona)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zona);
        var hoy = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ahoraUtc, DateTimeKind.Utc), zona).Date;
        var hora = Conexion.BannerSinConexion.FormatearHora(local);
        var fecha = $"{local.Day:00}/{local.Month:00}";
        return (delServidor, local.Date == hoy) switch
        {
            (true, true) => $"Actualizado hoy {hora}",
            (true, false) => $"Actualizado el {fecha} {hora}",
            (false, true) => $"Datos guardados de hoy {hora}",
            (false, false) => $"Datos guardados del {fecha} {hora}"
        };
    }
}

/// <summary>
/// Lee un recurso del servidor con el token de la sesión y lo guarda en el caché; si no hay conexión o el servidor no responde,
/// devuelve el último guardado. Así la pantalla muestra siempre algo, y dice de cuándo es.
/// </summary>
public sealed class LectorConCache(
    HttpClient http, IProveedorDeSesion sesion, ICacheLocal cache, IConectividad conectividad, TimeProvider? reloj = null)
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task<DatosConOrigen<T>> LeerAsync<T>(string clave, Uri direccion, CancellationToken ct = default)
    {
        string? aviso = null;

        if (conectividad.HayConexion)
        {
            var token = await sesion.ObtenerTokenVigenteAsync(ct);
            if (token is null)
            {
                aviso = "Tu sesión necesita renovarse. Mostrando datos guardados.";
            }
            else
            {
                try
                {
                    using var solicitud = new HttpRequestMessage(HttpMethod.Get, direccion);
                    solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using var respuesta = await http.SendAsync(solicitud, ct);

                    if (respuesta.IsSuccessStatusCode)
                    {
                        var json = await respuesta.Content.ReadAsStringAsync(ct);
                        var ahora = _reloj.GetUtcNow().UtcDateTime;
                        await cache.GuardarCacheAsync(clave, json, ahora, ct);
                        return new(JsonSerializer.Deserialize<T>(json, Opciones), true, ahora, null);
                    }

                    aviso = respuesta.StatusCode == System.Net.HttpStatusCode.Forbidden
                        ? "Tu rol no tiene acceso a esta información."
                        : "El servidor no pudo responder. Mostrando datos guardados.";
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    aviso = "Sin respuesta del servidor. Mostrando datos guardados.";
                }
            }
        }

        var guardada = await cache.LeerCacheAsync(clave, ct);
        return guardada is null
            ? new(default, false, null, aviso ?? "Todavía no hay datos guardados. Se cargarán cuando haya conexión.")
            : new(JsonSerializer.Deserialize<T>(guardada.Json, Opciones), false, guardada.ActualizadaUtc, aviso);
    }
}
