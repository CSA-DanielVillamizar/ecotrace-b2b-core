using EcoTrace.Identity.Api.Extensions;
using EcoTrace.Identity.Api.Seguridad;
using EcoTrace.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<IdentityDbContext>("Identity", "identity.db");
builder.AddSeguridadIdentity();

var app = builder.Build();
app.ApplyMigrations<IdentityDbContext>();
app.SembrarOperador();
app.UseApiDefaults<IdentityDbContext>("Identity");

// Después de UseApiDefaults para que el rechazo de un token ya lleve CorrelationId en el registro.
// El pipeline de endpoints siempre corre al final, así que la autorización aplica a todas las rutas.
app.UseSeguridadIdentity();
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class IdentityApiMarker;
