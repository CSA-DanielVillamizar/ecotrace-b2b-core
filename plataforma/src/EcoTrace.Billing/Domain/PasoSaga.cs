namespace EcoTrace.Billing.Domain;

public sealed class PasoSaga
{
    private PasoSaga()
    {
        Nombre = string.Empty;
    }

    public Guid PasoSagaId { get; private set; }

    public Guid SagaId { get; private set; }

    public int Orden { get; private set; }

    public string Nombre { get; private set; }

    public bool Compensable { get; private set; }

    public EstadoPasoSaga Estado { get; private set; }

    public int Intentos { get; private set; }

    public int IntentosCompensacion { get; private set; }

    public string? Detalle { get; private set; }

    internal static PasoSaga Crear(Guid sagaId, int orden, string nombre, bool compensable) => new()
    {
        PasoSagaId = Guid.NewGuid(),
        SagaId = sagaId,
        Orden = orden,
        Nombre = nombre,
        Compensable = compensable,
        Estado = EstadoPasoSaga.Pendiente
    };

    internal void RegistrarIntento(string? detalle)
    {
        Intentos++;
        Detalle = detalle;
    }

    internal void RegistrarIntentoCompensacion(string? detalle)
    {
        IntentosCompensacion++;
        Detalle = detalle;
    }

    internal void ActualizarDetalle(string? detalle) => Detalle = detalle;

    internal void MarcarCompletado(string? detalle)
    {
        Estado = EstadoPasoSaga.Completado;
        Detalle = detalle;
    }

    internal void MarcarFallido(string detalle)
    {
        Estado = EstadoPasoSaga.Fallido;
        Detalle = detalle;
    }

    internal void MarcarCompensado(string detalle)
    {
        Estado = EstadoPasoSaga.Compensado;
        Detalle = detalle;
    }
}