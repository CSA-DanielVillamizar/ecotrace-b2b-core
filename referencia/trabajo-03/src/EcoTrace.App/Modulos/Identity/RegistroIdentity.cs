using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Identity;

public static class RegistroIdentity
{
    /// <summary>Registra el módulo, su pantalla y los servicios que solo este módulo necesita.</summary>
    public static MauiAppBuilder AddModuloIdentity(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ModuloIdentity>();
        builder.Services.AddSingleton<IModuloApp>(sp => sp.GetRequiredService<ModuloIdentity>());
        builder.Services.AddSingleton<EcoTrace.App.ViewModels.PerfilViewModel>();
        // Un teléfono que el servidor rechaza por su formato (conflicto A) se corrige en esta pantalla y se reintenta.
        builder.Services.AddSingleton<EcoTrace.App.Servicios.ICorreccionDeModulo>(sp => new EcoTrace.App.Servicios.CorreccionDeModulo(
            "Identity", "ActualizarPerfil",
            rechazada => sp.GetRequiredService<EcoTrace.App.ViewModels.PerfilViewModel>().CorregirAsync(rechazada)));
        builder.Services.AddTransient<IdentityPage>();
        return builder;
    }
}
