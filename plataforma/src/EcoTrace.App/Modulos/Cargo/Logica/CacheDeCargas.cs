namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Una carga tal como el servidor la devolvió la última vez que hubo conexión. Es la copia que se muestra sin señal.
/// <para>
/// El estado que guarda es el del SERVIDOR. Lo que el conductor hizo y todavía no se envió no se escribe aquí: vive solo en la
/// cola, para que haya una única fuente de verdad (ver <see cref="ServicioDeCargas"/>).
/// </para>
/// </summary>
/// <param name="CargaId">El identificador de la carga.</param>
/// <param name="Descripcion">Qué se transporta.</param>
/// <param name="Origen">De dónde sale.</param>
/// <param name="Destino">A dónde llega.</param>
/// <param name="PesoKg">El peso, en kilogramos.</param>
/// <param name="Estado">El estado según el servidor.</param>
/// <param name="ActualizadaEnUtc">Cuándo se guardó esta copia (hora UTC).</param>
public sealed record CargaEnCache(
    Guid CargaId, string Descripcion, string Origen, string Destino, int PesoKg, string Estado, DateTime ActualizadaEnUtc);

/// <summary>Dónde se guardan en el teléfono las cargas para poder mostrarlas sin conexión (ADR 0005).</summary>
public interface ICacheDeCargas
{
    /// <summary>
    /// Reemplaza todo el contenido por la lista nueva, en una sola transacción: o queda la lista completa o queda la anterior.
    /// Una carga que el servidor ya no devuelve (por ejemplo, reasignada) desaparece de la copia.
    /// </summary>
    Task ReemplazarAsync(IReadOnlyList<CargaEnCache> cargas, CancellationToken ct = default);

    /// <summary>Devuelve las cargas guardadas en el teléfono.</summary>
    Task<IReadOnlyList<CargaEnCache>> ListarAsync(CancellationToken ct = default);
}
