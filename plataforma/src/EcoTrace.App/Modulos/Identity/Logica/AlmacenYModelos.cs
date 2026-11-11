using System.Text.Json;

namespace EcoTrace.App.Modulos.Identity.Logica;

/// <summary>
/// Donde el módulo guarda la sesión y el perfil en caché. En el teléfono es <c>SecureStorage</c> (Keystore de
/// Android); en las pruebas, un diccionario. Los tokens <b>nunca</b> van a SQLite ni a <c>Preferences</c> (ADR 0005).
/// </summary>
public interface IAlmacenSeguro
{
    Task<string?> LeerAsync(string clave);

    Task GuardarAsync(string clave, string valor);

    Task BorrarAsync(string clave);
}

/// <summary>Las claves del almacén seguro que usa el módulo. Están en un solo lugar para poder borrarlas todas.</summary>
public static class ClavesDelAlmacen
{
    public const string Sesion = "ecotrace.identity.sesion";
    public const string Perfil = "ecotrace.identity.perfil";

    /// <summary>
    /// De qué cuenta son las acciones de la cola. A propósito no está en <see cref="Todas"/>: sobrevive al cierre de
    /// sesión, para que las acciones de una cuenta nunca se envíen con el token de otra.
    /// </summary>
    public const string DuenoDeLaCola = "ecotrace.identity.dueno-cola";

    /// <summary>Lo que se borra al cerrar sesión.</summary>
    public static readonly string[] Todas = [Sesion, Perfil];

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<T?> LeerAsync<T>(this IAlmacenSeguro almacen, string clave)
        where T : class
    {
        var texto = await almacen.LeerAsync(clave);
        if (string.IsNullOrEmpty(texto))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(texto, Json);
        }
        catch (JsonException)
        {
            // Un dato dañado (por ejemplo de una versión anterior de la app) se trata como si no existiera.
            return null;
        }
    }

    internal static Task GuardarAsync<T>(this IAlmacenSeguro almacen, string clave, T valor) =>
        almacen.GuardarAsync(clave, JsonSerializer.Serialize(valor, Json));
}

/// <summary>
/// La sesión guardada en el teléfono. Tiene los dos tokens, así que solo vive en el almacén seguro.
/// <c>UltimoContactoUtc</c> es la última vez que Identity respondió de verdad (login o renovación).
/// </summary>
public sealed record DatosDeSesion(
    string AccessToken,
    string RefreshToken,
    DateTime AccesoExpiraEnUtc,
    string Email,
    DateTime UltimoLoginUtc,
    DateTime UltimoContactoUtc);

/// <summary>El perfil tal como lo devolvió Identity, guardado para mostrarlo sin señal.</summary>
public sealed record PerfilGuardado(
    Guid UserId,
    Guid TenantId,
    string Nombre,
    string Email,
    string? Telefono,
    string Role,
    string Organizacion,
    string EstadoOrganizacion,
    DateTime ObtenidoEnUtc);

/// <summary>La cuenta dueña de la cola desde <c>DesdeUtc</c>. Las acciones anteriores son de otra cuenta.</summary>
public sealed record DuenoDeLaCola(string Email, DateTime DesdeUtc);

/// <summary>Los tokens que entrega Identity en un login o una renovación.</summary>
public sealed record TokensEmitidos(string AccessToken, string RefreshToken, DateTime ExpiraEnUtc);

public enum EstadoAutenticacion
{
    Exito = 1,

    /// <summary>401: correo o contraseña incorrectos, o refresh token vencido o revocado.</summary>
    Rechazada = 2,

    /// <summary>403: la organización del usuario está suspendida.</summary>
    OrganizacionSuspendida = 3,

    /// <summary>El teléfono no tiene señal. El login nunca se guarda para después.</summary>
    SinConexion = 4,

    /// <summary>Hubo señal pero Identity no respondió bien (caído, 5xx, tiempo agotado).</summary>
    SinRespuesta = 5,

    /// <summary>
    /// El teléfono tiene acciones sin enviar de otra cuenta. No se deja entrar a una cuenta distinta hasta que la
    /// dueña las envíe: si no, se mandarían con el token de quien no las hizo.
    /// </summary>
    AccionesDeOtraCuenta = 6
}

public sealed record ResultadoAutenticacion(EstadoAutenticacion Estado, TokensEmitidos? Tokens, string Mensaje)
{
    public bool Exitosa => Estado == EstadoAutenticacion.Exito;
}
