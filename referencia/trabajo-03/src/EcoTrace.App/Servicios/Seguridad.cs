using EcoTrace.Mobile.Core.Sesion;

namespace EcoTrace.App.Servicios;

/// <summary>
/// Los tokens viven en <c>SecureStorage</c> (en Android, el Keystore). Nunca en SQLite, en <c>Preferences</c> ni en texto plano.
/// </summary>
public sealed class AlmacenSeguroMaui : IAlmacenSeguro
{
    public async Task<string?> LeerAsync(string clave)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(clave);
        }
        catch (Exception)
        {
            // Si el Keystore cambió (por ejemplo, tras restaurar una copia), el dato no se puede leer: se trata como sin sesión.
            SecureStorage.Default.Remove(clave);
            return null;
        }
    }

    public Task GuardarAsync(string clave, string valor) => SecureStorage.Default.SetAsync(clave, valor);

    public void Quitar(string clave) => SecureStorage.Default.Remove(clave);
}
