using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.Billing.Api.Extensions;
using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Services;

public sealed class SagaOrchestrator(
    BillingDbContext db,
    IHttpClientFactory httpClientFactory,
    TimeProvider reloj,
    ILogger<SagaOrchestrator> logger)
{
    private static readonly TimeSpan[] ReintentosIdentity = [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)];
    private static readonly TimeSpan[] ReintentosFleet = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];
    private static readonly TimeSpan[] ReintentosRevocacionPaso = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    public async Task ProcesarAsync(Guid sagaId, CancellationToken ct)
    {
        try
        {
            var saga = await db.Sagas.Include(s => s.Pasos)
                .FirstOrDefaultAsync(s => s.SagaId == sagaId, ct);
            if (saga is null)
            {
                return;
            }

            if (!saga.IntentarTomarControl(Guid.NewGuid().ToString("N"), Ahora()))
            {
                return;
            }

            await db.SaveChangesAsync(ct);

            while (!ct.IsCancellationRequested)
            {
                if (saga.Estado == EstadoSaga.EnCurso)
                {
                    var paso = saga.Pasos.OrderBy(p => p.Orden)
                        .FirstOrDefault(p => p.Estado == EstadoPasoSaga.Pendiente);
                    if (paso is null)
                    {
                        saga.ActualizarEstado(EstadoSaga.Completada, null, Ahora());
                        await db.SaveChangesAsync(ct);
                        return;
                    }

                    await EjecutarPasoAsync(saga, paso.Orden, ct);
                    continue;
                }

                if (saga.Estado == EstadoSaga.Compensando)
                {
                    await EjecutarCompensacionAsync(saga, ct);
                    continue;
                }

                return;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogDebug("A concurrent Saga executor persisted progress first; this executor is stopping.");
        }
    }

    private async Task EjecutarPasoAsync(SagaLiberacionPago saga, int orden, CancellationToken ct)
    {
        switch (orden)
        {
            case 1:
                await AutorizarPagoAsync(saga, ct);
                break;
            case 2:
                await LiberarFondosAsync(saga, ct);
                break;
            case 3:
                await LiberarRecursosAsync(saga, ct);
                break;
            case 4:
                await RegistrarAuditoriaAsync(saga, ct);
                break;
            default:
                await FallarSagaAsync(saga, orden, $"El Saga contiene un paso no reconocido: {orden}.", ct);
                break;
        }
    }

    private async Task AutorizarPagoAsync(SagaLiberacionPago saga, CancellationToken ct)
    {
        var resultado = await EjecutarLlamadasAsync(
            saga,
            orden: 1,
            compensacion: false,
            cliente: "Identity",
            crearSolicitud: () => new HttpRequestMessage(HttpMethod.Post, "/api/autorizaciones-pago")
            {
                Content = JsonContent.Create(new
                {
                    pagoId = saga.PagoId,
                    tenantId = saga.TransportistaTenantId
                })
            },
            maxIntentos: 3,
            demoras: ReintentosIdentity,
            ct);

        if (!resultado.Exitoso)
        {
            await FallarSagaAsync(saga, 1, $"No se pudo autorizar el pago: {resultado.Detalle}", ct);
            return;
        }

        var pago = await CargarPagoAsync(saga.PagoId, ct);
        pago.RegistrarAccionSaga("Pago autorizado en Identity", Ahora());
        saga.CompletarPaso(1, "Autorización creada o ya existente en Identity", Ahora());
        await db.SaveChangesAsync(ct);
    }

    private async Task LiberarFondosAsync(SagaLiberacionPago saga, CancellationToken ct)
    {
        var resultado = await EjecutarLlamadasAsync(
            saga,
            orden: 2,
            compensacion: false,
            cliente: "Identity",
            crearSolicitud: () => new HttpRequestMessage(HttpMethod.Get, $"/api/autorizaciones-pago/{saga.PagoId}"),
            maxIntentos: 3,
            demoras: ReintentosIdentity,
            ct);

        if (!resultado.Exitoso)
        {
            await FallarSagaAsync(saga, 2, $"No se pudo verificar la autorización: {resultado.Detalle}", ct);
            return;
        }

        if (!AutorizacionValida(resultado.Contenido, saga.TransportistaTenantId, out var detalle))
        {
            await FallarSagaAsync(saga, 2, detalle, ct);
            return;
        }

        var pago = await CargarPagoAsync(saga.PagoId, ct);
        try
        {
            pago.RegistrarAccionSaga("Autorización verificada en Identity", Ahora());
            pago.Liberar(Ahora());
            saga.CompletarPaso(2, "Autorización vigente verificada y fondos liberados", Ahora());
            await db.SaveChangesAsync(ct);
        }
        catch (DomainException ex)
        {
            await FallarSagaAsync(saga, 2, $"No se pudieron liberar los fondos: {ex.Message}", ct);
        }
    }

    private async Task LiberarRecursosAsync(SagaLiberacionPago saga, CancellationToken ct)
    {
        var resultado = await EjecutarLlamadasAsync(
            saga,
            orden: 3,
            compensacion: false,
            cliente: "FleetManagement",
            crearSolicitud: () => new HttpRequestMessage(HttpMethod.Post, "/api/liberaciones")
            {
                Content = JsonContent.Create(new
                {
                    cargaId = saga.CargaId,
                    vehiculoId = saga.VehiculoId,
                    conductorId = saga.ConductorId
                })
            },
            maxIntentos: 4,
            demoras: ReintentosFleet,
            ct);

        if (!resultado.Exitoso)
        {
            await FallarSagaAsync(saga, 3, $"No se pudieron liberar los recursos: {resultado.Detalle}", ct);
            return;
        }

        var pago = await CargarPagoAsync(saga.PagoId, ct);
        pago.RegistrarAccionSaga("Vehículo y conductor liberados en Fleet", Ahora());
        saga.CompletarPaso(3, "Recursos liberados en Fleet", Ahora());
        await db.SaveChangesAsync(ct);
    }

    private async Task RegistrarAuditoriaAsync(SagaLiberacionPago saga, CancellationToken ct)
    {
        var pago = await CargarPagoAsync(saga.PagoId, ct);
        pago.RegistrarAccionSaga("Saga de liberación completado", Ahora());
        saga.CompletarPaso(4, "Auditoría del Saga registrada", Ahora());
        saga.ActualizarEstado(EstadoSaga.Completada, null, Ahora());
        await db.SaveChangesAsync(ct);
    }

    private async Task EjecutarCompensacionAsync(SagaLiberacionPago saga, CancellationToken ct)
    {
        var paso = saga.Pasos
            .Where(p => p.Compensable && p.Estado == EstadoPasoSaga.Completado)
            .OrderByDescending(p => p.Orden)
            .FirstOrDefault();

        if (paso is null)
        {
            saga.ActualizarEstado(EstadoSaga.Compensada, saga.Motivo, Ahora());
            await db.SaveChangesAsync(ct);
            return;
        }

        if (paso.Orden == 2)
        {
            saga.RegistrarIntentoCompensacion(2, Ahora());
            await db.SaveChangesAsync(ct);

            var pago = await CargarPagoAsync(saga.PagoId, ct);
            pago.CompensarLiberacionSaga(Ahora());
            saga.MarcarPasoCompensado(2, "Pago movido a EnDisputa", Ahora());
            await db.SaveChangesAsync(ct);
            return;
        }

        if (paso.Orden == 1)
        {
            await RevocarAutorizacionAsync(saga, ct);
            return;
        }

        saga.ActualizarEstado(EstadoSaga.RequiereIntervencion,
            $"No se reconoce la compensación del paso {paso.Orden}.", Ahora());
        logger.LogError("ALERTA Saga {SagaId}: no se reconoce la compensación del paso {Paso}.", saga.SagaId, paso.Orden);
        await db.SaveChangesAsync(ct);
    }

    private async Task RevocarAutorizacionAsync(SagaLiberacionPago saga, CancellationToken ct)
    {
        ResultadoLlamada resultado = new(false, true, "No se recibió respuesta de Identity.", null);
        for (var intentoPaso = 0; intentoPaso < 4; intentoPaso++)
        {
            resultado = await EjecutarLlamadasAsync(
                saga,
                orden: 1,
                compensacion: true,
                cliente: "Identity",
                crearSolicitud: () => new HttpRequestMessage(
                    HttpMethod.Post, $"/api/autorizaciones-pago/{saga.PagoId}/revocacion"),
                maxIntentos: 3,
                demoras: ReintentosIdentity,
                aceptar404: true,
                ct: ct);

            if (resultado.Exitoso || !resultado.Transitorio)
            {
                break;
            }

            if (intentoPaso < ReintentosRevocacionPaso.Length)
            {
                await Task.Delay(ReintentosRevocacionPaso[intentoPaso], ct);
            }
        }

        if (!resultado.Exitoso)
        {
            var motivo = $"No se pudo revocar la autorización en Identity: {resultado.Detalle}";
            saga.ActualizarDetallePaso(1, motivo, Ahora());
            saga.ActualizarEstado(EstadoSaga.RequiereIntervencion, motivo, Ahora());
            logger.LogError("ALERTA Saga {SagaId}: {Motivo}", saga.SagaId, motivo);
            await db.SaveChangesAsync(ct);
            return;
        }

        var pago = await CargarPagoAsync(saga.PagoId, ct);
        pago.RegistrarAccionSaga("Autorización revocada en Identity", Ahora());
        saga.MarcarPasoCompensado(1, "Autorización revocada en Identity", Ahora());
        await db.SaveChangesAsync(ct);
    }

    private async Task<ResultadoLlamada> EjecutarLlamadasAsync(
        SagaLiberacionPago saga,
        int orden,
        bool compensacion,
        string cliente,
        Func<HttpRequestMessage> crearSolicitud,
        int maxIntentos,
        IReadOnlyList<TimeSpan> demoras,
        CancellationToken ct,
        bool aceptar404 = false)
    {
        var http = httpClientFactory.CreateClient(cliente);
        ResultadoLlamada resultado = new(false, true, "No se recibió respuesta.", null);

        for (var intento = 0; intento < maxIntentos; intento++)
        {
            if (compensacion)
            {
                saga.RegistrarIntentoCompensacion(orden, Ahora());
            }
            else
            {
                saga.RegistrarIntento(orden, Ahora());
            }

            await db.SaveChangesAsync(ct);

            using var solicitud = crearSolicitud();
            solicitud.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, saga.CorrelationId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(cliente == "FleetManagement" ? TimeSpan.FromSeconds(3) : TimeSpan.FromMilliseconds(500));

            try
            {
                using var respuesta = await http.SendAsync(solicitud, timeout.Token);
                var contenido = await respuesta.Content.ReadAsStringAsync(timeout.Token);
                var detalle = DetalleRespuesta(respuesta.StatusCode, contenido);
                var exitoso = respuesta.IsSuccessStatusCode || (aceptar404 && respuesta.StatusCode == HttpStatusCode.NotFound);
                resultado = new(exitoso, EsTransitorio(respuesta.StatusCode), detalle, contenido);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                resultado = new(false, true, "Tiempo agotado esperando respuesta del servicio.", null);
            }
            catch (HttpRequestException ex)
            {
                resultado = new(false, true, $"Error de red: {ex.Message}", null);
            }

            if (resultado.Exitoso || !resultado.Transitorio)
            {
                if (!resultado.Exitoso)
                {
                    saga.ActualizarDetallePaso(orden, resultado.Detalle, Ahora());
                    await db.SaveChangesAsync(ct);
                }

                return resultado;
            }

            saga.ActualizarDetallePaso(orden, resultado.Detalle, Ahora());
            await db.SaveChangesAsync(ct);
            if (intento < demoras.Count)
            {
                await Task.Delay(demoras[intento], ct);
            }
        }

        return resultado;
    }

    private async Task FallarSagaAsync(SagaLiberacionPago saga, int orden, string motivo, CancellationToken ct)
    {
        saga.FallarPaso(orden, motivo, Ahora());
        var tieneCompensacion = saga.Pasos.Any(p => p.Compensable && p.Estado == EstadoPasoSaga.Completado);
        saga.ActualizarEstado(tieneCompensacion ? EstadoSaga.Compensando : EstadoSaga.Fallida, motivo, Ahora());
        await db.SaveChangesAsync(ct);
    }

    private async Task<Pago> CargarPagoAsync(Guid pagoId, CancellationToken ct) =>
        await db.Pagos.Include(p => p.Auditoria).FirstOrDefaultAsync(p => p.PagoId == pagoId, ct)
        ?? throw DomainException.NotFound("No existe el pago asociado al Saga.");

    private static bool AutorizacionValida(string? contenido, Guid tenantId, out string detalle)
    {
        try
        {
            using var json = JsonDocument.Parse(contenido ?? string.Empty);
            var raiz = json.RootElement;
            var estado = raiz.GetProperty("estado").GetString();
            var tenant = raiz.GetProperty("tenantId").GetGuid();
            if (estado == "Autorizado" && tenant == tenantId)
            {
                detalle = string.Empty;
                return true;
            }

            detalle = tenant != tenantId
                ? "La autorización pertenece a un tenant diferente al transportista del pago."
                : $"La autorización no está Autorizado: {estado ?? "sin estado"}.";
            return false;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            detalle = "Identity devolvió una autorización con formato inválido.";
            return false;
        }
    }

    private static bool EsTransitorio(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or (HttpStatusCode)429;

    private static string DetalleRespuesta(HttpStatusCode status, string contenido)
    {
        var detalle = string.IsNullOrWhiteSpace(contenido) ? string.Empty : $": {contenido.Trim()}";
        if (detalle.Length > 400)
        {
            detalle = detalle[..400];
        }

        return $"HTTP {(int)status}{detalle}";
    }

    private DateTime Ahora() => reloj.GetUtcNow().UtcDateTime;

    private sealed record ResultadoLlamada(bool Exitoso, bool Transitorio, string Detalle, string? Contenido);
}