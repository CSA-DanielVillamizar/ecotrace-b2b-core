using EcoTrace.CargoTracking.Domain;

namespace EcoTrace.CargoTracking.Api.Contracts;

/// <summary>
/// Forma en que un MensajeOutbox se expone por HTTP. Sigue el mismo patrón que CargaResponse y
/// SeguimientoResponse (CargoContracts.cs): un record con un método estático De(...) que separa
/// el contrato público del modelo de dominio -- así, si MensajeOutbox cambia internamente, el
/// contrato de la API no tiene por qué cambiar con él.
/// </summary>
public sealed record MensajeOutboxResponse(
    Guid Id, string EventId, string CorrelationId, Guid CargaId,
    Guid VehiculoId, Guid ConductorId, Guid GeneradorTenantId, Guid TransportistaTenantId,
    DateTime OcurrioEn, EstadoMensaje Estado, int Intentos, DateTime ProximoIntentoEn, string? UltimoError)
{
    public static MensajeOutboxResponse De(MensajeOutbox m) =>
        new(m.Id, m.EventId, m.CorrelationId, m.CargaId, m.VehiculoId, m.ConductorId,
            m.GeneradorTenantId, m.TransportistaTenantId, m.OcurrioEn, m.Estado,
            m.Intentos, m.ProximoIntentoEn, m.UltimoError);
}
