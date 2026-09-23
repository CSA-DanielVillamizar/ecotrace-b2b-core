using EcoTrace.Billing.Api.Extensions;
using EcoTrace.Billing.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<BillingDbContext>("Billing", "billing.db");

var app = builder.Build();
app.ApplyMigrations<BillingDbContext>();
app.UseApiDefaults<BillingDbContext>("Billing");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class BillingApiMarker;
