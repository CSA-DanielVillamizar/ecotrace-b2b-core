using EcoTrace.Identity.Api.Extensions;
using EcoTrace.Identity.Api.Security;
using EcoTrace.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<IdentityDbContext>("Identity", "identity.db");
builder.AddSeguridad();

var app = builder.Build();
app.ValidarSeguridadAlArrancar();
app.ApplyMigrations<IdentityDbContext>();
await app.SembrarPlataformaAsync();
app.UseApiDefaults<IdentityDbContext>("Identity");
app.Run();

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class IdentityApiMarker;
