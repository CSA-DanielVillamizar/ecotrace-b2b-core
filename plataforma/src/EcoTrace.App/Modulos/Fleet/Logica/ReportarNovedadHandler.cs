using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Fleet;

/// <summary>
/// La novedad tal como se guarda en la cola: todo lo necesario para enviarla después sin volver a preguntarle nada
/// al conductor. <see cref="CargaId"/> es la carga que la app tenía en caché; con ella Fleet detecta el conflicto B.
/// </summary>
public sealed record DatosNovedad(Guid VehiculoId, Guid? CargaId, string Tipo, string Descripcion, DateTimeOffset OcurridaEn);

/// <summary>
/// Convierte una novedad de la cola en <c>POST /api/vehiculos/{id}/novedades</c>. No pone encabezados: el motor
/// agrega el token, el <c>Idempotency-Key</c> con el OperationId y el <c>X-Correlation-Id</c>.
/// </summary>
public sealed class ReportarNovedadHandler(ConfiguracionServicios servicios) : IManejadorOperacion
{
    public const string TipoAccion = "ReportarNovedad";

    public string Modulo => ServicioFleet.Modulo;

    public string Tipo => TipoAccion;

    public HttpRequestMessage Construir(OperacionPendiente operacion)
    {
        var datos = JsonSerializer.Deserialize<DatosNovedad>(operacion.Payload)!;
        return new HttpRequestMessage(HttpMethod.Post, new Uri(servicios.Fleet, $"/api/vehiculos/{datos.VehiculoId}/novedades"))
        {
            Content = JsonContent.Create(new
            {
                tipo = datos.Tipo,
                descripcion = datos.Descripcion,
                ocurridaEn = datos.OcurridaEn,
                cargaId = datos.CargaId
            })
        };
    }
}
