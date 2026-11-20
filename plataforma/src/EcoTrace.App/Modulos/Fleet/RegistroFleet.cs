using EcoTrace.Mobile.Core.Modulos;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EcoTrace.App.Modulos.Fleet;

public static class RegistroFleet
{
    /// <summary>Registra el módulo, su pantalla y los servicios que solo este módulo necesita.</summary>
    public static MauiAppBuilder AddModuloFleet(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ModuloFleet>();
        builder.Services.AddSingleton<IModuloApp>(sp => sp.GetRequiredService<ModuloFleet>());

        // La copia del vehículo y el conductor vive en su propio archivo SQLite, en el almacenamiento privado de la
        // app. Igual que la cola, nunca guarda tokens: eso es de SecureStorage y lo maneja Identity (ADR 0005).
        builder.Services.AddSingleton(_ =>
            new CacheDeFleetSqlite(Path.Combine(FileSystem.AppDataDirectory, "ecotrace-fleet-cache.db3")));

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ClienteFleet>();
        builder.Services.AddSingleton<ServicioFleet>();
        builder.Services.AddTransient<FleetViewModel>();
        builder.Services.AddTransient<FleetPage>();
        return builder;
    }
}
