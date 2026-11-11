using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Api.Security;
using EcoTrace.FleetManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<FleetManagementDbContext>("FleetManagement", "fleet.db");
builder.AddSeguridad(Audiencias.Fleet);
builder.Services.AddSingleton<SimulacionFallos>();

var app = builder.Build();
app.ApplyMigrations<FleetManagementDbContext>();
app.UseApiDefaults<FleetManagementDbContext>("FleetManagement");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class FleetManagementApiMarker;
