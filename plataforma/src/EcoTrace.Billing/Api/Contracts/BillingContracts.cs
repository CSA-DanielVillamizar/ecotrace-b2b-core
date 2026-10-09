using System.ComponentModel.DataAnnotations;
using EcoTrace.Billing.Domain;

namespace EcoTrace.Billing.Api.Contracts;

public sealed record CrearPagoRequest
{
    [Required]
    public Guid? GeneradorTenantId { get; init; }

    [Required]
    public Guid? TransportistaTenantId { get; init; }

    /// <summary>CargaId emitido por Cargo &amp; Tracking.</summary>
    [Required]
    public Guid? CargaId { get; init; }

    /// <summary>Monto en pesos colombianos, con máximo dos decimales.</summary>
    [Required]
    public decimal? Monto { get; init; }
}

public sealed record AuditoriaResponse(Guid AuditoriaId, string Accion, EstadoEscrow EstadoResultante, DateTime OcurridoEn)
{
    public static AuditoriaResponse De(AuditoriaFinanciera a) =>
        new(a.AuditoriaId, a.Accion, a.EstadoResultante, a.OcurridoEn);
}

public sealed record PagoResponse(
    Guid PagoId, Guid GeneradorTenantId, Guid TransportistaTenantId, Guid CargaId,
    decimal Monto, string Moneda, EstadoEscrow EstadoEscrow, DateTime CreadoEn, DateTime ActualizadoEn)
{
    public static PagoResponse De(Pago p) =>
        new(p.PagoId, p.GeneradorTenantId, p.TransportistaTenantId, p.CargaId,
            p.Monto, p.Moneda, p.EstadoEscrow, p.CreadoEn, p.ActualizadoEn);
}

public sealed record PagoDetalleResponse(PagoResponse Pago, IReadOnlyList<AuditoriaResponse> Auditoria)
{
    public static PagoDetalleResponse De(Pago p) =>
        new(PagoResponse.De(p), p.Auditoria.OrderBy(a => a.OcurridoEn).Select(AuditoriaResponse.De).ToList());
}

public sealed record EmitirFacturaRequest
{
    [Required]
    public Guid? GeneradorTenantId { get; init; }

    [Required]
    public Guid? TransportistaTenantId { get; init; }

    [Required]
    public Guid? CargaId { get; init; }

    [Required]
    public decimal? Monto { get; init; }
}

public sealed record FacturaResponse(
    Guid FacturaId, string Numero, Guid CargaId, Guid GeneradorTenantId, Guid TransportistaTenantId,
    decimal Monto, DateTime EmitidaEn)
{
    public static FacturaResponse De(Factura f) =>
        new(f.FacturaId, f.Numero, f.CargaId, f.GeneradorTenantId, f.TransportistaTenantId, f.Monto, f.EmitidaEn);
}

/// <summary>
/// Evento EntregaConfirmada tal como lo publica Cargo &amp; Tracking (ADR 0002, sección 4). Solo
/// llevan identificadores: Billing no recibe el monto porque ya lo conoce por el pago de la carga.
/// </summary>
public sealed record EntregaConfirmadaRequest
{
    /// <summary>Identifica al mensaje. Con él Billing detecta un reenvío.</summary>
    [Required]
    public Guid? EventId { get; init; }

    public string? EventType { get; init; }

    public DateTime? OccurredAt { get; init; }

    [Required]
    public Guid? CargaId { get; init; }

    /// <summary>Vehículo asignado a la carga (Fleet Management). Billing lo necesita para pedir su liberación.</summary>
    [Required]
    public Guid? VehiculoId { get; init; }

    [Required]
    public Guid? ConductorId { get; init; }

    public Guid? GeneradorTenantId { get; init; }

    public Guid? TransportistaTenantId { get; init; }

    public string? CorrelationId { get; init; }
}

/// <summary>Resultado de aceptar un evento: aceptado (Saga creado) o duplicado (ya se había procesado).</summary>
public sealed record EventoProcesadoResponse(string Resultado, Guid? SagaId, string? Detalle);

public sealed record SagaPasoResponse(
    int Orden, string Nombre, bool Compensable, EstadoPaso Estado, int Intentos, int IntentosCompensacion,
    string? Detalle, DateTime ActualizadoEn)
{
    public static SagaPasoResponse De(SagaPaso p) =>
        new(p.Orden, p.Nombre, p.Compensable, p.Estado, p.Intentos, p.IntentosCompensacion, p.Detalle, p.ActualizadoEn);
}

public sealed record SagaResponse(
    Guid SagaId, Guid PagoId, Guid CargaId, EstadoSaga Estado, string? Motivo, string CorrelationId,
    string EventoOrigen, DateTime CreadoEn, DateTime ActualizadoEn, DateTime ProximoIntentoEn,
    IReadOnlyList<SagaPasoResponse> Pasos)
{
    public static SagaResponse De(SagaLiberacionPago s) =>
        new(s.SagaId, s.PagoId, s.CargaId, s.Estado, s.Motivo, s.CorrelationId, s.EventoOrigen,
            s.CreadoEn, s.ActualizadoEn, s.ProximoIntentoEn,
            s.Pasos.OrderBy(p => p.Orden).Select(SagaPasoResponse.De).ToList());
}
