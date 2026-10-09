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
        builder.Services.AddSingleton<IProveedorDeSesion, ProveedorDeSesionIdentity>();
        builder.Services.AddTransient<IdentityPage>();
        return builder;
    }
}
