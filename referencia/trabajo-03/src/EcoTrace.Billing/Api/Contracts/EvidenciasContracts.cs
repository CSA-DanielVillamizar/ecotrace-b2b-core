using System.ComponentModel.DataAnnotations;
using EcoTrace.Billing.Domain;

namespace EcoTrace.Billing.Api.Contracts;

public sealed record RegistrarEvidenciaRequest
{
    [Required]
    public string? Descripcion { get; init; }

    /// <summary>Referencia opcional al archivo (foto, firma, acta). Billing guarda la referencia, no el archivo.</summary>
    public string? Referencia { get; init; }
}

public sealed record EvidenciaResponse(
    Guid EvidenciaId, Guid PagoId, Guid RegistradaPorUserId, string Descripcion, string? Referencia, DateTime RegistradaEn)
{
    public static EvidenciaResponse De(EvidenciaEntrega e) =>
        new(e.EvidenciaId, e.PagoId, e.RegistradaPorUserId, e.Descripcion, e.Referencia, e.RegistradaEn);
}
