using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Un cliente HTTP que llama a Cargo &amp; Tracking como <b>la persona correcta para cada ruta</b>, para que las pruebas que
/// no tratan de seguridad no tengan que fabricar tokens a mano:
/// <list type="bullet">
/// <item>Crear una carga: un supervisor de la organización generadora (la que dice el cuerpo).</item>
/// <item>Asignarla o consultarla: un supervisor de la organización transportista de esa carga.</item>
/// <item>Registrar su seguimiento (incluida la entrega): un conductor de esa organización transportista.</item>
/// <item>El Outbox: un servicio interno.</item>
/// </list>
/// Se entera de qué organización transporta cada carga al verla crearse. Si una prueba ya pone su propio token, lo respeta.
/// Las pruebas de seguridad no usan este cliente: arman sus propios tokens para probar justo lo que no deben poder hacer.
/// </summary>
public sealed class ActorAutomatico : DelegatingHandler
{
    /// <summary>Reconoce las rutas de una carga, como <c>/api/cargas/{id}/seguimientos</c>, y separa el identificador y el recurso.</summary>
    private static readonly Regex RutaDeCarga = new(
        @"^/api/cargas/(?<id>[0-9a-fA-F\-]{36})(?<recurso>/[a-z]+)?$", RegexOptions.Compiled);

    /// <summary>Qué organización transporta cada carga que se creó por este cliente.</summary>
    private readonly ConcurrentDictionary<Guid, Guid> _transportistaDe = new();

    /// <summary>Antes de enviar cada llamada le pone el token de la persona correcta, y al ver crearse una carga anota quién la transporta.</summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        var ruta = solicitud.RequestUri?.AbsolutePath ?? string.Empty;
        var esCrear = solicitud.Method == HttpMethod.Post && ruta == "/api/cargas";

        Guid? generador = null;
        Guid? transportista = null;
        if (esCrear && solicitud.Content is not null)
        {
            (generador, transportista) = LeerOrganizaciones(await solicitud.Content.ReadAsStringAsync(ct));
        }

        if (solicitud.Headers.Authorization is null && TokenPara(solicitud, ruta, generador) is { } token)
        {
            solicitud.Headers.Authorization = new("Bearer", token);
        }

        var respuesta = await base.SendAsync(solicitud, ct);

        if (esCrear && respuesta.StatusCode == HttpStatusCode.Created && transportista is { } organizacion)
        {
            using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            if (documento.RootElement.TryGetProperty("cargaId", out var id))
            {
                _transportistaDe[id.GetGuid()] = organizacion;
            }
        }

        return respuesta;
    }

    /// <summary>Decide qué persona (rol y organización) debe llamar a cada ruta y fabrica su token. Devuelve null para las rutas públicas.</summary>
    private string? TokenPara(HttpRequestMessage solicitud, string ruta, Guid? generador)
    {
        if (ruta.StartsWith("/api/outbox", StringComparison.Ordinal))
        {
            return SeguridadDePrueba.Emitir(Rol.Servicio, sub: "prueba");
        }

        if (ruta == "/api/cargas")
        {
            return solicitud.Method == HttpMethod.Post
                ? SeguridadDePrueba.Emitir(Rol.Supervisor, generador ?? Guid.NewGuid())
                : SeguridadDePrueba.Emitir(Rol.Supervisor, TenantDeLaConsulta(solicitud.RequestUri!) ?? Guid.NewGuid());
        }

        var coincidencia = RutaDeCarga.Match(ruta);
        if (!coincidencia.Success)
        {
            return null;
        }

        // Si la carga no se creó por este cliente (o no existe), se usa una organización cualquiera: el servidor
        // responde lo que corresponda, por ejemplo 404 para una carga que no existe.
        var transportista = _transportistaDe.TryGetValue(Guid.Parse(coincidencia.Groups["id"].Value), out var conocida)
            ? conocida
            : Guid.NewGuid();
        var registraSeguimiento = solicitud.Method == HttpMethod.Post && coincidencia.Groups["recurso"].Value == "/seguimientos";
        return SeguridadDePrueba.Emitir(registraSeguimiento ? Rol.Conductor : Rol.Supervisor, transportista);
    }

    /// <summary>Lee del cuerpo de «crear carga» las dos organizaciones. Si el cuerpo está mal, devuelve vacíos.</summary>
    private static (Guid? Generador, Guid? Transportista) LeerOrganizaciones(string cuerpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            return (Organizacion(documento.RootElement, "generadorTenantId"), Organizacion(documento.RootElement, "transportistaTenantId"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>Lee una organización de un campo del cuerpo, o null si falta o no es un identificador.</summary>
    private static Guid? Organizacion(JsonElement cuerpo, string propiedad) =>
        cuerpo.ValueKind == JsonValueKind.Object
        && cuerpo.TryGetProperty(propiedad, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && Guid.TryParse(valor.GetString(), out var id)
            ? id
            : null;

    /// <summary>Lee el filtro <c>tenantId</c> de la dirección, si lo trae.</summary>
    private static Guid? TenantDeLaConsulta(Uri direccion)
    {
        foreach (var par in direccion.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = par.Split('=', 2);
            if (partes.Length == 2 && partes[0] == "tenantId" && Guid.TryParse(partes[1], out var id))
            {
                return id;
            }
        }

        return null;
    }
}
