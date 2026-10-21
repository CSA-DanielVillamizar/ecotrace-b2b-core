using EcoTrace.Mobile.Core.Cola;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>Resultado de intentar guardar una acción del conductor. <see cref="Mensaje"/> se le muestra tal cual.</summary>
public sealed record ResultadoDeAccion(bool Exito, string Mensaje);

/// <summary>
/// Lo que el conductor puede hacer con una carga. Cada acción se guarda en la cola local <b>antes de tocar la red</b>
/// (paso 1 de la regla de oro del ADR 0005); el motor de sincronización la envía después, en orden. El OperationId
/// nace aquí, en el teléfono, al guardar: por eso un reintento tras un corte reenvía la misma clave y el servidor
/// no repite el efecto.
/// </summary>
public sealed class AccionesDeCarga(ServicioDeCargas servicio, IColaLocal cola)
{
    /// <summary>Largo máximo de la nota. Es el mismo límite del servidor, para no mandar algo que va a rechazar.</summary>
    public const int NotaMaxima = 300;

    // Revisar el estado y guardar la acción tiene que ser una sola operación: si el conductor toca dos veces seguidas
    // "Confirmar entrega", la segunda ya debe ver el estado nuevo y no encolar una entrega repetida.
    private readonly SemaphoreSlim _candado = new(1, 1);

    /// <summary>
    /// Pasa la carga a <paramref name="nuevoEstado"/>. Si es la entrega, se encola <c>ConfirmarEntrega</c> (la acción
    /// que dispara el Saga de pago); si no, <c>CambiarEstado</c>.
    /// </summary>
    public async Task<ResultadoDeAccion> CambiarEstadoAsync(
        Guid cargaId, string nuevoEstado, string? nota = null, CancellationToken ct = default)
    {
        await _candado.WaitAsync(ct);
        try
        {
            var vista = await servicio.ObtenerDeCacheAsync(ct);
            var carga = vista.Cargas.FirstOrDefault(c => c.CargaId == cargaId);
            if (carga is null)
            {
                return Falla("Esa carga no está en tu lista. Actualiza e intenta de nuevo.");
            }

            // Se valida contra el estado que el conductor está viendo (incluidas sus acciones en cola).
            if (!EstadosDeCarga.TransicionPermitida(carga.EstadoMostrado, nuevoEstado))
            {
                return Falla(
                    $"No se puede pasar de {EstadosDeCarga.Texto(carga.EstadoMostrado)} a {EstadosDeCarga.Texto(nuevoEstado)}.");
            }

            var notaLimpia = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
            if (nuevoEstado == EstadosDeCarga.ConNovedad && notaLimpia is null)
            {
                return Falla("Una novedad necesita una nota que explique qué ocurrió.");
            }

            if (notaLimpia is { Length: > NotaMaxima })
            {
                return Falla($"La nota no puede superar los {NotaMaxima} caracteres.");
            }

            var datos = new DatosCambioDeEstado(cargaId, nuevoEstado, UbicacionPara(carga, nuevoEstado), notaLimpia);
            var tipo = nuevoEstado == EstadosDeCarga.Entregado ? "ConfirmarEntrega" : "CambiarEstado";
            await cola.EncolarAsync(ServicioDeCargas.Modulo, tipo, PayloadDeCarga.Serializar(datos), ct);

            return new ResultadoDeAccion(true, "Guardado en el teléfono. Se envía solo en cuanto haya conexión.");
        }
        finally
        {
            _candado.Release();
        }
    }

    /// <summary>
    /// El servidor exige una ubicación de 2 a 120 caracteres. La app no usa GPS todavía: la entrega se registra en
    /// el destino de la carga y los demás cambios, "en ruta".
    /// </summary>
    private static string UbicacionPara(CargaVista carga, string nuevoEstado) =>
        nuevoEstado == EstadosDeCarga.Entregado ? carga.Destino : "En ruta";

    private static ResultadoDeAccion Falla(string mensaje) => new(false, mensaje);
}
