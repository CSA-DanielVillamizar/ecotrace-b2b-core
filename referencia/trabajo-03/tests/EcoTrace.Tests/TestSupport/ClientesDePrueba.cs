namespace EcoTrace.Tests.TestSupport;

/// <summary>Clientes HTTP de una sola API que llevan el token de un actor, con la audiencia del módulo al que apuntan.</summary>
public static class ClientesDePrueba
{
    public static string AudienciaDe<TMarker>() where TMarker : class
    {
        if (typeof(TMarker) == typeof(IdentityApiMarker))
        {
            return SeguridadDePrueba.AudIdentity;
        }

        if (typeof(TMarker) == typeof(FleetManagementApiMarker))
        {
            return SeguridadDePrueba.AudFleet;
        }

        if (typeof(TMarker) == typeof(CargoTrackingApiMarker))
        {
            return SeguridadDePrueba.AudCargo;
        }

        if (typeof(TMarker) == typeof(BillingApiMarker))
        {
            return SeguridadDePrueba.AudBilling;
        }

        throw new ArgumentException($"No conozco la audiencia de {typeof(TMarker).Name}.");
    }

    /// <summary>Un cliente cuyas llamadas las hace este actor.</summary>
    public static HttpClient Como<TMarker>(this ApiFactory<TMarker> fabrica, Actor actor) where TMarker : class
    {
        var token = actor.TokenPara(AudienciaDe<TMarker>());
        return fabrica.CreateClient().ConToken(string.IsNullOrEmpty(token) ? null : token);
    }

    /// <summary>Un cliente del operador de la plataforma: crea organizaciones y lee todo, pero no escribe datos de otras organizaciones.</summary>
    public static HttpClient ComoOperador<TMarker>(this ApiFactory<TMarker> fabrica) where TMarker : class =>
        fabrica.Como(Actor.Operador);
}

/// <summary>Dos organizaciones de prueba, una de cada lado, con una persona de cada rol que las pruebas necesitan.</summary>
public sealed record ParDeOrganizaciones(Guid GeneradorId, Guid TransportistaId)
{
    public Persona AdminGenerador { get; } = new(GeneradorId, Rol.Administrador);

    public Persona SupervisorGenerador { get; } = new(GeneradorId, Rol.Supervisor);

    public Persona AdminTransportista { get; } = new(TransportistaId, Rol.Administrador);

    public Persona SupervisorTransportista { get; } = new(TransportistaId, Rol.Supervisor);

    public Persona ConductorTransportista { get; } = new(TransportistaId, Rol.Conductor);

    public static ParDeOrganizaciones Nuevo() => new(Guid.NewGuid(), Guid.NewGuid());
}
