using EcoTrace.Mobile.Core.Modulos;

namespace EcoTrace.App.Modulos.Billing;

public static class RegistroBilling
{
    /// <summary>Registra el módulo, su pantalla y los servicios que solo este módulo necesita.</summary>
    public static MauiAppBuilder AddModuloBilling(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ModuloBilling>();
        builder.Services.AddSingleton<IModuloApp>(sp => sp.GetRequiredService<ModuloBilling>());
        builder.Services.AddTransient<BillingPage>();
        return builder;
    }
}
