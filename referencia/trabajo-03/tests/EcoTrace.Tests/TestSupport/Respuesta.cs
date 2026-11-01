using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EcoTrace.Tests.TestSupport;

/// <summary>Respuesta HTTP ya leida: codigo de estado y cuerpo JSON, con accesos cortos.</summary>
public sealed record Respuesta(HttpStatusCode Estado, JsonElement Json)
{
    public Guid Id(string propiedad) => Json.GetProperty(propiedad).GetGuid();

    public string Texto(string propiedad) => Json.GetProperty(propiedad).GetString()!;

    public JsonElement Propiedad(string propiedad) => Json.GetProperty(propiedad);

    public int Cantidad => Json.GetArrayLength();

    /// <summary>El tipo de conflicto (A, B o C) que el servidor declaró en el ProblemDetails, si lo hizo.</summary>
    public string? TipoConflicto => Json.ValueKind == JsonValueKind.Object && Json.TryGetProperty("tipoConflicto", out var tipo) ? tipo.GetString() : null;

    /// <summary>True si el servidor respondió con el resultado guardado de una Idempotency-Key repetida.</summary>
    public bool Repetida { get; init; }

    public static async Task<Respuesta> De(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(texto) ? default : JsonSerializer.Deserialize<JsonElement>(texto);
        return new Respuesta(respuesta.StatusCode, json) { Repetida = respuesta.Headers.Contains("Idempotent-Replayed") };
    }
}

public static class HttpClientExtensions
{
    /// <summary>Hace que todas las llamadas de este cliente lleven el token. Devuelve el mismo cliente para encadenar.</summary>
    public static HttpClient ConToken(this HttpClient cliente, string? token)
    {
        cliente.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);
        return cliente;
    }

    public static async Task<Respuesta> PostearAsync(this HttpClient cliente, string ruta, object? cuerpo = null)
    {
        var respuesta = cuerpo is null
            ? await cliente.PostAsync(ruta, content: null)
            : await cliente.PostAsJsonAsync(ruta, cuerpo);
        return await Respuesta.De(respuesta);
    }

    /// <summary>Hace una solicitud con cuerpo y con la <c>Idempotency-Key</c> dada, como lo hace la app móvil.</summary>
    public static async Task<Respuesta> EnviarConClaveAsync(
        this HttpClient cliente, HttpMethod metodo, string ruta, object? cuerpo, string? clave)
    {
        using var solicitud = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null)
        {
            solicitud.Content = JsonContent.Create(cuerpo);
        }

        if (clave is not null)
        {
            solicitud.Headers.Add("Idempotency-Key", clave);
        }

        return await Respuesta.De(await cliente.SendAsync(solicitud));
    }

    public static async Task<Respuesta> ConsultarAsync(this HttpClient cliente, string ruta) =>
        await Respuesta.De(await cliente.GetAsync(ruta));
}
