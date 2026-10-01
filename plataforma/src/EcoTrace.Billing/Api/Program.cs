using EcoTrace.Billing.Api.Extensions;
using EcoTrace.Billing.Api.Services;
using EcoTrace.Billing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<BillingDbContext>("Billing", "billing.db");
builder.Services.AddScoped<SagaRecepcionService>();
builder.Services.AddScoped<SagaOrchestrator>();
builder.Services.AddHostedService<SagaBackgroundProcessor>();
builder.Services.AddHttpClient("Identity", client =>
{
	client.BaseAddress = new Uri(builder.Configuration.GetValue("Identity:Url", "http://localhost:5101")!);
	client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddHttpClient("FleetManagement", client =>
{
	client.BaseAddress = new Uri(builder.Configuration.GetValue("FleetManagement:Url", "http://localhost:5102")!);
	client.Timeout = Timeout.InfiniteTimeSpan;
});

var app = builder.Build();
app.ApplyMigrations<BillingDbContext>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseApiDefaults<BillingDbContext>("Billing");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class BillingApiMarker;
