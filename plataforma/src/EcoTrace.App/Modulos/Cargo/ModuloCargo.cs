using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// El módulo de Cargo de la app: declara qué acciones guarda en la cola y los textos con los que se presenta en el menú.
/// La lógica está en la carpeta <c>Logica/</c> y la pantalla en <c>CargoPage</c>.
/// </summary>
/// <param name="servicios">Las direcciones de los servicios, que los manejadores usan para armar sus solicitudes.</param>
public sealed class ModuloCargo(ConfiguracionServicios servicios) : ModuloBase
{
    /// <summary>El nombre del módulo en la cola: «Cargo».</summary>
    public override string Nombre => ServicioDeCargas.Modulo;

    /// <summary>El título que se ve en el menú.</summary>
    public override string Titulo => "Cargas";

    /// <summary>El escuadrón dueño del módulo.</summary>
    public override string Escuadron => "Cargo & Tracking";

    /// <summary>Una frase corta que explica para qué sirve.</summary>
    public override string Descripcion => "Tus cargas y entregas";

    /// <summary>Qué datos muestra el módulo.</summary>
    public override string Muestra => "Cargas asignadas, su estado, la próxima parada y la fecha de la última sincronización.";

    /// <summary>Qué acciones guarda el módulo en la cola cuando no hay conexión.</summary>
    public override string AccionEncolada => "Cambio de estado de la carga, incluida la confirmación de entrega. Es la que dispara el Saga de pago.";

    /// <summary>Un ejemplo de conflicto que el módulo sabe mostrar.</summary>
    public override string ConflictoDeEjemplo => "B: la carga ya cambió de estado en el servidor (por ejemplo, otra persona ya la entregó).";

    /// <summary>Los dos tipos de acción que el módulo guarda en la cola: <c>CambiarEstado</c> y <c>ConfirmarEntrega</c>.</summary>
    public override IReadOnlyList<IManejadorOperacion> Manejadores { get; } = ManejadoresDeCargo.Crear(servicios);
}
