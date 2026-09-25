using System.Collections.Concurrent;

namespace EcoTrace.FleetManagement.Api.Extensions;

/// <summary>
/// Cuenta los fallos que Fleet Management debe simular. Existe para poder demostrar el reintento y
/// la compensación del Saga (ADR 0003) sin apagar el servicio: con los fallos armados, las próximas
/// llamadas responden 503, que es un error transitorio y por eso Billing las reintenta.
/// Es un singleton, así que el contador se pierde al reiniciar el servicio.
/// </summary>
public sealed class SimulacionDeFallos
{
    /// <summary>Única operación que se puede hacer fallar, la que llama Billing en el paso 3.</summary>
    public const string Liberaciones = "liberaciones";

    private readonly ConcurrentDictionary<string, int> _restantes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Arma los próximos <paramref name="cantidad"/> fallos. Con 0 se restablece.</summary>
    public void Armar(string operacion, int cantidad) => _restantes[operacion] = cantidad;

    /// <summary>
    /// Consume un fallo si queda alguno. El descuento es atómico: si dos llamadas llegan a la vez
    /// con un solo fallo armado, una recibe el fallo y la otra se procesa normal.
    /// </summary>
    public bool DebeFallar(string operacion)
    {
        while (true)
        {
            if (!_restantes.TryGetValue(operacion, out var restantes) || restantes <= 0)
            {
                return false;
            }

            // TryUpdate solo escribe si nadie cambio el valor mientras tanto; si perdemos, se relee.
            if (_restantes.TryUpdate(operacion, restantes - 1, restantes))
            {
                return true;
            }
        }
    }

    public IReadOnlyDictionary<string, int> Restantes() => new Dictionary<string, int>(_restantes);
}
