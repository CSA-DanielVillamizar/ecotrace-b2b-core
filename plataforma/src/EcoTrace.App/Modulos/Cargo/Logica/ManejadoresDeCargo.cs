using System.Text;
using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Convierte una acción de la cola en la solicitud que el servidor de Cargo espera: un <c>POST</c> a
/// <c>/api/cargas/{id}/seguimientos</c> con el estado, la ubicación y la nota.
/// <para>
/// Aquí no se ponen encabezados. El motor de sincronización agrega solo <c>Authorization</c>, <c>Idempotency-Key</c> (el número
/// de la operación) y <c>X-Correlation-Id</c>, así que este código no puede olvidarse de la clave que evita que un reintento
/// repita el efecto.
/// </para>
/// </summary>
/// <param name="servicios">Las direcciones de los servicios (de dónde sale la de Cargo).</param>
public abstract class ManejadorDeSeguimiento(ConfiguracionServicios servicios) : IManejadorOperacion
{
    /// <summary>Las reglas de escritura del JSON: nombres en camelCase.</summary>
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Datos para una acción cuyo contenido no se puede leer. Apuntan a una carga que no existe, así que el servidor la rechaza
    /// (404) y la acción queda Rechazada con su motivo. Lo importante es que <see cref="Construir"/> nunca lance un error: el
    /// motor no lo controla y la acción quedaría «Sincronizando» para siempre, bloqueando toda la cola.
    /// </summary>
    private static readonly DatosCambioDeEstado DatosIlegibles = new(Guid.Empty, string.Empty, string.Empty, null);

    /// <summary>A qué módulo pertenece la acción. Siempre «Cargo».</summary>
    public string Modulo => "Cargo";

    /// <summary>El nombre de la acción que sabe enviar (por ejemplo «ConfirmarEntrega»).</summary>
    public abstract string Tipo { get; }

    /// <summary>Si la acción siempre lleva a un mismo estado (confirmar entrega), se fija aquí. Si es null, se usa el estado que trae la acción.</summary>
    protected virtual string? EstadoFijo => null;

    /// <summary>
    /// Arma la solicitud para el servidor a partir de una acción guardada: el método (POST), la dirección de esa carga y el
    /// cuerpo con el estado, la ubicación y la nota. Nunca lanza un error: si los datos son ilegibles, arma una solicitud que el
    /// servidor rechaza.
    /// </summary>
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

/// <summary>La acción «cambiar estado»: pasar la carga a en tránsito o reportar una novedad.</summary>
public sealed class CambiarEstadoHandler(ConfiguracionServicios servicios) : ManejadorDeSeguimiento(servicios)
{
    /// <summary>El nombre de esta acción en la cola.</summary>
    public override string Tipo => "CambiarEstado";
}

/// <summary>
/// La acción «confirmar entrega», la más crítica: el servidor deja el aviso en su Outbox y de ahí arranca el Saga que libera
/// el pago. Siempre lleva la carga a Entregado, sin importar lo que diga el contenido guardado.
/// </summary>
public sealed class ConfirmarEntregaHandler(ConfiguracionServicios servicios) : ManejadorDeSeguimiento(servicios)
{
    /// <summary>El nombre de esta acción en la cola.</summary>
    public override string Tipo => "ConfirmarEntrega";

    /// <summary>Esta acción siempre lleva la carga a «Entregado».</summary>
    protected override string? EstadoFijo => EstadosDeCarga.Entregado;
}

/// <summary>Reúne los manejadores que el módulo de Cargo le entrega a la app.</summary>
public static class ManejadoresDeCargo
{
    /// <summary>Crea los dos manejadores del módulo: <c>CambiarEstado</c> y <c>ConfirmarEntrega</c>.</summary>
    public static IReadOnlyList<IManejadorOperacion> Crear(ConfiguracionServicios servicios) =>
        [new CambiarEstadoHandler(servicios), new ConfirmarEntregaHandler(servicios)];
}
