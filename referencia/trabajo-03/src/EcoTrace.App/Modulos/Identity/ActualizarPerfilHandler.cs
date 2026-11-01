using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Identity;

/// <summary>Cambia el teléfono de la propia cuenta. El motor agrega el token y la <c>Idempotency-Key</c>.</summary>
public sealed class ActualizarPerfilHandler(ConfiguracionServicios servicios) : IManejadorOperacion
{
    public string Modulo => "Identity";

    public string Tipo => "ActualizarPerfil";

    public HttpRequestMessage Construir(OperacionPendiente operacion)
    {
        using var datos = JsonDocument.Parse(operacion.Payload);
        var userId = datos.RootElement.GetProperty("userId").GetGuid();
        var telefono = datos.RootElement.GetProperty("telefono").GetString();

        return new HttpRequestMessage(HttpMethod.Put, new Uri(servicios.Identity, $"/api/users/{userId}/perfil"))
        {
            Content = JsonContent.Create(new { telefono })
        };
    }
}
