using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;

namespace EcoTrace.Mobile.Core.Sincronizacion;

/// <summary>
/// Envía la cola al servidor, <b>en el orden en que se encoló</b>, con el OperationId como Idempotency-Key.
/// <list type="bullet">
/// <item>Éxito (201, o 200 de una repetida): Sincronizado.</item>
/// <item>Falla de red, 5xx, 408, 429 o 401: la acción vuelve a PendienteSync y la sincronización se detiene, para no
/// mandar la siguiente antes que la anterior.</item>
/// <item>Rechazo del servidor (4xx): Rechazado, con el tipo de conflicto y el motivo. Se sigue con la siguiente.</item>
/// </list>
/// Sin token vigente no se envía nada: una acción nunca se autoriza con un token vencido.
/// </summary>
public sealed class MotorSincronizacion
{
    private readonly IColaLocal _cola;
    private readonly RegistroDeModulos _modulos;
    private readonly IProveedorDeSesion _sesion;
    private readonly IConectividad _conectividad;
    private readonly HttpClient _http;
    private readonly TimeProvider _reloj;
    private readonly SemaphoreSlim _enCurso = new(1, 1);

    public MotorSincronizacion(
        IColaLocal cola, RegistroDeModulos modulos, IProveedorDeSesion sesion,
        IConectividad conectividad, HttpClient http, TimeProvider? reloj = null)
    {
        _cola = cola;
        _modulos = modulos;
        _sesion = sesion;
        _conectividad = conectividad;
        _http = http;
        _reloj = reloj ?? TimeProvider.System;
    }

    /// <summary>Tiempo máximo de cada envío. Pasado ese tiempo es una falla transitoria.</summary>
    public TimeSpan TiempoMaximoPorEnvio { get; init; } = TimeSpan.FromSeconds(15);

    public async Task<ResultadoSincronizacion> SincronizarAsync(CancellationToken ct = default)
    {
        if (!await _enCurso.WaitAsync(0, CancellationToken.None))
        {
            return await ResultadoAsync(0, 0, ParadaSincronizacion.YaEnCurso);
        }

        try
        {
            // Con el candado tomado nadie más está enviando, así que cualquier "Sincronizando" que quede
            // es de una ejecución anterior que se cortó. Reenviarla es seguro: el servidor deduplica.
            await _cola.RecuperarInterrumpidasAsync(CancellationToken.None);

            if (!_conectividad.HayConexion)
            {
                return await ResultadoAsync(0, 0, ParadaSincronizacion.SinConexion);
            }

            var token = await _sesion.ObtenerTokenVigenteAsync(ct);
            if (string.IsNullOrEmpty(token))
            {
                return await ResultadoAsync(0, 0, ParadaSincronizacion.SinSesion);
            }

            return await EnviarColaAsync(token, ct);
        }
        finally
        {
            _enCurso.Release();
        }
    }

    private async Task<ResultadoSincronizacion> EnviarColaAsync(string token, CancellationToken ct)
    {
        int sincronizadas = 0, rechazadas = 0;

        foreach (var operacion in await _cola.ListarAsync(EstadoOperacion.PendienteSync, ct: CancellationToken.None))
        {
            if (ct.IsCancellationRequested)
            {
                return await ResultadoAsync(sincronizadas, rechazadas, ParadaSincronizacion.Cancelada);
            }

            await _cola.MarcarSincronizandoAsync(operacion.OperationId, CancellationToken.None);

            var manejador = _modulos.Manejador(operacion.Modulo, operacion.Tipo);
            if (manejador is null)
            {
                await _cola.RechazarAsync(operacion.OperationId, null,
                    $"La app no sabe enviar '{operacion.Tipo}' del módulo '{operacion.Modulo}'.", CancellationToken.None);
                rechazadas++;
                continue;
            }

            ResultadoEnvio resultado;
            try
            {
                resultado = await EnviarAsync(manejador, operacion, token, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await _cola.DevolverAPendienteAsync(operacion.OperationId, "Sincronización cancelada.", CancellationToken.None);
                return await ResultadoAsync(sincronizadas, rechazadas, ParadaSincronizacion.Cancelada);
            }
            catch (HttpRequestException)
            {
                // Sin red o sin respuesta a tiempo: no se perdió nada, queda para el próximo intento.
                await _cola.DevolverAPendienteAsync(operacion.OperationId, "Sin respuesta del servidor.", CancellationToken.None);
                return await ResultadoAsync(sincronizadas, rechazadas, ParadaSincronizacion.ErrorTransitorio);
            }

            switch (resultado.Tipo)
            {
                case TipoResultado.Exito:
                    await _cola.MarcarSincronizadaAsync(operacion.OperationId, CancellationToken.None);
                    sincronizadas++;
                    break;

                case TipoResultado.Rechazo:
                    await _cola.RechazarAsync(operacion.OperationId, resultado.Conflicto, resultado.Motivo, CancellationToken.None);
                    rechazadas++;
                    break;

                case TipoResultado.NoAutorizado:
                    await _cola.DevolverAPendienteAsync(operacion.OperationId, resultado.Motivo, CancellationToken.None);
                    await _sesion.NotificarTokenRechazadoAsync(CancellationToken.None);
                    return await ResultadoAsync(sincronizadas, rechazadas, ParadaSincronizacion.TokenRechazado);

                default:
                    await _cola.DevolverAPendienteAsync(operacion.OperationId, resultado.Motivo, CancellationToken.None);
                    return await ResultadoAsync(sincronizadas, rechazadas, ParadaSincronizacion.ErrorTransitorio);
            }
        }

        return await ResultadoAsync(sincronizadas, rechazadas, ParadaSincronizacion.Ninguna);
    }

    private async Task<ResultadoEnvio> EnviarAsync(
        IManejadorOperacion manejador, OperacionPendiente operacion, string token, CancellationToken ct)
    {
        using var solicitud = manejador.Construir(operacion);
        solicitud.Headers.Authorization = new("Bearer", token);

        // Los encabezados del contrato los pone el motor, no el módulo: no hay forma de olvidarlos.
        solicitud.Headers.Remove("Idempotency-Key");
        solicitud.Headers.Add("Idempotency-Key", operacion.OperationId.ToString("D"));
        solicitud.Headers.Remove("X-Correlation-Id");
        solicitud.Headers.Add("X-Correlation-Id", operacion.OperationId.ToString("N"));

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoMaximoPorEnvio);

        try
        {
            using var respuesta = await _http.SendAsync(solicitud, limite.Token);
            var cuerpo = respuesta.IsSuccessStatusCode ? null : await respuesta.Content.ReadAsStringAsync(limite.Token);

            // Cualquier respuesta del servidor es contacto real, aunque sea un rechazo.
            await _cola.RegistrarSincronizacionAsync(_reloj.GetUtcNow().UtcDateTime, CancellationToken.None);
            return ClasificadorDeRespuesta.Clasificar(respuesta.StatusCode, cuerpo);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("Se agotó el tiempo de espera.");
        }
    }

    private async Task<ResultadoSincronizacion> ResultadoAsync(int sincronizadas, int rechazadas, ParadaSincronizacion parada) =>
        new(sincronizadas, rechazadas, await _cola.ContarAsync(EstadoOperacion.PendienteSync), parada);
}
