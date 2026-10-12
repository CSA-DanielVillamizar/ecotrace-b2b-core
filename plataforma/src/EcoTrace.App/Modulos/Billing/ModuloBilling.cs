using EcoTrace.Mobile.Core.Modulos;

namespace EcoTrace.App.Modulos.Billing;

/// <summary>
/// Módulo Billing de la app. Aquí el escuadrón de Billing &amp; Escrow declara qué acciones encola (<see cref="ModuloBase.Manejadores"/>)
/// y ajusta los textos del esqueleto. Las pantallas van en esta misma carpeta.
/// </summary>
public sealed class ModuloBilling : ModuloBase
{
    public override string Nombre => "Billing";

    public override string Titulo => "Pagos";

    public override string Escuadron => "Billing & Escrow";

    public override string Descripcion => "El estado de tus pagos";

    public override string Muestra => "Pagos de tu organización con su estado: EnCustodia, Liberado o EnDisputa.";

    public override string AccionEncolada => "Registrar evidencia de entrega (POST /api/pagos/{id}/evidencias, ruta nueva).";

    public override string ConflictoDeEjemplo => "B: el pago ya fue liberado por otro proceso.";
}
