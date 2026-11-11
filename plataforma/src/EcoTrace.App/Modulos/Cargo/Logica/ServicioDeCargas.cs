using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Arma la lista de cargas que ve el conductor, con la regla de oro del ADR 0005: <b>local primero, servidor después, nunca
/// en silencio</b>.
/// <list type="bullet">
/// <item>Con conexión, pide las cargas al servidor y las guarda en la copia. Sin conexión (o si el servidor no responde),
/// muestra la copia con un aviso.</item>
/// <item>Encima de lo que dice el servidor pone lo que el conductor ya hizo y todavía no se envió (lo lee de la cola). Así la
/// pantalla cambia al instante y nunca espera al servidor.</item>
/// </list>
/// </summary>
/// <param name="cliente">Pide las cargas al servidor.</param>
/// <param name="cache">La copia de las cargas en el teléfono.</param>
/// <param name="cola">La cola de acciones pendientes.</param>
/// <param name="conectividad">Dice si hay conexión.</param>
/// <param name="reloj">El reloj.</param>
public sealed class ServicioDeCargas(
    ClienteDeCargas cliente, ICacheDeCargas cache, IColaLocal cola, IConectividad conectividad, TimeProvider reloj)
{
    /// <summary>El nombre del módulo en la cola local. Debe coincidir con el nombre de <c>ModuloCargo</c>.</summary>
    public const string Modulo = "Cargo";

    /// <summary>Los dos tipos de acción de este módulo, para reconocerlas en la cola.</summary>
    private static readonly string[] TiposDeAccion = ["CambiarEstado", "ConfirmarEntrega"];

    /// <summary>La lista tal como está en el teléfono, sin tocar la red. Es lo primero que se pinta al abrir la pantalla.</summary>
    public Task<VistaDeCargas> ObtenerDeCacheAsync(CancellationToken ct = default) =>
        ArmarAsync(sonDatosGuardados: true, aviso: string.Empty, ct);

    /// <summary>
    /// Pide la lista al servidor y actualiza la copia. Si no puede (sin conexión, sin sesión o servidor caído), devuelve la copia
    /// con un aviso que explica por qué.
    /// </summary>
    public async Task<VistaDeCargas> RefrescarAsync(CancellationToken ct = default)
    {
        if (!conectividad.HayConexion)
        {
            return await ArmarAsync(true, "Sin conexión. Mostrando datos guardados.", ct);
        }

        var respuesta = await cliente.ObtenerAsync(ct);
        if (respuesta.Resultado != ResultadoConsulta.Exito)
        {
            return await ArmarAsync(true, $"{respuesta.Mensaje} Mostrando datos guardados.", ct);
        }

        await cache.ReemplazarAsync(respuesta.Cargas, ct);

        // Que el servidor haya respondido es contacto real: alimenta el "Última sincronización" del banner.
        await cola.RegistrarSincronizacionAsync(reloj.GetUtcNow().UtcDateTime, ct);

        return await ArmarAsync(false, string.Empty, ct);
    }

    /// <summary>Arma lo que se va a dibujar: la copia de las cargas más lo que hay pendiente en la cola.</summary>
    private async Task<VistaDeCargas> ArmarAsync(bool sonDatosGuardados, string aviso, CancellationToken ct)
    {
        var cargas = await cache.ListarAsync(ct);
        var operaciones = await cola.ListarAsync(modulo: Modulo, ct: ct);
        var ultimaSincronizacion = await cola.UltimaSincronizacionAsync(ct);

        var ultimaAccion = UltimaAccionPorCarga(operaciones);
        var filas = cargas
            .Select(carga => Fila(carga, ultimaAccion.GetValueOrDefault(carga.CargaId)))
            .OrderBy(fila => fila.EstadoMostrado == EstadosDeCarga.Entregado)
            .ThenBy(fila => fila.Descripcion, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new VistaDeCargas(filas, sonDatosGuardados, ultimaSincronizacion, aviso);
    }

    /// <summary>
    /// De todas las acciones de la cola, deja la más reciente de cada carga. Las acciones llegan ordenadas, así que la última que
    /// se lee de una carga es la que cuenta.
    /// </summary>
    private static Dictionary<Guid, AccionDeCarga> UltimaAccionPorCarga(IReadOnlyList<OperacionPendiente> operaciones)
    {
        var porCarga = new Dictionary<Guid, AccionDeCarga>();
        foreach (var operacion in operaciones)
        {
            if (!TiposDeAccion.Contains(operacion.Tipo) || PayloadDeCarga.Leer(operacion.Payload) is not { } datos)
            {
                continue;
            }

            porCarga[datos.CargaId] = new AccionDeCarga(operacion, datos);
        }

        return porCarga;
    }

    /// <summary>
    /// Arma la fila de una carga. Si la última acción está pendiente, muestra su estado; si el servidor la rechazó, muestra el
    /// motivo; si ya se envió y la copia es más vieja, confía en la acción hasta el próximo refresco.
    /// </summary>
    private static CargaVista Fila(CargaEnCache carga, AccionDeCarga? ultima)
    {
        var estadoMostrado = carga.Estado;
        var pendiente = false;
        TipoConflicto? conflicto = null;
        string? motivo = null;

        if (ultima is not null)
        {
            var operacion = ultima.Operacion;
            switch (operacion.Estado)
            {
                // Todavía no llegó al servidor: la pantalla ya muestra el resultado de lo que hizo el conductor.
                case EstadoOperacion.PendienteSync or EstadoOperacion.Sincronizando:
                    estadoMostrado = ultima.Datos.Estado;
                    pendiente = true;
                    break;

                // Ya llegó, pero la copia es más vieja que el envío: se confía en la acción hasta el próximo refresco.
                // Si la copia es más nueva que el envío, manda la copia (el servidor puede haber cambiado después).
                case EstadoOperacion.Sincronizado
                    when (operacion.ActualizadaEnUtc ?? operacion.CreadaEnUtc) > carga.ActualizadaEnUtc:
                    estadoMostrado = ultima.Datos.Estado;
                    break;

                // El servidor la rechazó: se conserva y se explica por qué. El estado mostrado sigue siendo el del servidor.
                case EstadoOperacion.Rechazado:
                    conflicto = operacion.TipoConflicto;
                    motivo = operacion.MotivoRechazo ?? "El servidor rechazó la acción.";
                    break;
            }
        }

        return new CargaVista(
            carga.CargaId,
            carga.Descripcion,
            $"{carga.Origen} → {carga.Destino}",
            carga.Destino,
            carga.PesoKg,
            carga.Estado,
            estadoMostrado,
            pendiente,
            conflicto,
            motivo,
            EstadosDeCarga.SiguientesPermitidos(estadoMostrado));
    }

    /// <summary>Una acción de la cola junto con sus datos ya leídos.</summary>
    private sealed record AccionDeCarga(OperacionPendiente Operacion, DatosCambioDeEstado Datos);
}
