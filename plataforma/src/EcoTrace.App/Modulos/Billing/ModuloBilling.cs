using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Billing;

/// <summary>
/// Módulo Billing de la app. Permite al conductor registrar evidencia de entrega de un pago en custodia (ADR 0005).
/// </summary>
public sealed class ModuloBilling(ConfiguracionServicios configuracion, IColaLocal cola) : ModuloBase
{
    private readonly ManejadorRegistrarEvidencia _manejador = new(configuracion);

    public override string Nombre => "Billing";

    public override string Titulo => "Pagos";

    public override string Escuadron => "Billing & Escrow";

    public override string Descripcion => "El estado de tus pagos";

    public override string Muestra => "Pagos de tu organización con su estado: EnCustodia, Liberado o EnDisputa.";

    public override string AccionEncolada => "Registrar evidencia de entrega (POST /api/pagos/{id}/evidencias).";

    public override string ConflictoDeEjemplo => "B: el pago ya fue liberado por otro proceso.";

    public override IReadOnlyList<IManejadorOperacion> Manejadores => [_manejador];

    public Task EncolarEvidenciaAsync(Guid pagoId, string evidencia, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            pagoId = pagoId.ToString(),
            evidencia = evidencia.Trim()
        });

        return cola.EncolarAsync(Nombre, "RegistrarEvidencia", payload, ct);
    }
}
