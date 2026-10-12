using EcoTrace.Mobile.Core.Modulos;

namespace EcoTrace.App.Modulos.Fleet;

/// <summary>
/// Módulo Fleet de la app. Aquí el escuadrón de Fleet Management declara qué acciones encola (<see cref="ModuloBase.Manejadores"/>)
/// y ajusta los textos del esqueleto. Las pantallas van en esta misma carpeta.
/// </summary>
public sealed class ModuloFleet : ModuloBase
{
    public override string Nombre => "Fleet";

    public override string Titulo => "Vehículo";

    public override string Escuadron => "Fleet Management";

    public override string Descripcion => "Tu vehículo y tu estado";

    public override string Muestra => "Vehículo y conductor asignados, con su estado.";

    public override string AccionEncolada => "Reportar una novedad del vehículo (POST /api/vehiculos/{id}/novedades, ruta nueva).";

    public override string ConflictoDeEjemplo => "B: el vehículo fue reservado para otra carga mientras no había señal.";
}
