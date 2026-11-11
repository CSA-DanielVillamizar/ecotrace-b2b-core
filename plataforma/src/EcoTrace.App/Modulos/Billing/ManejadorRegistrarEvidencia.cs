using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Billing;

/// <summary>
/// Convierte la acción encolada "RegistrarEvidencia" en la petición HTTP hacia el backend de Billing (ADR 0005).
/// </summary>
public sealed class ManejadorRegistrarEvidencia(ConfiguracionServicios configuracion) : IManejadorOperacion
{
    public string Modulo => "Billing";

    public string Tipo => "RegistrarEvidencia";

    public HttpRequestMessage Construir(OperacionPendiente operacion)
    {
        using var documento = JsonDocument.Parse(operacion.Payload);
        var raiz = documento.RootElement;
        var pagoId = raiz.GetProperty("pagoId").GetString();
        var evidencia = raiz.GetProperty("evidencia").GetString();

        var ruta = new Uri(configuracion.Billing, $"api/pagos/{pagoId}/evidencias");
        var mensaje = new HttpRequestMessage(HttpMethod.Post, ruta)
        {
            Content = JsonContent.Create(new { evidencia })
        };

        return mensaje;
    }
}

