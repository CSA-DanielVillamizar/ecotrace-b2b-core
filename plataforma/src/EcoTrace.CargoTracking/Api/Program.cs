using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.CargoTracking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<CargoTrackingDbContext>("CargoTracking", "cargotracking.db");

// ============================================================
// Trabajo 2: cliente HTTP hacia Billing y el publicador del Outbox
// ============================================================
// AddHttpClient("Billing", ...) crea, a través de IHttpClientFactory, un
// HttpClient reutilizable configurado con la dirección de Billing. Se usa
// IHttpClientFactory (en vez de "new HttpClient()" directo) porque crear y
// desechar HttpClient manualmente puede agotar los sockets de red
// disponibles con el tiempo -- un problema conocido de .NET que esta
// fábrica administra por nosotros.
//
// La dirección sale de configuración (appsettings.json, Servicios:
// BillingBaseUrl), no del código: cuando el escuadrón de Billing confirme
// su puerto real, solo hay que cambiar un valor de configuración, sin
// tocar ni recompilar nada.
builder.Services.AddHttpClient("Billing", client =>
{
    var baseUrl = builder.Configuration["Servicios:BillingBaseUrl"] ?? "http://localhost:5101";
    client.BaseAddress = new Uri(baseUrl);
});

// AddHostedService le dice a .NET: arranca OutboxPublicador automáticamente
// cuando la aplicación inicie, y mantenlo corriendo en paralelo hasta que
// se apague. Así, el publicador no depende de que nadie lo llame por
// HTTP -- empieza a revisar mensajes Pendiente desde el primer segundo.
builder.Services.AddHostedService<OutboxPublicador>();

var app = builder.Build();
app.ApplyMigrations<CargoTrackingDbContext>();
app.UseApiDefaults<CargoTrackingDbContext>("CargoTracking");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class CargoTrackingApiMarker;
