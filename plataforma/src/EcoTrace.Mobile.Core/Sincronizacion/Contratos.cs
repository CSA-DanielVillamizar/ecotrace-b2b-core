using EcoTrace.Mobile.Core.Cola;

namespace EcoTrace.Mobile.Core.Sincronizacion;

/// <summary>
/// Cómo se convierte una acción de la cola en una solicitud HTTP. Cada módulo implementa uno por tipo de acción.
/// El motor es quien agrega <c>Authorization</c>, <c>Idempotency-Key</c> y <c>X-Correlation-Id</c>: el módulo
/// solo decide método, ruta y cuerpo, así que no puede olvidarse de la clave de idempotencia.
/// </summary>
public interface IManejadorOperacion
{
    /// <summary>Módulo dueño, igual al <see cref="Modulos.IModuloApp.Nombre"/> que lo registra.</summary>
    string Modulo { get; }

    /// <summary>Tipo de acción que sabe enviar, por ejemplo "ConfirmarEntrega".</summary>
    string Tipo { get; }

    /// <summary>Arma la solicitud con una dirección absoluta (la del servicio sale de la configuración).</summary>
    HttpRequestMessage Construir(OperacionPendiente operacion);
}

/// <summary>
/// De dónde sale el token para sincronizar. La implementa el módulo de Identity sobre SecureStorage.
/// </summary>
public interface IProveedorDeSesion
{
    /// <summary>
    /// El access token vigente, renovado si hizo falta, o null si no hay uno vigente (sin sesión, vencido sin poder
    /// renovar). Con null el motor no envía nada y las acciones siguen en PendienteSync: <b>nunca</b> se autoriza
    /// una acción con un token vencido (ADR 0005).
    /// </summary>
    Task<string?> ObtenerTokenVigenteAsync(CancellationToken ct = default);

    /// <summary>El servidor dijo 401 con el token que se envió. Identity decide si cierra la sesión o la renueva.</summary>
    Task NotificarTokenRechazadoAsync(CancellationToken ct = default);
}

public interface IConectividad
{
    bool HayConexion { get; }

    event EventHandler<bool>? CambioDeConexion;
}

public enum TipoResultado
{
    /// <summary>2xx: aceptada, o repetida y ya aceptada antes (200).</summary>
    Exito = 1,

    /// <summary>401: el token no sirvió. La acción sigue pendiente.</summary>
    NoAutorizado = 2,

    /// <summary>Falla de red, tiempo agotado, 5xx, 408 o 429: se reintenta, la acción sigue pendiente.</summary>
    Transitorio = 3,

    /// <summary>4xx: el servidor la rechazó y repetirla no lo cambia.</summary>
    Rechazo = 4
}

public sealed record ResultadoEnvio(TipoResultado Tipo, TipoConflicto? Conflicto, string Motivo);

public enum ParadaSincronizacion
{
    /// <summary>Se recorrió toda la cola.</summary>
    Ninguna = 0,

    SinConexion = 1,

    /// <summary>No hay token vigente.</summary>
    SinSesion = 2,

    /// <summary>El servidor rechazó el token (401).</summary>
    TokenRechazado = 3,

    /// <summary>Falló la red o el servidor: se detiene para no enviar fuera de orden.</summary>
    ErrorTransitorio = 4,

    /// <summary>Ya había una sincronización en curso.</summary>
    YaEnCurso = 5,

    Cancelada = 6
}

public sealed record ResultadoSincronizacion(
    int Sincronizadas, int Rechazadas, int Pendientes, ParadaSincronizacion Parada);
