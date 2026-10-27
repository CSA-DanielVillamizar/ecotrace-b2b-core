using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;

namespace EcoTrace.App.Modulos.Identity.Logica;

/// <summary>
/// Lo que muestra la pantalla de perfil: el perfil en caché, con el cambio pendiente encima si lo hay, y el último
/// rechazo con su motivo. Se arma solo con datos del teléfono, así que funciona en modo avión.
/// </summary>
public sealed record VistaDePerfil(
    EstadoDeSesion Sesion,
    string? Nombre,
    string? Telefono,
    string? Role,
    string? Organizacion,
    string? EstadoOrganizacion,
    DateTime? UltimaSincronizacionUtc,
    string? Aviso,
    string? Rechazo,
    TipoConflicto? TipoDeRechazo,
    Guid? OperacionRechazada)
{
    /// <summary>Solo un conflicto A se puede corregir y reintentar desde la pantalla.</summary>
    public bool PuedeCorregir => OperacionRechazada is not null && TipoDeRechazo == TipoConflicto.A;
}

/// <summary>
/// El perfil del conductor siguiendo la regla de oro del ADR 0005: cada cambio se guarda primero en la cola,
/// la pantalla lo muestra de inmediato y el motor lo envía cuando hay señal.
/// </summary>
public sealed class ServicioDePerfil
{
    private readonly GestorDeSesion _sesion;
    private readonly ClienteIdentity _cliente;
    private readonly IColaLocal _cola;
    private readonly IAlmacenSeguro _almacen;
    private readonly TimeProvider _reloj;

    public ServicioDePerfil(
        GestorDeSesion sesion, ClienteIdentity cliente, IColaLocal cola, IAlmacenSeguro almacen, TimeProvider? reloj = null)
    {
        _sesion = sesion;
        _cliente = cliente;
        _cola = cola;
        _almacen = almacen;
        _reloj = reloj ?? TimeProvider.System;
    }

    public async Task<VistaDePerfil> ObtenerVistaAsync(CancellationToken ct = default)
    {
        var sesion = await _sesion.EstadoAsync();
        var perfil = sesion.Iniciada ? await _almacen.LeerAsync<PerfilGuardado>(ClavesDelAlmacen.Perfil) : null;
        var dueno = await _almacen.LeerAsync<DuenoDeLaCola>(ClavesDelAlmacen.DuenoDeLaCola);
        var ultimaSincronizacion = await _cola.UltimaSincronizacionAsync(ct);

        // Solo cuentan los cambios de la cuenta actual: los de una cuenta anterior no se muestran a esta.
        var ultimoCambio = dueno is null || dueno.Email != sesion.Email
            ? null
            : (await _cola.ListarAsync(modulo: AccionesDeIdentity.Modulo, ct: ct))
                .Where(o => o.Tipo == AccionesDeIdentity.ActualizarPerfil && o.CreadaEnUtc >= dueno.DesdeUtc)
                .MaxBy(o => o.Secuencia);

        var nombre = perfil?.Nombre;
        var telefono = perfil?.Telefono;
        string? aviso = null;
        string? rechazo = null;
        TipoConflicto? tipo = null;
        Guid? rechazada = null;

        if (ultimoCambio is not null && sesion.Iniciada)
        {
            var datos = Leer(ultimoCambio.Payload);
            switch (ultimoCambio.Estado)
            {
                case EstadoOperacion.PendienteSync:
                case EstadoOperacion.Sincronizando:
                    (nombre, telefono) = (datos?.Nombre ?? nombre, datos is null ? telefono : datos.Telefono);
                    aviso = ultimoCambio.Intentos > 0 && ultimoCambio.MotivoRechazo is { } motivo
                        ? $"Cambio guardado en el teléfono. Aún no se pudo enviar: {motivo}"
                        : "Cambio guardado en el teléfono. Se envía cuando haya señal.";
                    break;

                // Ya llegó al servidor, pero el perfil en caché es de antes: se muestra el cambio hasta refrescar.
                case EstadoOperacion.Sincronizado when perfil is null || (ultimoCambio.ActualizadaEnUtc ?? ultimoCambio.CreadaEnUtc) > perfil.ObtenidoEnUtc:
                    (nombre, telefono) = (datos?.Nombre ?? nombre, datos is null ? telefono : datos.Telefono);
                    aviso = "Cambio enviado.";
                    break;

                case EstadoOperacion.Rechazado:
                    rechazo = ultimoCambio.MotivoRechazo ?? "El servidor rechazó el cambio.";
                    tipo = ultimoCambio.TipoConflicto;
                    rechazada = ultimoCambio.OperationId;
                    break;
            }
        }

        return new VistaDePerfil(
            sesion, nombre, telefono, perfil?.Role, perfil?.Organizacion, perfil?.EstadoOrganizacion,
            ultimaSincronizacion, aviso, rechazo, tipo, rechazada);
    }

    /// <summary>
    /// Guarda el cambio en la cola y vuelve de inmediato. El formato del teléfono lo valida Identity: si no cumple,
    /// el cambio vuelve rechazado con conflicto A y se puede corregir.
    /// </summary>
    public Task<OperacionPendiente> CambiarPerfilAsync(string? nombre, string? telefono, CancellationToken ct = default) =>
        _cola.EncolarAsync(AccionesDeIdentity.Modulo, AccionesDeIdentity.ActualizarPerfil, Payload(nombre, telefono), ct);

    /// <summary>Conflicto A: crea una acción nueva con el dato corregido; la rechazada queda intacta para auditoría.</summary>
    public Task<OperacionPendiente> CorregirAsync(Guid operacionRechazada, string? nombre, string? telefono, CancellationToken ct = default) =>
        _cola.ReintentarCorregidaAsync(operacionRechazada, Payload(nombre, telefono), ct);

    public Task<OperacionPendiente> CerrarOtrasSesionesAsync(CancellationToken ct = default) =>
        _cola.EncolarAsync(AccionesDeIdentity.Modulo, AccionesDeIdentity.CerrarOtrasSesiones, "{}", ct);

    /// <summary>
    /// Trae el perfil de Identity y lo guarda en caché. Devuelve false si no se pudo (sin señal o sin sesión): la
    /// pantalla sigue mostrando lo guardado.
    /// </summary>
    public async Task<bool> RefrescarAsync(CancellationToken ct = default)
    {
        var token = await _sesion.ObtenerTokenVigenteAsync(ct);
        if (token is null)
        {
            return false;
        }

        var ahora = _reloj.GetUtcNow().UtcDateTime;
        var perfil = await _cliente.ObtenerPerfilAsync(token, ahora, ct);
        if (perfil is null)
        {
            return false;
        }

        await _almacen.GuardarAsync(ClavesDelAlmacen.Perfil, perfil);
        await _cola.RegistrarSincronizacionAsync(ahora, ct);
        return true;
    }

    private static string Payload(string? nombre, string? telefono)
    {
        var limpio = (nombre ?? string.Empty).Trim();
        if (limpio.Length == 0)
        {
            throw new ArgumentException("Escribe tu nombre.", nameof(nombre));
        }

        var tel = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();
        return JsonSerializer.Serialize(new DatosDePerfil(limpio, tel), ClavesDelAlmacen.Json);
    }

    private static DatosDePerfil? Leer(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<DatosDePerfil>(payload, ClavesDelAlmacen.Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
