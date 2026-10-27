using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace EcoTrace.App.Modulos.Identity.Logica;

/// <summary>
/// Las llamadas directas a Identity: login, renovación, logout y leer el perfil. No pasan por la cola: el login
/// nunca se encola (la app no guarda la contraseña para reintentarla) y las demás solo tienen sentido con señal.
/// </summary>
public sealed class ClienteIdentity(HttpClient http, Uri identity)
{
    public async Task<ResultadoAutenticacion> IniciarSesionAsync(string email, string password, CancellationToken ct = default)
    {
        var resultado = await AutenticarAsync("/api/auth/login", new { email, password }, ct);
        return resultado.Estado == EstadoAutenticacion.Rechazada
            ? resultado with { Mensaje = "Correo o contraseña incorrectos." }
            : resultado;
    }

    public async Task<ResultadoAutenticacion> RenovarAsync(string refreshToken, CancellationToken ct = default)
    {
        var resultado = await AutenticarAsync("/api/auth/refresh", new { refreshToken }, ct);
        return resultado.Estado == EstadoAutenticacion.Rechazada
            ? resultado with { Mensaje = "Tu sesión terminó: venció o se cerró desde otro dispositivo. Inicia sesión de nuevo." }
            : resultado;
    }

    /// <summary>Revoca el refresh token en Identity. Es lo mejor posible: si no hay red, la sesión local se cierra igual.</summary>
    public async Task<bool> CerrarSesionAsync(string refreshToken, CancellationToken ct = default)
    {
        try
        {
            using var respuesta = await http.PostAsJsonAsync(new Uri(identity, "/api/auth/logout"), new { refreshToken }, ct);
            return respuesta.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>El perfil del usuario del token, o null si no se pudo leer (sin red, token rechazado, error).</summary>
    public async Task<PerfilGuardado?> ObtenerPerfilAsync(string accessToken, DateTime ahoraUtc, CancellationToken ct = default)
    {
        try
        {
            using var solicitud = new HttpRequestMessage(HttpMethod.Get, new Uri(identity, "/api/perfil"));
            solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var respuesta = await http.SendAsync(solicitud, ct);
            if (!respuesta.IsSuccessStatusCode)
            {
                return null;
            }

            var perfil = await respuesta.Content.ReadFromJsonAsync<PerfilGuardado>(ClavesDelAlmacen.Json, ct);
            return perfil is null ? null : perfil with { ObtenidoEnUtc = ahoraUtc };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private async Task<ResultadoAutenticacion> AutenticarAsync(string ruta, object cuerpo, CancellationToken ct)
    {
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await http.PostAsJsonAsync(new Uri(identity, ruta), cuerpo, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new(EstadoAutenticacion.SinRespuesta, null, "Identity no respondió. Intenta de nuevo en un momento.");
        }

        using (respuesta)
        {
            switch (respuesta.StatusCode)
            {
                case HttpStatusCode.OK:
                    var sesion = await respuesta.Content.ReadFromJsonAsync<SesionEmitida>(ClavesDelAlmacen.Json, ct);
                    return sesion is { AccessToken.Length: > 0, RefreshToken.Length: > 0 }
                        ? new(EstadoAutenticacion.Exito, new TokensEmitidos(sesion.AccessToken, sesion.RefreshToken, sesion.ExpiraEn.ToUniversalTime()), string.Empty)
                        : new(EstadoAutenticacion.SinRespuesta, null, "Identity respondió algo inesperado.");

                case HttpStatusCode.Unauthorized:
                    return new(EstadoAutenticacion.Rechazada, null, "Identity rechazó las credenciales.");

                case HttpStatusCode.Forbidden:
                    return new(EstadoAutenticacion.OrganizacionSuspendida, null,
                        "Tu organización está suspendida. Habla con el administrador de la plataforma.");

                default:
                    return new(EstadoAutenticacion.SinRespuesta, null, $"Identity no pudo atender la solicitud ({(int)respuesta.StatusCode}).");
            }
        }
    }

    private sealed record SesionEmitida(string AccessToken, string RefreshToken, DateTime ExpiraEn);
}
