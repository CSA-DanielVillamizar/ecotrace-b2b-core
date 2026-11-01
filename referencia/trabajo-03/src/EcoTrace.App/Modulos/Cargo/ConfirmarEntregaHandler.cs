using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Confirma la entrega de una carga. Es una acción <b>crítica</b>: dispara el Saga que libera el pago, así que no se encola con
/// la sesión degradada. Lleva el conductor que la app creía asignado: si la carga se reasignó sin señal, el servidor responde 409 de tipo B.
/// </summary>
public sealed class ConfirmarEntregaHandler(ConfiguracionServicios servicios) : IManejadorOperacion
{
    public string Modulo => "Cargo";

    public string Tipo => "ConfirmarEntrega";

    public bool EsCritica => true;

    public HttpRequestMessage Construir(OperacionPendiente operacion)
    {
        using var datos = JsonDocument.Parse(operacion.Payload);
        var raiz = datos.RootElement;
        var cargaId = raiz.GetProperty("cargaId").GetGuid();
        Guid? conductorId = raiz.TryGetProperty("conductorId", out var c) && c.ValueKind == JsonValueKind.String ? c.GetGuid() : null;

        return new HttpRequestMessage(HttpMethod.Post, new Uri(servicios.Cargo, $"/api/cargas/{cargaId}/seguimientos"))
        {
            Content = JsonContent.Create(new { estado = "Entregado", ubicacion = raiz.GetProperty("ubicacion").GetString(), conductorId })
        };
    }
}
