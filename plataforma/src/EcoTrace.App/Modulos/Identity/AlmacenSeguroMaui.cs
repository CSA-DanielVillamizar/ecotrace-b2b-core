using EcoTrace.App.Modulos.Identity.Logica;

namespace EcoTrace.App.Modulos.Identity;

/// <summary>
/// El almacén seguro en el teléfono: <c>SecureStorage</c>, que en Android cifra con una clave del Keystore. Es el único
/// lugar donde viven los tokens (ADR 0005): nunca en SQLite ni en <c>Preferences</c>.
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
            // Si Android no puede descifrar (por ejemplo, tras restaurar una copia de seguridad), se trata como si
            // no hubiera sesión: el conductor vuelve a iniciar sesión, nunca se usa un dato a medias.
            SecureStorage.Default.Remove(clave);
            return null;
        }
    }

    public Task GuardarAsync(string clave, string valor) => SecureStorage.Default.SetAsync(clave, valor);

    public Task BorrarAsync(string clave)
    {
        SecureStorage.Default.Remove(clave);
        return Task.CompletedTask;
    }
}
