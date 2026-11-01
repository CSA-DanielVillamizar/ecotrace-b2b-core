namespace EcoTrace.Billing.Domain;

public enum EstadoSaga
{
    /// <summary>Avanzando paso a paso.</summary>
    EnCurso = 1,

    Completada = 2,

    /// <summary>Un paso falló y se están deshaciendo, en orden inverso, los que ya se habían completado.</summary>
    Compensando = 3,

    /// <summary>Falló, pero todo lo que se había hecho quedó revertido.</summary>
    Compensada = 4,

    /// <summary>Falló antes de mover nada (por ejemplo, una organización suspendida): no hubo nada que revertir.</summary>
    Fallida = 5,

    /// <summary>La compensación misma no pudo terminar. Necesita a una persona.</summary>
    RequiereIntervencion = 6
}

public enum EstadoPaso
{
    Pendiente = 1,
    Completado = 2,
    Fallido = 3,
    Compensado = 4
}

/// <summary>Nombres de los pasos del Saga "Liberar Pago en Escrow" (ADR 0003), en orden de ejecución.</summary>
public static class PasosDeLiberacion
{
    public const string AutorizarPago = "AutorizarPago";
    public const string LiberarFondos = "LiberarFondos";
    public const string LiberarRecursos = "LiberarRecursos";
    public const string RegistrarAuditoria = "RegistrarAuditoria";
}

/// <summary>Un paso del Saga: qué hace, en qué estado está y cuántas veces se intentó.</summary>
public sealed class SagaPaso
{
    private SagaPaso()
    {
        Nombre = string.Empty;
    }

    public Guid PasoId { get; private set; }

    public Guid SagaId { get; private set; }

    public int Orden { get; private set; }

    public string Nombre { get; private set; }

    /// <summary>Si el paso tiene una acción que lo deshace. Solo estos participan en la compensación.</summary>
    public bool Compensable { get; private set; }

    public EstadoPaso Estado { get; internal set; }

    /// <summary>Intentos fallidos de ejecutarlo hacia adelante.</summary>
    public int Intentos { get; internal set; }

    /// <summary>Intentos fallidos de compensarlo.</summary>
    public int IntentosCompensacion { get; internal set; }

    /// <summary>Último resultado o error, en palabras que pueda leer una persona.</summary>
    public string? Detalle { get; internal set; }

    public DateTime ActualizadoEn { get; internal set; }

    internal static SagaPaso Crear(Guid sagaId, int orden, string nombre, bool compensable, DateTime ahoraUtc) =>
        new()
        {
            PasoId = Guid.NewGuid(),
            SagaId = sagaId,
            Orden = orden,
            Nombre = nombre,
            Compensable = compensable,
            Estado = EstadoPaso.Pendiente,
            ActualizadoEn = ahoraUtc
        };
}

/// <summary>
/// Estado durable del Saga "Liberar Pago en Escrow", orquestado por Billing (ADR 0003). Cada cambio
/// se guarda, así que si el proceso se reinicia el Saga continúa donde quedó. El orquestador decide
/// qué llamar; esta clase decide qué transiciones son válidas y en qué estado queda el Saga.
/// </summary>
public sealed class SagaLiberacionPago
{
    private readonly List<SagaPaso> _pasos = [];

    private SagaLiberacionPago()
    {
        EventoOrigen = string.Empty;
        CorrelationId = string.Empty;
    }

    public Guid SagaId { get; private set; }

    /// <summary>Pago que se libera. Es del mismo contexto, por eso es clave foránea real. Uno por pago.</summary>
    public Guid PagoId { get; private set; }

    [ReferenciaExterna("CargoTracking", "Carga entregada que dispara el Saga")]
    public Guid CargaId { get; private set; }

    [ReferenciaExterna("FleetManagement", "Vehículo que hay que liberar al terminar")]
    public Guid VehiculoId { get; private set; }

    [ReferenciaExterna("FleetManagement", "Conductor que hay que liberar al terminar")]
    public Guid ConductorId { get; private set; }

    /// <summary>eventId del mensaje que inició el Saga. Es texto: identifica un mensaje, no una entidad.</summary>
    public string EventoOrigen { get; private set; }

    /// <summary>Hilo de trazabilidad que acompaña todas las llamadas del Saga.</summary>
    public string CorrelationId { get; private set; }

    public EstadoSaga Estado { get; private set; }

    /// <summary>Por qué falló o se compensó. Vacío mientras todo va bien.</summary>
    public string? Motivo { get; private set; }

