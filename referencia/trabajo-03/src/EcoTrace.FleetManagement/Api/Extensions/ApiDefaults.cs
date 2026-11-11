using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace EcoTrace.FleetManagement.Api.Extensions;

/// <summary>
/// Configuracion transversal de la API. Este archivo se repite igual en los cuatro modulos
/// a proposito: compartirlo como proyecto obligaria a redesplegar todo junto (ver
/// docs/decisiones-de-diseno.md, decision 2).
/// </summary>
public static class ApiDefaults
{
    private const string PoliticaCors = "console";

    /// <summary>Encabezado que enlaza todas las llamadas de una misma operación entre servicios.</summary>
    public const string EncabezadoCorrelacion = "X-Correlation-Id";

    private const string InicioDeSolicitud = "ecotrace.inicio";

    public static WebApplicationBuilder AddApiDefaults<TContext>(
        this WebApplicationBuilder builder, string servicio, string archivoDb)
        where TContext : DbContext
    {
        builder.Host.UseSerilog((_, servicios, configuracion) =>
        {
            configuracion
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
                    "[{Timestamp:HH:mm:ss} {Level:u3}] {Servicio} | {CorrelationId} | {Message:lj}{NewLine}{Exception}");

            // Sumideros adicionales registrados en DI: las pruebas capturan los registros por aquí. En ejecución normal no hay ninguno.
            foreach (var sumidero in servicios.GetServices<Serilog.Core.ILogEventSink>())
            {
                configuracion.WriteTo.Sink(sumidero);
            }
        },
        // No se reemplaza el logger estático (Log.Logger): cada servicio escribe en el suyo. Así dos servicios en el mismo proceso
        // (las pruebas levantan varios) no se pisan los sumideros.
        preserveStaticLogger: true);

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
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            // Un 400 por campos que faltan o vienen mal también es un conflicto de tipo A: se puede corregir y reintentar.
            .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = contexto =>
            {
                var fabrica = contexto.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory>();
                var problema = fabrica.CreateValidationProblemDetails(contexto.HttpContext, contexto.ModelState);
                problema.Title = "Hay datos que no cumplen las reglas.";
                problema.Detail = "Revisa estos campos: " + string.Join(", ", problema.Errors.Keys) + ".";
                problema.Extensions["tipoConflicto"] = "A";
                return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(problema) { ContentTypes = { "application/problem+json" } };
            });

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

    /// <summary>CorrelationId de la solicitud en curso (ver UseApiDefaults).</summary>
    public static string CorrelationId(this HttpContext http) =>
        http.Items[EncabezadoCorrelacion] as string ?? Guid.NewGuid().ToString("N");

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
        // Cada solicitud lleva un CorrelationId: el que envio el llamador o uno nuevo. Se devuelve en la
        // respuesta y se agrega a los registros, para seguir una operacion a traves de los servicios.
        app.Use(async (http, siguiente) =>
        {
            var recibido = http.Request.Headers[EncabezadoCorrelacion].FirstOrDefault()?.Trim();
            var id = !string.IsNullOrEmpty(recibido) && recibido.Length <= 64
                && recibido.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
                ? recibido
                : Guid.NewGuid().ToString("N");

            http.Items[EncabezadoCorrelacion] = id;
            http.Items[InicioDeSolicitud] = DateTime.UtcNow;
            http.Response.Headers[EncabezadoCorrelacion] = id;
            using (LogContext.PushProperty("CorrelationId", id))
            {
                await siguiente();
            }
        });

        // Esquema mínimo de los registros de una solicitud (ADR 0006): CorrelationId (lo agrega el middleware de arriba), EntityId,
        // HttpStatus, OperationName y DurationMs. Nunca se registran cuerpos, encabezados (Authorization), contraseñas, tokens,
        // claves, ni datos de contacto. OperationName es el nombre de la acción (Pagos.RegistrarEvidencia), no la dirección completa.
        // Se escribe con el ILogger del host (no con estado compartido), después de que el resto del pipeline contestó.
        var registroDeSolicitudes = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EcoTrace.Solicitudes");
        app.Use(async (http, siguiente) =>
        {
            var estado = StatusCodes.Status500InternalServerError;
            try
            {
                await siguiente();
                estado = http.Response.StatusCode;
            }
            finally
            {
                var accion = http.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>();
                var inicio = http.Items[InicioDeSolicitud] is DateTime desde ? desde : DateTime.UtcNow;
                registroDeSolicitudes.Log(
                    estado >= 500 ? LogLevel.Error : LogLevel.Information,
                    "{OperationName} -> {HttpStatus} en {DurationMs} ms (entidad {EntityId})",
                    accion is null ? "Sistema" : $"{accion.ControllerName}.{accion.ActionName}",
                    estado,
                    (int)(DateTime.UtcNow - inicio).TotalMilliseconds,
                    http.Request.RouteValues["id"]?.ToString() ?? "-");
            }
        });
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseCors(PoliticaCors);
        app.UseAuthentication();
        app.UseAuthorization();

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

        // Lo que la verificación de la versión de producción (scripts/verificar-release.ps1) pregunta a cada servicio:
        // en qué entorno corre y qué emisor de tokens acepta. No lleva secretos: el emisor también viaja dentro de cada token.
        app.MapGet("/api/_meta/entorno", (HttpContext http) =>
            {
                var configuracion = http.RequestServices.GetRequiredService<IConfiguration>();
                var entorno = http.RequestServices.GetRequiredService<IHostEnvironment>();
                return new
                {
                    servicio,
                    entorno = entorno.IsProduction() ? "produccion" : "desarrollo",
                    version = typeof(ApiDefaults).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                    emisor = configuracion["Jwt:Issuer"]
                };
            })
            .WithName("Entorno")
            .WithSummary("Entorno en el que corre el servicio, su versión y el emisor de tokens que reconoce.");

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
