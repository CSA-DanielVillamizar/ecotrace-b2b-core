namespace EcoTrace.CargoTracking.Domain;

/// <summary>
/// Mensaje del Outbox de Cargo &amp; Tracking. Cuando se registra una entrega, se guarda
/// un mensaje EntregaConfirmada en la misma transacción. Un publicador en segundo plano
/// lo envía a Billing con reintentos y espera creciente.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        EventId = string.Empty;
        EventType = string.Empty;
        CorrelationId = string.Empty;
        Estado = string.Empty;
        UltimoError = null;
        Error = null;
    }

    public string EventId { get; private set; }

    public string EventType { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public Guid CargaId { get; private set; }

    [ReferenciaExterna("FleetManagement", "Vehículo que transporta la carga")]
    public Guid VehiculoId { get; private set; }

    [ReferenciaExterna("FleetManagement", "Conductor que transporta la carga")]
    public Guid ConductorId { get; private set; }

    [ReferenciaExterna("Identity", "Organización que genera la carga y paga el servicio")]
    public Guid GeneradorTenantId { get; private set; }

    [ReferenciaExterna("Identity", "Transportista que ejecuta el servicio y cobra")]
    public Guid TransportistaTenantId { get; private set; }

    public string CorrelationId { get; private set; }

    public string Estado { get; private set; }

    public int Intentos { get; private set; }

    public string? UltimoError { get; private set; }

    public string? Error { get; private set; }

    public DateTime? ProximoIntentoEn { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public static OutboxMessage CrearEntregaConfirmada(
        Guid cargaId,
        Guid vehiculoId,
        Guid conductorId,
        Guid generadorTenantId,
        Guid transportistaTenantId,
        string correlationId,
        DateTime ahoraUtc)
    {
        return new OutboxMessage
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = "EntregaConfirmada",
            OccurredAt = ahoraUtc,
            CargaId = cargaId,
            VehiculoId = vehiculoId,
            ConductorId = conductorId,
            GeneradorTenantId = generadorTenantId,
            TransportistaTenantId = transportistaTenantId,
            CorrelationId = correlationId,
            Estado = "Pendiente",
            Intentos = 0,
            UltimoError = null,
            Error = null,
            ProximoIntentoEn = ahoraUtc,
            CreadoEn = ahoraUtc
        };
    }

    public void MarcarPublicado()
    {
        Estado = "Publicado";
        UltimoError = null;
        ProximoIntentoEn = null;
    }

    public void RegistrarFalloTransitorio(string error, DateTime proximoIntento)
    {
        Intentos++;
        UltimoError = error;
        ProximoIntentoEn = proximoIntento;
    }

    public void MarcarMuerto(string error)
    {
        Estado = "Muerto";
        Error = $"ALERTA - {error}";
        ProximoIntentoEn = null;
    }

    public void Reprocesar()
    {
        Estado = "Pendiente";
        Intentos = 0;
        UltimoError = null;
        Error = null;
        ProximoIntentoEn = DateTime.UtcNow;
    }
}
