using EcoTrace.Mobile.Core.Cola;

namespace EcoTrace.App.Modulos.Identity.Logica;

/// <summary>
/// La cola local, con el control de la sesión delante (ADR 0005). Antes de guardar una acción de cualquier módulo pregunta a
/// <see cref="GestorDeSesion"/> si la sesión lo permite: sin sesión o con más de 48 horas sin conexión, nada; con la sesión
/// degradada, solo tras desbloquear con huella o PIN y nunca una acción crítica. Lo demás pasa sin cambios.
/// <para>
/// Se registra desde <c>RegistroIdentity</c> en lugar de la cola del esqueleto, así el control vale para los cuatro módulos
/// sin tocar el <c>Mobile.Core</c>, que es compartido. Si no se permite, lanza <see cref="AccionNoPermitidaException"/>
/// (una <see cref="InvalidOperationException"/>) antes de guardar nada.
/// </para>
/// </summary>
public sealed class ColaConSesion(IColaLocal interna, GestorDeSesion sesion) : IColaLocal
{
    public event EventHandler? Cambio
    {
        add => interna.Cambio += value;
        remove => interna.Cambio -= value;
    }

    public async Task<OperacionPendiente> EncolarAsync(string modulo, string tipo, string payloadJson, CancellationToken ct = default)
    {
        await sesion.ExigirPermisoParaEncolarAsync(modulo, tipo, ct);
        return await interna.EncolarAsync(modulo, tipo, payloadJson, ct);
    }

    public async Task<OperacionPendiente> ReintentarCorregidaAsync(Guid operationIdRechazada, string nuevoPayloadJson, CancellationToken ct = default)
    {
        // Corregir un conflicto A crea una acción nueva: pasa por el mismo control.
        if (await interna.ObtenerAsync(operationIdRechazada, ct) is { } rechazada)
        {
            await sesion.ExigirPermisoParaEncolarAsync(rechazada.Modulo, rechazada.Tipo, ct);
        }

        return await interna.ReintentarCorregidaAsync(operationIdRechazada, nuevoPayloadJson, ct);
    }

    public Task<OperacionPendiente?> ObtenerAsync(Guid operationId, CancellationToken ct = default) =>
        interna.ObtenerAsync(operationId, ct);

    public Task<IReadOnlyList<OperacionPendiente>> ListarAsync(
        EstadoOperacion? estado = null, string? modulo = null, CancellationToken ct = default) =>
        interna.ListarAsync(estado, modulo, ct);

    public Task<int> ContarAsync(EstadoOperacion estado, CancellationToken ct = default) => interna.ContarAsync(estado, ct);

    public Task MarcarSincronizandoAsync(Guid operationId, CancellationToken ct = default) =>
        interna.MarcarSincronizandoAsync(operationId, ct);

    public Task MarcarSincronizadaAsync(Guid operationId, CancellationToken ct = default) =>
        interna.MarcarSincronizadaAsync(operationId, ct);

    public Task DevolverAPendienteAsync(Guid operationId, string? motivo, CancellationToken ct = default) =>
        interna.DevolverAPendienteAsync(operationId, motivo, ct);

    public Task RechazarAsync(Guid operationId, TipoConflicto? tipo, string motivo, CancellationToken ct = default) =>
        interna.RechazarAsync(operationId, tipo, motivo, ct);

    public Task<int> RecuperarInterrumpidasAsync(CancellationToken ct = default) => interna.RecuperarInterrumpidasAsync(ct);

    public Task<DateTime?> UltimaSincronizacionAsync(CancellationToken ct = default) => interna.UltimaSincronizacionAsync(ct);

    public Task RegistrarSincronizacionAsync(DateTime utc, CancellationToken ct = default) =>
        interna.RegistrarSincronizacionAsync(utc, ct);
}
