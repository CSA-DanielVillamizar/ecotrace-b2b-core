namespace EcoTrace.CargoTracking.Api.Outbox;

/// <summary>
/// Ejecuta el publicador cada cierto tiempo mientras el servicio está en marcha. Se puede apagar con
/// Outbox:EjecutorAutomatico=false, que es lo que hacen las pruebas para invocar el publicador a mano
/// y controlar en qué momento ocurre cada intento.
/// </summary>
public sealed class OutboxDispatcherService(
    OutboxPublicador publicador, IConfiguration configuracion, ILogger<OutboxDispatcherService> registro)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        if (!configuracion.GetValue("Outbox:EjecutorAutomatico", true))
        {
            return;
        }

        var intervalo = TimeSpan.FromSeconds(configuracion.GetValue("Outbox:IntervaloSegundos", 1.0));
        using var temporizador = new PeriodicTimer(intervalo);

        try
        {
            while (await temporizador.WaitForNextTickAsync(detener))
            {
                try
                {
                    await publicador.PublicarPendientesAsync(detener);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un fallo de un ciclo no debe detener el publicador: el siguiente lo reintenta.
                    registro.LogError(ex, "Falló un ciclo del publicador del Outbox");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // El servicio se está deteniendo.
        }
    }
}
