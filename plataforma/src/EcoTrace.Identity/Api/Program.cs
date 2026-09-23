using EcoTrace.Identity.Api.Extensions;
using EcoTrace.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<IdentityDbContext>("Identity", "identity.db");

var app = builder.Build();
app.ApplyMigrations<IdentityDbContext>();
app.UseApiDefaults<IdentityDbContext>("Identity");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class IdentityApiMarker;
