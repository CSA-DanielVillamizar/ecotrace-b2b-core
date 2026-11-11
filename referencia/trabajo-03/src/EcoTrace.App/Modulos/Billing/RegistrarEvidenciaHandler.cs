using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Billing;

/// <summary>Registra la evidencia de entrega de un pago (<c>POST /api/pagos/{id}/evidencias</c>).</summary>
public sealed class RegistrarEvidenciaHandler(ConfiguracionServicios servicios) : IManejadorOperacion
{
    public string Modulo => "Billing";

    public string Tipo => "RegistrarEvidencia";

    public HttpRequestMessage Construir(OperacionPendiente operacion)
    {
        using var datos = JsonDocument.Parse(operacion.Payload);
        var raiz = datos.RootElement;
        var pagoId = raiz.GetProperty("pagoId").GetGuid();
        var referencia = raiz.TryGetProperty("referencia", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;

        return new HttpRequestMessage(HttpMethod.Post, new Uri(servicios.Billing, $"/api/pagos/{pagoId}/evidencias"))
        {
            Content = JsonContent.Create(new { descripcion = raiz.GetProperty("descripcion").GetString(), referencia })
        };
    }
}
