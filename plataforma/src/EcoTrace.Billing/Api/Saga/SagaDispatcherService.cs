namespace EcoTrace.Billing.Api.Saga;

/// <summary>
/// Llama al ejecutor cada cierto tiempo mientras el servicio está en marcha. Se puede apagar con
/// Saga:EjecutorAutomatico=false, que es lo que hacen las pruebas para avanzar el Saga a mano y
/// observar el estado después de cada ciclo.
/// </summary>
public sealed class SagaDispatcherService(
    SagaEjecutor ejecutor, IConfiguration configuracion, ILogger<SagaDispatcherService> registro)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken detener)
    {
        if (!configuracion.GetValue("Saga:EjecutorAutomatico", true))
        {
            return;
        }

        var intervalo = TimeSpan.FromSeconds(configuracion.GetValue("Saga:IntervaloSegundos", 1.0));
        using var temporizador = new PeriodicTimer(intervalo);

        try
        {
            while (await temporizador.WaitForNextTickAsync(detener))
            {
                try
                {
                    await ejecutor.ProcesarPendientesAsync(detener);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Un fallo de un ciclo no debe detener el ejecutor: el siguiente lo reintenta.
                    registro.LogError(ex, "Falló un ciclo del ejecutor de Sagas");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // El servicio se está deteniendo.
        }
    }
}
