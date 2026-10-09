using EcoTrace.Mobile.Core.Modulos;

namespace EcoTrace.App.Modulos.Identity;

/// <summary>
/// Módulo Identity de la app. Aquí el escuadrón de Identity declara qué acciones encola (<see cref="ModuloBase.Manejadores"/>)
/// y ajusta los textos del esqueleto. Las pantallas van en esta misma carpeta.
/// </summary>
public sealed class ModuloIdentity : ModuloBase
{
    public override string Nombre => "Identity";

    public override string Titulo => "Perfil";

    public override string Escuadron => "Identity";

    public override string Descripcion => "Tu sesión y tus datos";

    public override string Muestra => "Sesión, datos del último inicio de sesión y fecha de la última sincronización.";

    public override string AccionEncolada => "Cambio de datos de perfil y cierre de sesión en otros dispositivos.";

    public override string ConflictoDeEjemplo => "A: teléfono con formato inválido. B: organización suspendida.";
}