    /// <summary>Token de concurrencia: si dos ejecutores avanzan el mismo Saga a la vez, el segundo falla al guardar.</summary>
    public int Version { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ActualizadoEn { get; private set; }

    /// <summary>Desde cuándo el ejecutor puede volver a tomarlo. Es lo que implementa la espera entre reintentos.</summary>
    public DateTime ProximoIntentoEn { get; private set; }

    public IReadOnlyList<SagaPaso> Pasos => _pasos;

    public bool EsTerminal => Estado is EstadoSaga.Completada or EstadoSaga.Compensada
        or EstadoSaga.Fallida or EstadoSaga.RequiereIntervencion;

    /// <summary>Primer paso sin ejecutar, mientras el Saga avanza. Null si no hay o si no está en curso.</summary>
    public SagaPaso? PasoPendiente => Estado != EstadoSaga.EnCurso
        ? null
        : _pasos.OrderBy(p => p.Orden).FirstOrDefault(p => p.Estado == EstadoPaso.Pendiente);

    /// <summary>Siguiente paso a deshacer: el completado y compensable más reciente. Null si no se está compensando.</summary>
    public SagaPaso? PasoPorCompensar => Estado != EstadoSaga.Compensando
        ? null
        : _pasos.Where(p => p.Compensable && p.Estado == EstadoPaso.Completado)
            .OrderByDescending(p => p.Orden)
            .FirstOrDefault();

    public static SagaLiberacionPago Iniciar(
        Pago pago, Guid vehiculoId, Guid conductorId, string eventoOrigen, string correlationId, DateTime ahoraUtc)
    {
        if (pago.EstadoEscrow != EstadoEscrow.EnCustodia)
        {
            throw DomainException.Conflict($"El pago ya está {pago.EstadoEscrow}. Solo un pago en custodia se puede liberar.");
        }

        if (vehiculoId == Guid.Empty || conductorId == Guid.Empty)
        {
            throw DomainException.Validation("El Saga necesita el vehículo y el conductor que hay que liberar.");
        }

        var saga = new SagaLiberacionPago
        {
            SagaId = Guid.NewGuid(),
            PagoId = pago.PagoId,
            CargaId = pago.CargaId,
            VehiculoId = vehiculoId,
            ConductorId = conductorId,
            EventoOrigen = eventoOrigen,
            CorrelationId = correlationId,
            Estado = EstadoSaga.EnCurso,
            Version = 1,
            CreadoEn = ahoraUtc,
            ActualizadoEn = ahoraUtc,
            ProximoIntentoEn = ahoraUtc
        };

        saga._pasos.Add(SagaPaso.Crear(saga.SagaId, 1, PasosDeLiberacion.AutorizarPago, compensable: true, ahoraUtc));
        saga._pasos.Add(SagaPaso.Crear(saga.SagaId, 2, PasosDeLiberacion.LiberarFondos, compensable: true, ahoraUtc));
        saga._pasos.Add(SagaPaso.Crear(saga.SagaId, 3, PasosDeLiberacion.LiberarRecursos, compensable: false, ahoraUtc));
        saga._pasos.Add(SagaPaso.Crear(saga.SagaId, 4, PasosDeLiberacion.RegistrarAuditoria, compensable: false, ahoraUtc));
        return saga;
    }

    public void CompletarPaso(SagaPaso paso, string detalle, DateTime ahoraUtc)
    {
        paso.Estado = EstadoPaso.Completado;
        paso.Detalle = detalle;
        paso.ActualizadoEn = ahoraUtc;
        ProximoIntentoEn = ahoraUtc;

        if (PasoPendiente is null)
        {
            Estado = EstadoSaga.Completada;
        }

        Tocar(ahoraUtc);
    }

    /// <summary>El paso falló pero todavía tiene intentos: se agenda otro para más tarde.</summary>
    public void ProgramarReintento(SagaPaso paso, string error, DateTime proximoIntentoUtc, DateTime ahoraUtc)
    {
        paso.Intentos++;
        paso.Detalle = error;
        paso.ActualizadoEn = ahoraUtc;
        ProximoIntentoEn = proximoIntentoUtc;
        Tocar(ahoraUtc);
    }

    /// <summary>
    /// El paso falló definitivamente. Si antes se completó algún paso compensable, el Saga pasa a
    /// Compensando; si no, no hay nada que deshacer y queda Fallida.
    /// </summary>
    public void FallarPaso(SagaPaso paso, string motivo, DateTime ahoraUtc)
    {
        paso.Estado = EstadoPaso.Fallido;
        paso.Intentos++;
        paso.Detalle = motivo;
        paso.ActualizadoEn = ahoraUtc;
        Motivo = motivo;
        ProximoIntentoEn = ahoraUtc;
        Estado = _pasos.Any(p => p.Compensable && p.Estado == EstadoPaso.Completado)
            ? EstadoSaga.Compensando
            : EstadoSaga.Fallida;
        Tocar(ahoraUtc);
    }

    public void MarcarCompensado(SagaPaso paso, string detalle, DateTime ahoraUtc)
    {
        paso.Estado = EstadoPaso.Compensado;
        paso.Detalle = detalle;
        paso.ActualizadoEn = ahoraUtc;
        ProximoIntentoEn = ahoraUtc;

        if (PasoPorCompensar is null)
        {
            Estado = EstadoSaga.Compensada;
        }

        Tocar(ahoraUtc);
    }

    public void ProgramarReintentoCompensacion(SagaPaso paso, string error, DateTime proximoIntentoUtc, DateTime ahoraUtc)
    {
        paso.IntentosCompensacion++;
        paso.Detalle = error;
        paso.ActualizadoEn = ahoraUtc;
        ProximoIntentoEn = proximoIntentoUtc;
        Tocar(ahoraUtc);
    }

    /// <summary>La compensación no pudo completarse. Se detiene todo y se deja constancia para una persona.</summary>
    public void RequerirIntervencion(SagaPaso paso, string motivo, DateTime ahoraUtc)
    {
        paso.IntentosCompensacion++;
        paso.Detalle = motivo;
        paso.ActualizadoEn = ahoraUtc;
        Motivo = $"{Motivo} La compensación de {paso.Nombre} no pudo completarse: {motivo}";
        Estado = EstadoSaga.RequiereIntervencion;
        Tocar(ahoraUtc);
    }

    private void Tocar(DateTime ahoraUtc)
    {
        Version++;
        ActualizadoEn = ahoraUtc;
    }
}
