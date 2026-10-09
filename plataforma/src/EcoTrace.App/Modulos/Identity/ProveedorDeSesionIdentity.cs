using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Identity;

/// <summary>
/// De dónde sale el token para sincronizar. El esqueleto devuelve null (no hay sesión), así que el motor no envía nada
/// y las acciones esperan en la cola. El escuadrón de Identity lo reemplaza: lee el access token de <c>SecureStorage</c>,
/// lo renueva con el refresh token si venció y devuelve null si no puede. Nunca devuelve un token vencido.
/// </summary>
public sealed class ProveedorDeSesionIdentity : IProveedorDeSesion
{
    public Task<string?> ObtenerTokenVigenteAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);

    public Task NotificarTokenRechazadoAsync(CancellationToken ct = default) => Task.CompletedTask;
}
