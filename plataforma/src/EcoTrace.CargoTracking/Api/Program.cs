using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.CargoTracking.Api.Outbox;
using EcoTrace.CargoTracking.Api.Security;
using EcoTrace.CargoTracking.Infrastructure;

// PUNTO DE ARRANQUE de Cargo & Tracking. Tiene dos partes: primero se arman los servicios (todo lo que el servicio
// necesita para funcionar) y después se define el ORDEN de los pasos por los que pasa cada solicitud.

// --- Parte 1: armar los servicios ---
var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<CargoTrackingDbContext>("CargoTracking", "cargotracking.db");
builder.AddRegistroSeguro(); // Una línea de registro por solicitud, sin datos sensibles (ADR 0006).

// Seguridad (ADR 0004). Este módulo valida los tokens de Identity por su cuenta (AddSeguridad) y, cuando llama a otro
// módulo, se identifica con un token de servicio (AddTokenDeServicio).
builder.AddSeguridad();
builder.AddTokenDeServicio();

// Cliente hacia Billing, destino del Outbox. La dirección sale de la configuración para que en
// Docker sea el nombre del servicio y en local sea localhost. El tiempo máximo por intento evita
// que un Billing lento deje colgado al publicador. Cada llamada lleva un token de servicio para Billing.
builder.Services.AddHttpClient(OutboxPublicador.ClienteBilling, (proveedor, cliente) =>
{
    var configuracion = proveedor.GetRequiredService<IConfiguration>();
    cliente.BaseAddress = new Uri(configuracion["Servicios:Billing:BaseUrl"] ?? "http://localhost:5104");
    cliente.Timeout = TimeSpan.FromSeconds(configuracion.GetValue("Outbox:TimeoutSegundos", 3.0));
}).ConTokenDeServicio(Audiencias.Billing);
builder.Services.AddSingleton<OutboxPublicador>();
builder.Services.AddHostedService<OutboxDispatcherService>();

// --- Parte 2: el orden de los pasos de cada solicitud ---
var app = builder.Build();
app.ApplyMigrations<CargoTrackingDbContext>();

// Un registro por solicitud con el esquema mínimo del ADR 0006. Va antes que todo lo demás para ver el código final.
app.UseRegistroSeguro();
app.UseApiDefaults<CargoTrackingDbContext>("CargoTracking");

// Quién eres (autenticación) y qué puedes hacer (autorización). Van después de CORS, que UseApiDefaults ya puso.
app.UseAuthentication();
app.UseAuthorization();
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class CargoTrackingApiMarker;
