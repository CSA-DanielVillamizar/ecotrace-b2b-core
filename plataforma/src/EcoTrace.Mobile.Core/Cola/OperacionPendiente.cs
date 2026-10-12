namespace EcoTrace.Mobile.Core.Cola;

/// <summary>Una fila de la cola local. La tabla la define el Mobile.Core; los módulos no la rehacen.</summary>
public sealed record OperacionPendiente
{
    /// <summary>Orden de llegada. La cola se sincroniza siempre en este orden.</summary>
    public long Secuencia { get; init; }

    /// <summary>
    /// Se genera en el dispositivo, al guardar la acción, y viaja como <c>Idempotency-Key</c>.
    /// Si naciera al sincronizar, un reintento tras un corte de red crearía una acción nueva.
    /// </summary>
    public required Guid OperationId { get; init; }

    /// <summary>Módulo dueño de la acción, por ejemplo "Cargo".</summary>
    public required string Modulo { get; init; }

    /// <summary>Qué acción es, por ejemplo "ConfirmarEntrega".</summary>
    public required string Tipo { get; init; }

    /// <summary>Cuerpo de la solicitud, en JSON.</summary>
    public required string Payload { get; init; }

    public required EstadoOperacion Estado { get; init; }

    public TipoConflicto? TipoConflicto { get; init; }

    /// <summary>Lo que respondió el servidor, o el motivo por el que se devolvió a la cola.</summary>
    public string? MotivoRechazo { get; init; }

    public int Intentos { get; init; }

    public required DateTime CreadaEnUtc { get; init; }

    public DateTime? ActualizadaEnUtc { get; init; }
}
