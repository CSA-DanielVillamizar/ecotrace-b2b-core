namespace EcoTrace.Mobile.Core.Sincronizacion;

/// <summary>
/// Dispara la sincronización sin que el conductor tenga que pedirla: al arrancar, cuando vuelve la señal y cada
/// cierto tiempo mientras la app esté abierta. El motor evita que dos corran a la vez, así que disparar de más es inofensivo.
/// </summary>
public sealed class DisparadorDeSincronizacion : IDisposable
{
    private readonly MotorSincronizacion _motor;
    private readonly IConectividad _conectividad;
    private readonly CancellationTokenSource _detener = new();

    public DisparadorDeSincronizacion(MotorSincronizacion motor, IConectividad conectividad)
    {
        _motor = motor;
        _conectividad = conectividad;
    }

    /// <summary>Cada cuánto reintenta mientras la app está abierta. Cubre el 5xx transitorio y el token que se renueva.</summary>
    public TimeSpan Intervalo { get; init; } = TimeSpan.FromSeconds(30);

    public void Iniciar()
    {
        _conectividad.CambioDeConexion += AlCambiarLaConexion;
        _ = Task.Run(() => RepetirAsync(_detener.Token));
    }

    public void Dispose()
    {
        _conectividad.CambioDeConexion -= AlCambiarLaConexion;
        _detener.Cancel();
        _detener.Dispose();
    }

    private void AlCambiarLaConexion(object? remitente, bool hayConexion)
    {
        if (hayConexion)
        {
            _ = Task.Run(() => IntentarAsync(_detener.Token));
        }
    }

    private async Task RepetirAsync(CancellationToken ct)
    {
        await IntentarAsync(ct);
        using var reloj = new PeriodicTimer(Intervalo);
        try
        {
            while (await reloj.WaitForNextTickAsync(ct))
            {
                await IntentarAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // La app se cerró.
        }
    }

    private async Task IntentarAsync(CancellationToken ct)
    {
        try
        {
            await _motor.SincronizarAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un fallo inesperado no debe tumbar el hilo de fondo; la acción sigue en la cola y se reintenta.
        }
    }
}
