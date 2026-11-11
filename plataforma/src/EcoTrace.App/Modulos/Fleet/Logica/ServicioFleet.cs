using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Fleet;

/// <summary>Lo que muestra la pantalla de Fleet: la asignación, si viene de la caché y por qué.</summary>
public sealed record VistaFleet(AsignacionEnCache? Asignacion, bool SonDatosGuardados, string Aviso);

/// <summary>Si la novedad quedó en la cola y qué decirle al conductor.</summary>
public sealed record ResultadoReporte(bool Encolada, string Mensaje);

/// <summary>
/// Decide de dónde sale lo que ve el conductor, con la regla del ADR 0005: <b>local primero, servidor después</b>.
/// Con conexión pide su asignación a Fleet y la guarda en la caché; sin conexión, o si Fleet no responde, muestra
/// la caché con un aviso. La pantalla nunca queda en blanco por falta de señal.
/// </summary>
public sealed class ServicioFleet(
    ClienteFleet cliente, CacheDeFleetSqlite cache, IColaLocal cola, IConectividad conectividad, TimeProvider reloj)
{
    /// <summary>Nombre del módulo en la cola local. Debe coincidir con <c>ModuloFleet.Nombre</c>.</summary>
    public const string Modulo = "Fleet";

    /// <summary>Lo guardado en el teléfono, sin tocar la red. Es lo primero que se pinta al abrir la pantalla.</summary>
    public async Task<VistaFleet> ObtenerDeCacheAsync(CancellationToken ct = default) =>
        new(await cache.LeerAsync(ct), SonDatosGuardados: true, Aviso: string.Empty);

    /// <summary>Pide la asignación a Fleet y actualiza la caché. Si no puede, devuelve la caché con el motivo.</summary>
    public async Task<VistaFleet> RefrescarAsync(CancellationToken ct = default)
    {
        if (!conectividad.HayConexion)
        {
            return new(await cache.LeerAsync(ct), true, "Sin conexión. Mostrando datos guardados.");
        }

        var consulta = await cliente.ConsultarAsync(ct);
        if (!consulta.Exito)
        {
            return new(await cache.LeerAsync(ct), true, $"{consulta.Mensaje} Mostrando datos guardados.");
        }

        await cache.GuardarAsync(consulta.Asignacion!, ct);

        // Que Fleet haya respondido es contacto real: alimenta el "Última sincronización" del banner.
        await cola.RegistrarSincronizacionAsync(reloj.GetUtcNow().UtcDateTime, ct);

        return new(consulta.Asignacion, false, string.Empty);
    }

    /// <summary>
    /// Guarda la novedad en la cola local antes de tocar la red (regla de oro del ADR 0005). Ahí nace su
    /// OperationId. El motor la envía cuando haya señal y sesión, así que funciona igual con o sin conexión.
    /// </summary>
    public async Task<ResultadoReporte> ReportarNovedadAsync(string tipo, string? descripcion, CancellationToken ct = default)
    {
        var asignacion = await cache.LeerAsync(ct);
        if (asignacion?.VehiculoId is null)
        {
            return new(false, "No tienes un vehículo asignado. Actualiza con señal antes de reportar.");
        }

        if (string.IsNullOrWhiteSpace(descripcion))
        {
            return new(false, "Escribe qué le pasó al vehículo.");
        }

        // La carga y la hora salen del teléfono en este momento: si se envía horas después, Fleet sabe
        // cuándo ocurrió y con qué carga la vio el conductor.
        var datos = new DatosNovedad(
            asignacion.VehiculoId.Value, asignacion.CargaId, tipo, descripcion.Trim(), reloj.GetUtcNow());
        await cola.EncolarAsync(Modulo, ReportarNovedadHandler.TipoAccion, JsonSerializer.Serialize(datos), ct);

        return new(true, conectividad.HayConexion
            ? "Novedad guardada. Se envía en un momento."
            : "Novedad guardada sin conexión. Se envía cuando vuelva la señal.");
    }
}
