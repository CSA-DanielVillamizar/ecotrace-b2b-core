namespace EcoTrace.FleetManagement.Api.Extensions;

/// <summary>
/// Permite hacer fallar a propósito una operación para ver al orquestador reintentar y compensar.
/// Es una herramienta de demostración: solo se puede activar por HTTP si la configuración
/// Simulacion:Habilitada es true, y por defecto no lo está.
/// </summary>
public sealed class SimulacionFallos
{
    private readonly object _candado = new();
    private readonly Dictionary<string, int> _pendientes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Devuelve true, y descuenta una, si a esta operación le quedan fallos por simular.</summary>
    public bool ConsumirFallo(string operacion)
    {
        lock (_candado)
        {
            if (!_pendientes.TryGetValue(operacion, out var restantes) || restantes <= 0)
            {
                return false;
            }

            _pendientes[operacion] = restantes - 1;
            return true;
        }
    }

    public void Armar(string operacion, int cantidad)
    {
        lock (_candado)
        {
            _pendientes[operacion] = Math.Max(0, cantidad);
        }
    }

    public IReadOnlyDictionary<string, int> Estado()
    {
        lock (_candado)
        {
            return new Dictionary<string, int>(_pendientes, StringComparer.OrdinalIgnoreCase);
        }
    }
}
