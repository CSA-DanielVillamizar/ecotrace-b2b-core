using EcoTrace.App.Modulos.Identity.Logica;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Identity;

/// <summary>
/// Módulo Identity de la app: la sesión del conductor (que usan los cuatro módulos para sincronizar) y su perfil.
/// Encola dos acciones: el cambio de datos de perfil y el cierre de sesión en los otros dispositivos.
/// </summary>
public sealed class ModuloIdentity(ConfiguracionServicios servicios) : ModuloBase
{
    public override string Nombre => AccionesDeIdentity.Modulo;

    public override string Titulo => "Perfil";

    public override string Escuadron => "Identity";

    public override string Descripcion => "Tu sesión y tus datos";

    public override string Muestra => "Sesión, datos del último inicio de sesión y fecha de la última sincronización.";

    public override string AccionEncolada => "Cambio de datos de perfil y cierre de sesión en otros dispositivos.";

    public override string ConflictoDeEjemplo => "A: teléfono con formato inválido. B: organización suspendida.";

    public override IReadOnlyList<IManejadorOperacion> Manejadores { get; } =
        [new ActualizarPerfilHandler(servicios), new CerrarOtrasSesionesHandler(servicios)];
}
