using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<FleetManagementDbContext>("FleetManagement", "fleet.db");

// Contador de fallos simulados: uno solo para todo el servicio (Trabajo 2, demo de la compensacion).
builder.Services.AddSingleton<SimulacionDeFallos>();

var app = builder.Build();
app.ApplyMigrations<FleetManagementDbContext>();

// Va antes de UseApiDefaults para que el registro de cada solicitud ya lleve el CorrelationId.
app.UseCorrelacion();
app.UseApiDefaults<FleetManagementDbContext>("FleetManagement");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class FleetManagementApiMarker;
