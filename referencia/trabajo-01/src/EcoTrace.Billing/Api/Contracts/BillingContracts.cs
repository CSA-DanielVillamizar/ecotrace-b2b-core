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
