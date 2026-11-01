using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Fleet;

/// <summary>Reporta una novedad del vehículo (<c>POST /api/vehiculos/{id}/novedades</c>).</summary>
public sealed class ReportarNovedadHandler(ConfiguracionServicios servicios) : IManejadorOperacion
{
    public string Modulo => "Fleet";

    public string Tipo => "ReportarNovedad";

    public HttpRequestMessage Construir(OperacionPendiente operacion)
    {
        using var datos = JsonDocument.Parse(operacion.Payload);
        var raiz = datos.RootElement;
        var vehiculoId = raiz.GetProperty("vehiculoId").GetGuid();

        return new HttpRequestMessage(HttpMethod.Post, new Uri(servicios.Fleet, $"/api/vehiculos/{vehiculoId}/novedades"))
        {
            Content = JsonContent.Create(new { cargaId = raiz.GetProperty("cargaId").GetGuid(), descripcion = raiz.GetProperty("descripcion").GetString() })
        };
    }
}
