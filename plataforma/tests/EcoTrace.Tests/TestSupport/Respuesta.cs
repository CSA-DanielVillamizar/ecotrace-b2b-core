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

    public static async Task<Respuesta> De(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        var json = string.IsNullOrWhiteSpace(texto) ? default : JsonSerializer.Deserialize<JsonElement>(texto);
        return new Respuesta(respuesta.StatusCode, json);
    }
}

public static class HttpClientExtensions
{
    public static async Task<Respuesta> PostearAsync(this HttpClient cliente, string ruta, object? cuerpo = null)
    {
        var respuesta = cuerpo is null
            ? await cliente.PostAsync(ruta, content: null)
            : await cliente.PostAsJsonAsync(ruta, cuerpo);
        return await Respuesta.De(respuesta);
    }

    public static async Task<Respuesta> ConsultarAsync(this HttpClient cliente, string ruta) =>
        await Respuesta.De(await cliente.GetAsync(ruta));
}
