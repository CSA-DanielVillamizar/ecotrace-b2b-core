using System.Text;
using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Convierte una acción de la cola en la solicitud HTTP que el servidor de Cargo &amp; Tracking espera:
/// <c>POST /api/cargas/{id}/seguimientos</c> con el estado, la ubicación y la nota.
/// <para>
/// Aquí no se ponen encabezados. El motor de sincronización agrega <c>Authorization</c>, <c>Idempotency-Key</c>
/// (el OperationId de la acción) y <c>X-Correlation-Id</c>, así que este código no puede olvidarse de la clave
/// que evita que un reintento repita el efecto.
/// </para>
/// </summary>
public abstract class ManejadorDeSeguimiento(ConfiguracionServicios servicios) : IManejadorOperacion
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Datos para una acción cuyo payload no se puede leer. Apuntan a una carga que no existe, así que el servidor
    /// la rechaza (404) y la acción queda Rechazada con su motivo. Lo importante es que <see cref="Construir"/> nunca
    /// lance una excepción: el motor no la controla y la acción quedaría "Sincronizando" bloqueando la cola entera.
    /// </summary>
    private static readonly DatosCambioDeEstado DatosIlegibles = new(Guid.Empty, string.Empty, string.Empty, null);

    public string Modulo => "Cargo";

    public abstract string Tipo { get; }

    /// <summary>Si la acción siempre lleva a un mismo estado (confirmar entrega), se fija aquí. Null usa el del payload.</summary>
    protected virtual string? EstadoFijo => null;

    public HttpRequestMessage Construir(OperacionPendiente operacion)
    {
        var datos = PayloadDeCarga.Leer(operacion.Payload) ?? DatosIlegibles;
        var cuerpo = JsonSerializer.Serialize(
            new { estado = EstadoFijo ?? datos.Estado, ubicacion = datos.Ubicacion, nota = datos.Nota }, Opciones);

        return new HttpRequestMessage(HttpMethod.Post, new Uri(servicios.Cargo, $"/api/cargas/{datos.CargaId}/seguimientos"))
        {
            Content = new StringContent(cuerpo, Encoding.UTF8, "application/json")
        };
    }
}

/// <summary>Cambio de estado de una carga que no es la entrega: pasar a en tránsito o reportar una novedad.</summary>
public sealed class CambiarEstadoHandler(ConfiguracionServicios servicios) : ManejadorDeSeguimiento(servicios)
{
    public override string Tipo => "CambiarEstado";
}

/// <summary>
/// La confirmación de entrega, la acción más crítica: el servidor deja el evento en su Outbox y de ahí arranca el
/// Saga que libera el pago. Siempre lleva la carga a Entregado, sin importar lo que diga el payload.
/// </summary>
public sealed class ConfirmarEntregaHandler(ConfiguracionServicios servicios) : ManejadorDeSeguimiento(servicios)
{
    public override string Tipo => "ConfirmarEntrega";

    protected override string? EstadoFijo => EstadosDeCarga.Entregado;
}

/// <summary>Los manejadores que el módulo de Cargo registra en la app.</summary>
public static class ManejadoresDeCargo
{
    public static IReadOnlyList<IManejadorOperacion> Crear(ConfiguracionServicios servicios) =>
        [new CambiarEstadoHandler(servicios), new ConfirmarEntregaHandler(servicios)];
}
