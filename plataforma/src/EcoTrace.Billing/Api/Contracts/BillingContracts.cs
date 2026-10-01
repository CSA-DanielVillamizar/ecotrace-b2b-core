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

public sealed record EntregaConfirmadaRequest
{
    [Required, MaxLength(128)]
    public string? EventId { get; init; }

    [Required, MaxLength(50)]
    public string? EventType { get; init; }

    public DateTimeOffset? OccurredAt { get; init; }

    [Required]
    public Guid? CargaId { get; init; }

    [Required]
    public Guid? VehiculoId { get; init; }

    [Required]
    public Guid? ConductorId { get; init; }

    [Required]
    public Guid? GeneradorTenantId { get; init; }

    [Required]
    public Guid? TransportistaTenantId { get; init; }

    public string? CorrelationId { get; init; }
}

public sealed record EventoAceptadoResponse(string Resultado, Guid SagaId);

public sealed record EventoDuplicadoResponse(string Resultado);

public sealed record PasoSagaResponse(
    int Orden,
    string Nombre,
    bool Compensable,
    EstadoPasoSaga Estado,
    int Intentos,
    int IntentosCompensacion,
    string? Detalle)
{
    public static PasoSagaResponse De(PasoSaga paso) => new(
        paso.Orden, paso.Nombre, paso.Compensable, paso.Estado,
        paso.Intentos, paso.IntentosCompensacion, paso.Detalle);
}

public sealed record SagaResponse(
    Guid SagaId,
    Guid PagoId,
    Guid CargaId,
    EstadoSaga Estado,
    string? Motivo,
    string CorrelationId,
    IReadOnlyList<PasoSagaResponse> Pasos)
{
    public static SagaResponse De(SagaLiberacionPago saga) => new(
        saga.SagaId,
        saga.PagoId,
        saga.CargaId,
        saga.Estado,
        saga.Motivo,
        saga.CorrelationId,
        saga.Pasos.OrderBy(p => p.Orden).Select(PasoSagaResponse.De).ToList());
}
