namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Una carga tal como el servidor la devolvió la última vez que hubo conexión. Es la copia que se muestra sin señal.
/// <see cref="Estado"/> es el estado del SERVIDOR; los cambios que el conductor hizo y todavía no se enviaron no se
/// escriben aquí, viven solo en la cola (ver <see cref="ServicioDeCargas"/>), para que haya una única fuente de verdad.
/// </summary>
public sealed record CargaEnCache(
    Guid CargaId, string Descripcion, string Origen, string Destino, int PesoKg, string Estado, DateTime ActualizadaEnUtc);

/// <summary>Dónde se guardan en el teléfono las cargas para mostrarlas sin conexión (ADR 0005).</summary>
public interface ICacheDeCargas
{
    /// <summary>
    /// Reemplaza todo el contenido por la lista nueva, en una sola transacción: o queda la lista completa o queda la
    /// anterior. Una carga que el servidor ya no devuelve (por ejemplo, reasignada) desaparece de la copia.
    /// </summary>
    Task ReemplazarAsync(IReadOnlyList<CargaEnCache> cargas, CancellationToken ct = default);

    Task<IReadOnlyList<CargaEnCache>> ListarAsync(CancellationToken ct = default);
}
