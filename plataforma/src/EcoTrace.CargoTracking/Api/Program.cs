using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.CargoTracking.Api.Outbox;
using EcoTrace.CargoTracking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<CargoTrackingDbContext>("CargoTracking", "cargotracking.db");

// Cliente hacia Billing, destino del Outbox. La dirección sale de la configuración para que en
// Docker sea el nombre del servicio y en local sea localhost. El tiempo máximo por intento evita
// que un Billing lento deje colgado al publicador.
builder.Services.AddHttpClient(OutboxPublicador.ClienteBilling, (proveedor, cliente) =>
{
    var configuracion = proveedor.GetRequiredService<IConfiguration>();
    cliente.BaseAddress = new Uri(configuracion["Servicios:Billing:BaseUrl"] ?? "http://localhost:5104");
    cliente.Timeout = TimeSpan.FromSeconds(configuracion.GetValue("Outbox:TimeoutSegundos", 3.0));
});
builder.Services.AddSingleton<OutboxPublicador>();
builder.Services.AddHostedService<OutboxDispatcherService>();

var app = builder.Build();
app.ApplyMigrations<CargoTrackingDbContext>();
app.UseApiDefaults<CargoTrackingDbContext>("CargoTracking");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class CargoTrackingApiMarker;
