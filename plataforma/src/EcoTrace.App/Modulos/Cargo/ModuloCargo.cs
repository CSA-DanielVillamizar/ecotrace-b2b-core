using EcoTrace.Mobile.Core.Modulos;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Módulo Cargo de la app. Aquí el escuadrón de Cargo &amp; Tracking declara qué acciones encola (<see cref="ModuloBase.Manejadores"/>)
/// y ajusta los textos del esqueleto. Las pantallas van en esta misma carpeta.
/// </summary>
public sealed class ModuloCargo : ModuloBase
{
    public override string Nombre => "Cargo";

    public override string Titulo => "Cargas";

    public override string Escuadron => "Cargo & Tracking";

    public override string Descripcion => "Tus cargas y entregas";

    public override string Muestra => "Cargas asignadas, su estado, la próxima parada y la fecha de la última sincronización.";

    public override string AccionEncolada => "Cambio de estado de la carga, incluida la confirmación de entrega. Es la que dispara el Saga de pago.";

    public override string ConflictoDeEjemplo => "B: la carga fue reasignada a otro conductor.";
}
