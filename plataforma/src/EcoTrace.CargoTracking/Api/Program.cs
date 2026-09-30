using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<CargoTrackingDbContext>("CargoTracking", "cargotracking.db");
builder.Services.AddDbContextFactory<CargoTrackingDbContext>();

builder.Services.AddHttpClient();
builder.Services.AddHostedService<OutboxPublisher>();

var app = builder.Build();
app.ApplyMigrations<CargoTrackingDbContext>();
app.UseApiDefaults<CargoTrackingDbContext>("CargoTracking");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class CargoTrackingApiMarker;
