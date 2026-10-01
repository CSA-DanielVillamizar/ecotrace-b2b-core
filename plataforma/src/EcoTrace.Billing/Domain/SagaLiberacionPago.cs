namespace EcoTrace.Billing.Domain;

public sealed class SagaLiberacionPago
{
    private readonly List<PasoSaga> _pasos = [];

    private SagaLiberacionPago()
    {
        CorrelationId = string.Empty;
        Motivo = string.Empty;
    }

    public Guid SagaId { get; private set; }

    public Guid PagoId { get; private set; }

    [ReferenciaExterna("CargoTracking", "Carga cuya entrega inició el Saga")]
    public Guid CargaId { get; private set; }

    [ReferenciaExterna("FleetManagement", "Vehículo que se libera al completar el Saga")]
    public Guid VehiculoId { get; private set; }

    [ReferenciaExterna("FleetManagement", "Conductor que se libera al completar el Saga")]
    public Guid ConductorId { get; private set; }

    [ReferenciaExterna("Identity", "Organización generadora del pago")]
    public Guid GeneradorTenantId { get; private set; }

    [ReferenciaExterna("Identity", "Organización transportista autorizada para el pago")]
    public Guid TransportistaTenantId { get; private set; }

    public EstadoSaga Estado { get; private set; }

    public string? Motivo { get; private set; }

    public string CorrelationId { get; private set; }

    public DateTime CreadaEn { get; private set; }

    public DateTime ActualizadaEn { get; private set; }

    public int Version { get; private set; }

    public DateTime? LeaseHasta { get; private set; }

    public string? LeaseToken { get; private set; }

    public IReadOnlyList<PasoSaga> Pasos => _pasos;

    public static SagaLiberacionPago Crear(
        Pago pago,
        Guid vehiculoId,
        Guid conductorId,
        string correlationId,
        DateTime ahoraUtc)
    {
        var saga = new SagaLiberacionPago
        {
            SagaId = Guid.NewGuid(),
            PagoId = pago.PagoId,
            CargaId = pago.CargaId,
            VehiculoId = vehiculoId,
            ConductorId = conductorId,
            GeneradorTenantId = pago.GeneradorTenantId,
            TransportistaTenantId = pago.TransportistaTenantId,
            Estado = EstadoSaga.EnCurso,
            CorrelationId = correlationId,
            CreadaEn = ahoraUtc,
            ActualizadaEn = ahoraUtc
        };

        saga._pasos.Add(PasoSaga.Crear(saga.SagaId, 1, "AutorizarPago", compensable: true));
        saga._pasos.Add(PasoSaga.Crear(saga.SagaId, 2, "LiberarFondos", compensable: true));
        saga._pasos.Add(PasoSaga.Crear(saga.SagaId, 3, "LiberarRecursos", compensable: false));
        saga._pasos.Add(PasoSaga.Crear(saga.SagaId, 4, "RegistrarAuditoria", compensable: false));
        return saga;
    }

    public PasoSaga ObtenerPaso(int orden) =>
        _pasos.Single(p => p.Orden == orden);

    public void RegistrarIntento(int orden, DateTime ahoraUtc)
    {
        ObtenerPaso(orden).RegistrarIntento(null);
        RegistrarCambio(ahoraUtc);
    }

    public void ActualizarDetallePaso(int orden, string? detalle, DateTime ahoraUtc)
    {
        ObtenerPaso(orden).ActualizarDetalle(detalle);
        RegistrarCambio(ahoraUtc);
    }

    public void CompletarPaso(int orden, string detalle, DateTime ahoraUtc)
    {
        ObtenerPaso(orden).MarcarCompletado(detalle);
        RegistrarCambio(ahoraUtc);
    }

    public void FallarPaso(int orden, string detalle, DateTime ahoraUtc)
    {
        ObtenerPaso(orden).MarcarFallido(detalle);
        RegistrarCambio(ahoraUtc);
    }

    public void RegistrarIntentoCompensacion(int orden, DateTime ahoraUtc)
    {
        ObtenerPaso(orden).RegistrarIntentoCompensacion(null);
        RegistrarCambio(ahoraUtc);
    }

    public void MarcarPasoCompensado(int orden, string detalle, DateTime ahoraUtc)
    {
        ObtenerPaso(orden).MarcarCompensado(detalle);
        RegistrarCambio(ahoraUtc);
    }

    public void RegistrarCambio(DateTime ahoraUtc)
    {
        Version++;
        ActualizadaEn = ahoraUtc;
        if (LeaseToken is not null && Estado is EstadoSaga.EnCurso or EstadoSaga.Compensando)
        {
            LeaseHasta = ahoraUtc.AddSeconds(30);
        }
    }

    public bool IntentarTomarControl(string token, DateTime ahoraUtc)
    {
        if (LeaseHasta > ahoraUtc && LeaseToken != token)
        {
            return false;
        }

        LeaseToken = token;
        LeaseHasta = ahoraUtc.AddSeconds(30);
        RegistrarCambio(ahoraUtc);
        return true;
    }

    public void ActualizarEstado(EstadoSaga estado, string? motivo, DateTime ahoraUtc)
    {
        Estado = estado;
        Motivo = motivo;
        if (estado is EstadoSaga.Completada or EstadoSaga.Compensada or EstadoSaga.Fallida or EstadoSaga.RequiereIntervencion)
        {
            LeaseToken = null;
            LeaseHasta = null;
        }

        RegistrarCambio(ahoraUtc);
    }
}