using EcoTrace.Mobile.Core.Modulos;

namespace EcoTrace.App.Modulos.Cargo;

public static class RegistroCargo
{
    /// <summary>Registra el módulo, su pantalla y los servicios que solo este módulo necesita.</summary>
    public static MauiAppBuilder AddModuloCargo(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ModuloCargo>();
        builder.Services.AddSingleton<IModuloApp>(sp => sp.GetRequiredService<ModuloCargo>());
        builder.Services.AddTransient<CargoPage>();
        return builder;
    }
}
