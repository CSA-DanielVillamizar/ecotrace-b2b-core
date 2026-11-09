namespace EcoTrace.Mobile.Core.Cola;

/// <summary>
/// Los cuatro estados de una acción en la cola local (ADR 0005). Es el único vocabulario permitido:
/// ningún módulo inventa un estado propio.
/// </summary>
public enum EstadoOperacion
{
    /// <summary>Guardada en el teléfono y todavía sin enviar.</summary>
    PendienteSync = 1,

    /// <summary>Se está enviando. Si la app se cierra aquí, al arrancar vuelve a PendienteSync.</summary>
    Sincronizando = 2,

    /// <summary>El servidor la aceptó (201) o ya la había aceptado antes (200 con la misma Idempotency-Key).</summary>
    Sincronizado = 3,

    /// <summary>
    /// El servidor la rechazó. Se conserva con su motivo para auditoría y para mostrarla al conductor:
    /// una acción nunca se borra en silencio.
    /// </summary>
    Rechazado = 4
}

/// <summary>Clasificación de conflictos del ADR 0005.</summary>
public enum TipoConflicto
{
    /// <summary>Validación recuperable: el dato local no cumple una regla, pero se puede corregir y reintentar.</summary>
    A = 1,

    /// <summary>Estado autoritativo: el servidor cambió mientras no había señal. El servidor gana y no hay reintento automático.</summary>
    B = 2,

    /// <summary>Edición concurrente: dos personas legítimas cambiaron lo mismo. Nunca gana el último que sincroniza.</summary>
    C = 3
}
