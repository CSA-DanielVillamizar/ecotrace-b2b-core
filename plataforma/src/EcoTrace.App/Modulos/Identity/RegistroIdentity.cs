using EcoTrace.App.Modulos.Identity.Logica;
using EcoTrace.Mobile.Core.Cola;
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

        builder.Services.AddSingleton<IAlmacenSeguro, AlmacenSeguroMaui>();

        // Cliente propio con tiempo de espera: el login no debe quedarse colgado si Identity no responde.
        builder.Services.AddSingleton(sp => new ClienteIdentity(
            new HttpClient { Timeout = TimeSpan.FromSeconds(15) },
            sp.GetRequiredService<ConfiguracionServicios>().Identity));

        // La sesión es el IProveedorDeSesion del motor: de aquí sale el token con el que sincronizan los cuatro módulos.
        builder.Services.AddSingleton(sp => new GestorDeSesion(
            sp.GetRequiredService<IAlmacenSeguro>(),
            sp.GetRequiredService<ClienteIdentity>(),
            sp.GetRequiredService<IConectividad>(),
            sp.GetRequiredService<IColaLocal>()));
        builder.Services.AddSingleton<IProveedorDeSesion>(sp => sp.GetRequiredService<GestorDeSesion>());

        builder.Services.AddSingleton(sp => new ServicioDePerfil(
            sp.GetRequiredService<GestorDeSesion>(),
            sp.GetRequiredService<ClienteIdentity>(),
            sp.GetRequiredService<IColaLocal>(),
            sp.GetRequiredService<IAlmacenSeguro>()));

        builder.Services.AddSingleton<PerfilViewModel>();
        builder.Services.AddTransient<IdentityPage>();
        return builder;
    }
}
