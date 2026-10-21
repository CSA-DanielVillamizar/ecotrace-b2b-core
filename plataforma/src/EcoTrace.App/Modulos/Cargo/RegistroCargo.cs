using EcoTrace.Mobile.Core.Modulos;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EcoTrace.App.Modulos.Cargo;

public static class RegistroCargo
{
    /// <summary>Registra el módulo, su pantalla y los servicios que solo este módulo necesita.</summary>
    public static MauiAppBuilder AddModuloCargo(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton<ModuloCargo>();
        builder.Services.AddSingleton<IModuloApp>(sp => sp.GetRequiredService<ModuloCargo>());

        // La copia de las cargas vive en su propio archivo SQLite dentro del almacenamiento privado de la app.
        // Igual que la cola, nunca guarda tokens: eso es de SecureStorage (ADR 0005).
        builder.Services.AddSingleton<ICacheDeCargas>(_ =>
            new CacheDeCargasSqlite(Path.Combine(FileSystem.AppDataDirectory, "ecotrace-cargo-cache.db3")));

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ClienteDeCargas>();
        builder.Services.AddSingleton<ServicioDeCargas>();
        builder.Services.AddSingleton<AccionesDeCarga>();
        builder.Services.AddSingleton<CargoViewModel>();
        builder.Services.AddTransient<CargoPage>();
        return builder;
    }
}
