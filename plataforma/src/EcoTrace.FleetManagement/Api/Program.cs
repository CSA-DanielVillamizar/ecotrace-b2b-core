using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<FleetManagementDbContext>("FleetManagement", "fleet.db");

var app = builder.Build();
app.ApplyMigrations<FleetManagementDbContext>();
app.UseApiDefaults<FleetManagementDbContext>("FleetManagement");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class FleetManagementApiMarker;
