using EcoTrace.Mobile.Core.Modulos;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>Conecta el módulo de Cargo con el resto de la app al arrancar.</summary>
public static class RegistroCargo
{
    /// <summary>
    /// Registra el módulo, su pantalla y todo lo que solo este módulo necesita: la copia de cargas en su archivo SQLite, el cliente
    /// que pide las cargas, el servicio que arma la lista, las acciones del conductor y el ViewModel.
    /// </summary>
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
