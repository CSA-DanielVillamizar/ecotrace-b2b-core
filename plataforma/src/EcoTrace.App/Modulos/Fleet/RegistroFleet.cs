using EcoTrace.Mobile.Core.Modulos;

namespace EcoTrace.App.Modulos.Fleet;

public static class RegistroFleet
{
    /// <summary>Registra el módulo, su pantalla y los servicios que solo este módulo necesita.</summary>
    public static MauiAppBuilder AddModuloFleet(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ModuloFleet>();
        builder.Services.AddSingleton<IModuloApp>(sp => sp.GetRequiredService<ModuloFleet>());
        builder.Services.AddTransient<FleetPage>();
        return builder;
    }
}
