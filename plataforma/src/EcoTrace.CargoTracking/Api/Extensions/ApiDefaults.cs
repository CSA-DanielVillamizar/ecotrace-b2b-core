using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

namespace EcoTrace.CargoTracking.Api.Extensions;

/// <summary>
/// Configuracion transversal de la API. Este archivo se repite igual en los cuatro modulos
/// a proposito: compartirlo como proyecto obligaria a redesplegar todo junto (ver
/// docs/decisiones-de-diseno.md, decision 2).
/// </summary>
public static class ApiDefaults
{
    private const string PoliticaCors = "console";

    public static WebApplicationBuilder AddApiDefaults<TContext>(
        this WebApplicationBuilder builder, string servicio, string archivoDb)
        where TContext : DbContext
    {
        builder.Host.UseSerilog((_, configuracion) => configuracion
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            // Las violaciones de unicidad y de concurrencia se traducen a 409 en GuardarAsync:
            // son respuestas esperadas, no fallos. Un error real de base de datos sigue
            // apareciendo como excepcion no controlada en el middleware de ASP.NET.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Update", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogEventLevel.Fatal)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Servicio", servicio)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Servicio} | {Message:lj}{NewLine}{Exception}"));

        // La cadena de conexion se resuelve al primer uso (no al registrar), para que las
        // pruebas de integracion puedan reemplazarla con su propia base temporal.
        builder.Services.AddDbContext<TContext>((proveedor, opciones) =>
        {
            var configuracion = proveedor.GetRequiredService<IConfiguration>();
            var entorno = proveedor.GetRequiredService<IHostEnvironment>();
            opciones.UseSqlite(ResolverCadenaConexion(configuracion, entorno, archivoDb));
        });

        builder.Services
            .AddControllers()
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o =>
            o.SwaggerDoc("v1", new() { Title = $"EcoTrace {servicio}", Version = "v1" }));

        var origenes = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
            ?? ["http://localhost:5100", "http://127.0.0.1:5100"];
        builder.Services.AddCors(o => o.AddPolicy(PoliticaCors, politica =>
            politica.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod()));

        return builder;
    }

    public static WebApplication ApplyMigrations<TContext>(this WebApplication app)
        where TContext : DbContext
    {
        if (!app.Configuration.GetValue("Database:MigrarAlArrancar", true))
        {
            return app;
        }

        using var alcance = app.Services.CreateScope();
        alcance.ServiceProvider.GetRequiredService<TContext>().Database.Migrate();
        return app;
    }

    public static WebApplication UseApiDefaults<TContext>(this WebApplication app, string servicio)
        where TContext : DbContext
    {
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors(PoliticaCors);

        if (app.Configuration.GetValue("Swagger:Habilitado", true))
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapControllers();

        // El DbContext se resuelve desde HttpContext: el analizador de rutas de ASP.NET 8
        // falla con parametros de tipo generico directamente en la lambda.
        app.MapGet("/health", async (HttpContext http) =>
        {
            var db = http.RequestServices.GetRequiredService<TContext>();
            var conectada = await db.Database.CanConnectAsync(http.RequestAborted);
            var cuerpo = new
            {
                servicio,
                estado = conectada ? "ok" : "degradado",
                baseDeDatos = conectada ? "ok" : "sin conexión"
            };

            return conectada
                ? Results.Ok(cuerpo)
                : Results.Json(cuerpo, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).ExcludeFromDescription();

        app.MapGet("/api/_meta/contexto", (HttpContext http) =>
                ContextoMetadata.Describir(http.RequestServices.GetRequiredService<TContext>(), servicio))
            .WithName("Contexto")
            .WithSummary("Describe las entidades del contexto y sus referencias externas por ID.");

        return app;
    }

    private static string ResolverCadenaConexion(IConfiguration configuracion, IHostEnvironment entorno, string archivoDb)
    {
        var explicita = configuracion.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(explicita))
        {
            return explicita;
        }

        var carpeta = Path.Combine(entorno.ContentRootPath, "App_Data");
        Directory.CreateDirectory(carpeta);
        return $"Data Source={Path.Combine(carpeta, archivoDb)}";
    }
}
