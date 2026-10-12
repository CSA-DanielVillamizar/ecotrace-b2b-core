namespace EcoTrace.Mobile.Core.Cola;

/// <summary>
/// La cola de acciones del conductor. Toda acción sigue el mismo ciclo: se guarda aquí antes de tocar la red,
/// la pantalla se actualiza de inmediato y el motor la sincroniza después, en el orden en que se encoló.
/// <para>
/// A propósito no hay un método para borrar una acción ni para devolver una rechazada a PendienteSync:
/// "nunca se pierde una acción en silencio".
/// </para>
/// </summary>
public interface IColaLocal
{
    /// <summary>Se dispara cuando cambia algo en la cola (para refrescar pantallas y contadores).</summary>
    event EventHandler? Cambio;

    /// <summary>Guarda una acción nueva en estado PendienteSync y le asigna su OperationId.</summary>
    Task<OperacionPendiente> EncolarAsync(string modulo, string tipo, string payloadJson, CancellationToken ct = default);

    Task<OperacionPendiente?> ObtenerAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Lista en orden de llegada, con filtros opcionales.</summary>
    Task<IReadOnlyList<OperacionPendiente>> ListarAsync(
        EstadoOperacion? estado = null, string? modulo = null, CancellationToken ct = default);

    Task<int> ContarAsync(EstadoOperacion estado, CancellationToken ct = default);

    /// <summary>PendienteSync → Sincronizando. Falla si la acción no estaba pendiente.</summary>
    Task MarcarSincronizandoAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Sincronizando → Sincronizado.</summary>
    Task MarcarSincronizadaAsync(Guid operationId, CancellationToken ct = default);

    /// <summary>Sincronizando → PendienteSync, contando el intento. Es para fallos transitorios: no se pierde nada.</summary>
    Task DevolverAPendienteAsync(Guid operationId, string? motivo, CancellationToken ct = default);

    /// <summary>Sincronizando → Rechazado, con el tipo de conflicto (si el servidor lo dijo) y el motivo.</summary>
    Task RechazarAsync(Guid operationId, TipoConflicto? tipo, string motivo, CancellationToken ct = default);

    /// <summary>
    /// Deja otra vez en PendienteSync lo que quedó en Sincronizando porque la app se cerró a mitad de un envío.
    /// Es seguro: el servidor deduplica por OperationId, así que reenviar no repite el efecto.
    /// </summary>
    Task<int> RecuperarInterrumpidasAsync(CancellationToken ct = default);

    /// <summary>
    /// Conflicto A: el conductor corrigió el dato. Crea una acción nueva, con otro OperationId, y deja la rechazada
    /// intacta para auditoría. La nueva va al final de la cola.
    /// </summary>
    Task<OperacionPendiente> ReintentarCorregidaAsync(Guid operationIdRechazada, string nuevoPayloadJson, CancellationToken ct = default);

    /// <summary>Último contacto real con el servidor (para el banner "Sin conexión"). Null si nunca hubo.</summary>
    Task<DateTime?> UltimaSincronizacionAsync(CancellationToken ct = default);

    Task RegistrarSincronizacionAsync(DateTime utc, CancellationToken ct = default);
}
