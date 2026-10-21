using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Módulo Cargo de la app: declara qué acciones encola (<see cref="Manejadores"/>) y los textos del esqueleto.
/// La lógica vive en <c>Logica/</c> y la pantalla en <c>CargoPage</c>.
/// </summary>
public sealed class ModuloCargo(ConfiguracionServicios servicios) : ModuloBase
{
    public override string Nombre => ServicioDeCargas.Modulo;

    public override string Titulo => "Cargas";

    public override string Escuadron => "Cargo & Tracking";

    public override string Descripcion => "Tus cargas y entregas";

    public override string Muestra => "Cargas asignadas, su estado, la próxima parada y la fecha de la última sincronización.";

    public override string AccionEncolada => "Cambio de estado de la carga, incluida la confirmación de entrega. Es la que dispara el Saga de pago.";

    public override string ConflictoDeEjemplo => "B: la carga ya cambió de estado en el servidor (por ejemplo, otra persona ya la entregó).";

    /// <summary>Los dos tipos de acción que el módulo encola: <c>CambiarEstado</c> y <c>ConfirmarEntrega</c>.</summary>
    public override IReadOnlyList<IManejadorOperacion> Manejadores { get; } = ManejadoresDeCargo.Crear(servicios);
}
