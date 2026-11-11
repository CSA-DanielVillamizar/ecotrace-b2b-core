using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Un cliente HTTP que llama a Cargo &amp; Tracking como <b>la persona correcta para cada ruta</b>, para que las pruebas que
/// no tratan de seguridad no tengan que armar tokens a mano:
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
    private static readonly Regex RutaDeCarga = new(
        @"^/api/cargas/(?<id>[0-9a-fA-F\-]{36})(?<recurso>/[a-z]+)?$", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<Guid, Guid> _transportistaDe = new();

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

    private static Guid? Organizacion(JsonElement cuerpo, string propiedad) =>
        cuerpo.ValueKind == JsonValueKind.Object
        && cuerpo.TryGetProperty(propiedad, out var valor)
        && valor.ValueKind == JsonValueKind.String
        && Guid.TryParse(valor.GetString(), out var id)
            ? id
            : null;

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
