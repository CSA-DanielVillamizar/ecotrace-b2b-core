using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.CargoTracking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<CargoTrackingDbContext>("CargoTracking", "cargotracking.db");

var app = builder.Build();
app.ApplyMigrations<CargoTrackingDbContext>();
app.UseApiDefaults<CargoTrackingDbContext>("CargoTracking");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class CargoTrackingApiMarker;
