using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using EcoTrace.Billing.Api.Security;
using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;

namespace EcoTrace.Billing.Api.Extensions;

/// <summary>
/// Hace idempotente una ruta (ADR 0005). La app móvil manda <c>Idempotency-Key</c> con el <c>OperationId</c> que nació en el
/// dispositivo; si la misma clave llega otra vez (un reintento tras un corte de red), el servidor responde <b>200 con el
/// resultado anterior</b> y no repite el efecto.
/// <para>
/// Sin el encabezado la ruta funciona como siempre. Con él, el efecto y el registro de la clave se guardan en <b>una sola
/// transacción</b>: o quedan los dos o no queda ninguno, así que no hay una ventana en la que el efecto exista y su clave no.
/// </para>
/// </summary>
public sealed class IdempotenteAttribute : TypeFilterAttribute
{
    public IdempotenteAttribute() : base(typeof(FiltroDeIdempotencia))
    {
    }
}

internal sealed class FiltroDeIdempotencia(
    BillingDbContext db, TimeProvider reloj, IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> opcionesJson, ILogger<FiltroDeIdempotencia> registro) : IAsyncActionFilter
{
    public const string Encabezado = "Idempotency-Key";
    private const int LargoMaximoDeClave = 100;

    // Dos solicitudes con la misma clave a la vez se atienden de a una. Son 64 candados repartidos por clave: este servicio
    // corre en un solo proceso. Con varias instancias, la restricción única (Clave, Alcance) de la base es la que decide.
    private static readonly SemaphoreSlim[] Candados = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate siguiente)
    {
        var http = contexto.HttpContext;
        if (!http.Request.Headers.TryGetValue(Encabezado, out var valores))
        {
            await siguiente();
            return;
        }

        var clave = valores.ToString().Trim();
        if (clave.Length is 0 or > LargoMaximoDeClave || clave.Any(char.IsControl))
        {
            contexto.Result = Problema(StatusCodes.Status400BadRequest, "Idempotency-Key no válida",
                $"La clave debe tener entre 1 y {LargoMaximoDeClave} caracteres.", "A");
            return;
        }

        // La clave vale para quien la mandó y para esa ruta concreta: la misma clave en otra ruta o de otra persona es otra operación.
        var alcance = $"{http.User.Sub()}|{http.Request.Method} {http.Request.Path}";
        var huella = HuellaDe(contexto.ActionArguments);

        var candado = Candados[(int)((uint)HashCode.Combine(clave, alcance) % Candados.Length)];
        await candado.WaitAsync(http.RequestAborted);
        try
        {
            var previa = await db.OperacionesIdempotentes.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Clave == clave && o.Alcance == alcance, http.RequestAborted);

            if (previa is not null)
            {
                if (previa.Huella != huella)
                {
                    contexto.Result = Problema(StatusCodes.Status422UnprocessableEntity, "Idempotency-Key reutilizada",
                        "Esa clave ya se usó con otra solicitud. Cada acción nueva lleva su propia clave.", "A");
                    return;
                }

                registro.LogInformation("Idempotency-Key repetida en {Metodo} {Ruta}: se devuelve el resultado anterior",
                    http.Request.Method, http.Request.Path);
                http.Response.Headers["Idempotent-Replayed"] = "true";
                contexto.Result = new ContentResult
                {
                    StatusCode = StatusCodes.Status200OK,
                    Content = previa.Respuesta,
                    ContentType = "application/json"
                };
                return;
            }

            await using var transaccion = await db.Database.BeginTransactionAsync(http.RequestAborted);
            var ejecutado = await siguiente();

            // Una excepción (una regla de negocio rechazada, por ejemplo) o una respuesta que no es un éxito no deja huella:
            // la acción no pasó, y reintentarla con la misma clave vuelve a evaluarla.
            if (ejecutado.Exception is not null || ejecutado.Result is not ObjectResult { StatusCode: >= 200 and < 300 } exito)
            {
                return;
            }

            db.OperacionesIdempotentes.Add(OperacionIdempotente.Registrar(
                clave, alcance, huella, exito.StatusCode!.Value,
                JsonSerializer.Serialize(exito.Value, exito.Value?.GetType() ?? typeof(object), opcionesJson.Value.JsonSerializerOptions),
                reloj.GetUtcNow().UtcDateTime));
            await db.SaveChangesAsync(http.RequestAborted);
            await transaccion.CommitAsync(http.RequestAborted);
        }
        finally
        {
            candado.Release();
        }
    }

    private string HuellaDe(IDictionary<string, object?> argumentos)
    {
        var relevantes = argumentos.Where(a => a.Value is not CancellationToken).OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value);
        var texto = JsonSerializer.Serialize(relevantes, opcionesJson.Value.JsonSerializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
    }

    private static ObjectResult Problema(int estado, string titulo, string detalle, string tipoConflicto) =>
        new(new ProblemDetails
        {
            Status = estado,
            Title = titulo,
            Detail = detalle,
            Extensions = { ["tipoConflicto"] = tipoConflicto }
        })
        {
            StatusCode = estado,
            ContentTypes = { "application/problem+json" }
        };
}
