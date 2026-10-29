using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Api.Seguridad;
using EcoTrace.FleetManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<FleetManagementDbContext>("FleetManagement", "fleet.db");
builder.AddSeguridadFleet();
builder.Services.AddSingleton<SimulacionFallos>();

var app = builder.Build();
app.ApplyMigrations<FleetManagementDbContext>();
app.SembrarVehiculoConocido();
app.UseApiDefaults<FleetManagementDbContext>("FleetManagement");

// Después de UseApiDefaults para que el rechazo de un token ya lleve CorrelationId en el registro.
// Los endpoints siempre corren al final del pipeline, así que la autorización aplica a todas las rutas.
app.UseSeguridadFleet();
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class FleetManagementApiMarker;
