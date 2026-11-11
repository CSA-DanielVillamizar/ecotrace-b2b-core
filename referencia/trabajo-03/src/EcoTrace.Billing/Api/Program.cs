using EcoTrace.Billing.Api.Extensions;
using EcoTrace.Billing.Api.Security;
using EcoTrace.Billing.Api.Saga;
using EcoTrace.Billing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<BillingDbContext>("Billing", "billing.db");
builder.AddSeguridad(Audiencias.Billing);
builder.AddTokenDeServicio();

// Clientes hacia Identity y Fleet Management, los servicios que el Saga necesita. Las direcciones salen
// de la configuracion (localhost en local, el nombre del servicio en Docker). El tiempo maximo por
// intento lo controla ServiciosExternos con su propio cancelador, por eso el del cliente queda abierto.
// Cada llamada lleva un token de servicio (ADR 0004): sin identidad, Identity y Fleet no responden.
foreach (var (nombre, puertoLocal, audiencia) in new[]
{
    (ServiciosExternos.Identity, 5101, Audiencias.Identity),
    (ServiciosExternos.Fleet, 5102, Audiencias.Fleet)
})
{
    builder.Services.AddHttpClient(nombre, (proveedor, cliente) =>
    {
        var configuracion = proveedor.GetRequiredService<IConfiguration>();
        cliente.BaseAddress = new Uri(configuracion[$"Servicios:{nombre}:BaseUrl"] ?? $"http://localhost:{puertoLocal}");
        cliente.Timeout = Timeout.InfiniteTimeSpan;
    }).ConTokenDeServicio(audiencia);
}

builder.Services.AddSingleton<ServiciosExternos>();
builder.Services.AddSingleton<SagaEjecutor>();
builder.Services.AddHostedService<SagaDispatcherService>();

var app = builder.Build();
app.ApplyMigrations<BillingDbContext>();
app.UseApiDefaults<BillingDbContext>("Billing");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class BillingApiMarker;
