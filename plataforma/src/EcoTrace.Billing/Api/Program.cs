using EcoTrace.Billing.Api.Extensions;
using EcoTrace.Billing.Api.Saga;
using EcoTrace.Billing.Api.Seguridad;
using EcoTrace.Billing.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiDefaults<BillingDbContext>("Billing", "billing.db");

// Configuración base de autenticación JWT Bearer.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        var identityBaseUrl = builder.Configuration["Servicios:Identity:BaseUrl"]
            ?? "http://localhost:5101";
        var jwksUrl = $"{identityBaseUrl.TrimEnd('/')}/.well-known/jwks.json";

        opciones.MapInboundClaims = false;
        opciones.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            jwksUrl,
            new JwksBillingRetriever(),
            new HttpDocumentRetriever { RequireHttps = false });

        opciones.TokenValidationParameters.ValidateIssuerSigningKey = true;
        opciones.TokenValidationParameters.ValidateLifetime = true;
        opciones.TokenValidationParameters.ValidateIssuer = true;
        opciones.TokenValidationParameters.ValidIssuer = builder.Configuration["Jwt:Emisor"];
        opciones.TokenValidationParameters.ValidateAudience = true;
        opciones.TokenValidationParameters.ValidAudience = builder.Configuration["Jwt:Audiencia"];

        opciones.TokenValidationParameters.NameClaimType = "sub";
        opciones.TokenValidationParameters.RoleClaimType = "role";

        opciones.Events = new JwtBearerEvents
        {
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.Headers.WWWAuthenticate = "Bearer";
                return ResponderProblema(ctx.HttpContext, 401, "No autenticado", "Se requiere un token válido.");
            },
            OnForbidden = ctx =>
                ResponderProblema(ctx.HttpContext, 403, "Sin permiso", "El token no tiene permisos suficientes.")
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Administrador", p => p.RequireRole("Administrador").RequireClaim("tenant_id"))
    .AddPolicy("CrearPago", p => p.RequireRole("Administrador", "Servicio"))
    .AddPolicy("Lectura", p => p.RequireAuthenticatedUser().RequireClaim("tenant_id"));

// Clientes hacia Identity y Fleet Management, los servicios que el Saga necesita. Las direcciones salen
// de la configuracion (localhost en local, el nombre del servicio en Docker). El tiempo maximo por
// intento lo controla ServiciosExternos con su propio cancelador, por eso el del cliente queda abierto.
foreach (var (nombre, puertoLocal) in new[] { (ServiciosExternos.Identity, 5101), (ServiciosExternos.Fleet, 5102) })
{
    builder.Services.AddHttpClient(nombre, (proveedor, cliente) =>
    {
        var configuracion = proveedor.GetRequiredService<IConfiguration>();
        cliente.BaseAddress = new Uri(configuracion[$"Servicios:{nombre}:BaseUrl"] ?? $"http://localhost:{puertoLocal}");
        cliente.Timeout = Timeout.InfiniteTimeSpan;
    });
}

builder.Services.AddSingleton<ServiciosExternos>();
builder.Services.AddSingleton<SagaEjecutor>();
builder.Services.AddHostedService<SagaDispatcherService>();

var app = builder.Build();
app.ApplyMigrations<BillingDbContext>();
app.UseApiDefaults<BillingDbContext>("Billing");
app.UseAuthentication();
app.UseAuthorization();
app.Run();

static Task ResponderProblema(HttpContext ctx, int status, string titulo, string detalle)
{
    ctx.Response.StatusCode = status;
    return ctx.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = titulo, Detail = detalle });
}

/// <summary>Punto de anclaje para las pruebas de integracion (WebApplicationFactory).</summary>
public sealed class BillingApiMarker;
