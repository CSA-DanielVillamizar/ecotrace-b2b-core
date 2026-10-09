using EcoTrace.App.Modulos.Billing;
using EcoTrace.App.Modulos.Cargo;
using EcoTrace.App.Modulos.Fleet;
using EcoTrace.App.Modulos.Identity;
using EcoTrace.App.Paginas;
using EcoTrace.App.Servicios;
using EcoTrace.App.ViewModels;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Conexion;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App;

/// <summary>
/// Arma la app: la cola local, el motor de sincronización y el registro de módulos. Este archivo es compartido y lo
/// cambia el docente; cada escuadrón registra lo suyo desde <c>Modulos/&lt;Nombre&gt;/Registro*.cs</c>.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Configuración del entorno: sale del appsettings.json incrustado (desarrollo en Debug, producción en Release).
        builder.Services.AddSingleton(ConfiguracionDelEntorno.Cargar());

        // La cola vive en SQLite dentro del almacenamiento privado de la app. Los tokens NO van aquí (SecureStorage).
        builder.Services.AddSingleton<ColaLocalSqlite>(_ =>
            new ColaLocalSqlite(Path.Combine(FileSystem.AppDataDirectory, "ecotrace-cola.db3")));
        builder.Services.AddSingleton<IColaLocal>(sp => sp.GetRequiredService<ColaLocalSqlite>());
        builder.Services.AddSingleton<IConectividad, ConectividadMaui>();
        builder.Services.AddSingleton<BannerSinConexion>();

        builder.Services.AddSingleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        builder.Services.AddSingleton(sp => new RegistroDeModulos(sp.GetServices<IModuloApp>()));
        builder.Services.AddSingleton<MotorSincronizacion>();
        builder.Services.AddSingleton<DisparadorDeSincronizacion>();

        // Cada escuadrón registra su módulo en su propia carpeta.
        builder.AddModuloIdentity();
        builder.AddModuloFleet();
        builder.AddModuloCargo();
        builder.AddModuloBilling();

        builder.Services.AddSingleton<InicioViewModel>();
        builder.Services.AddTransient<InicioPage>();

        var app = builder.Build();
        app.Services.GetRequiredService<DisparadorDeSincronizacion>().Iniciar();
        return app;
    }
}
