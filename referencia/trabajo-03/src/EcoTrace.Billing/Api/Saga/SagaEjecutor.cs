using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Saga;

/// <summary>
/// Orquestador del Saga "Liberar Pago en Escrow" (ADR 0003). Ejecuta un paso por vez y guarda el
/// resultado antes de seguir, así que un reinicio a mitad de camino continúa desde el último paso
/// guardado. Cada llamada a otro servicio es idempotente, y los pasos que cambian solo a Billing se
/// guardan en la misma transacción que la marca de "paso completado". Por eso repetir un paso, ya
/// sea por un reintento o por un reinicio, nunca duplica su efecto.
/// </summary>
public sealed class SagaEjecutor(
    IServiceScopeFactory alcances,
    ServiciosExternos servicios,
    TimeProvider reloj,
    IConfiguration configuracion,
    ILogger<SagaEjecutor> registro)
{
    private const int LoteMaximo = 20;

    /// <summary>Avanza los Sagas activos cuya hora de reintento ya llegó. Devuelve cuántos tocó.</summary>
    public async Task<int> ProcesarPendientesAsync(CancellationToken ct = default)
    {
        List<Guid> ids;
        await using (var alcance = alcances.CreateAsyncScope())
        {
            var db = alcance.ServiceProvider.GetRequiredService<BillingDbContext>();
            var ahora = reloj.GetUtcNow().UtcDateTime;
            ids = await db.Sagas
                .Where(s => (s.Estado == EstadoSaga.EnCurso || s.Estado == EstadoSaga.Compensando) && s.ProximoIntentoEn <= ahora)
                .OrderBy(s => s.CreadoEn)
                .Select(s => s.SagaId)
                .Take(LoteMaximo)
                .ToListAsync(ct);
        }

        foreach (var id in ids)
        {
            await AvanzarAsync(id, ct);
        }

        return ids.Count;
    }

    /// <summary>Avanza un Saga hasta que termina, o hasta que un paso queda agendado para más tarde.</summary>
    public async Task AvanzarAsync(Guid sagaId, CancellationToken ct = default)
    {
        await using var alcance = alcances.CreateAsyncScope();
        var db = alcance.ServiceProvider.GetRequiredService<BillingDbContext>();

        var saga = await db.Sagas.Include(s => s.Pasos).FirstOrDefaultAsync(s => s.SagaId == sagaId, ct);
        if (saga is null || saga.EsTerminal)
        {
            return;
        }

        var pago = await db.Pagos.Include(p => p.Auditoria).FirstAsync(p => p.PagoId == saga.PagoId, ct);

        using var _ = registro.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = saga.CorrelationId,
            ["SagaId"] = saga.SagaId
        });

        try
        {
            while (!saga.EsTerminal && saga.ProximoIntentoEn <= reloj.GetUtcNow().UtcDateTime)
            {
                var versionAntes = saga.Version;
                if (saga.Estado == EstadoSaga.Compensando)
                {
                    await CompensarSiguienteAsync(saga, pago, ct);
                }
                else
                {
                    await EjecutarSiguienteAsync(saga, pago, ct);
                }

                await db.SaveChangesAsync(ct);

                // Salvaguarda: si un ciclo no cambio nada, seguir girando solo gastaria CPU.
                if (saga.Version == versionAntes)
                {
                    break;
                }
            }

            if (saga.EsTerminal)
            {
                registro.LogInformation("Saga {SagaId} terminó como {Estado}", saga.SagaId, saga.Estado);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // Otro ejecutor (u otra solicitud sobre el mismo pago) avanzó el Saga primero. No es un fallo:
            // el siguiente ciclo lo vuelve a leer desde la base y continúa desde donde quedó.
            registro.LogInformation("Saga {SagaId}: otro ejecutor lo avanzó primero; se reintenta en el próximo ciclo", saga.SagaId);
        }
    }

    // ---- Camino hacia adelante ---------------------------------------------------------------

    private async Task EjecutarSiguienteAsync(SagaLiberacionPago saga, Pago pago, CancellationToken ct)
    {
        var paso = saga.PasoPendiente;
        if (paso is null)
        {
            return;
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        switch (paso.Nombre)
        {
            case PasosDeLiberacion.AutorizarPago:
                await AutorizarPagoAsync(saga, pago, paso, ahora, ct);
                break;
            case PasosDeLiberacion.LiberarFondos:
                await LiberarFondosAsync(saga, pago, paso, ahora, ct);
                break;
            case PasosDeLiberacion.LiberarRecursos:
                await LiberarRecursosAsync(saga, pago, paso, ahora, ct);
                break;
            case PasosDeLiberacion.RegistrarAuditoria:
                pago.RegistrarEvento("Saga de liberación completada", ahora);
                saga.CompletarPaso(paso, "Traza del Saga registrada", ahora);
                break;
            default:
                throw new InvalidOperationException($"Paso desconocido: {paso.Nombre}");
        }
    }

    // Paso 1. Identity decide si el transportista puede cobrar. Si no responde, el Saga falla cerrado
    // (ADR 0002): sin la autorizacion no se mueve dinero, y como todavia no se hizo nada no hay que deshacer.
    private async Task AutorizarPagoAsync(SagaLiberacionPago saga, Pago pago, SagaPaso paso, DateTime ahora, CancellationToken ct)
    {
        var respuesta = await servicios.AutorizarPagoAsync(pago.PagoId, pago.TransportistaTenantId, saga.CorrelationId, ct);
        if (!respuesta.Exito)
        {
            saga.FallarPaso(paso, respuesta.Detalle, ahora);
            registro.LogWarning("Saga {SagaId}: falló {Paso}: {Motivo}", saga.SagaId, paso.Nombre, respuesta.Detalle);
            return;
        }

        pago.RegistrarEvento("Autorización de Identity obtenida", ahora);
        saga.CompletarPaso(paso, "Autorizado por Identity", ahora);
    }

    // Paso 2. Billing verifica en Identity que la autorizacion sigue vigente y libera los fondos. La
    // liberacion y la marca de "paso completado" van en el mismo guardado: o quedan las dos o ninguna.
    private async Task LiberarFondosAsync(SagaLiberacionPago saga, Pago pago, SagaPaso paso, DateTime ahora, CancellationToken ct)
    {
        var verificacion = await servicios.ObtenerAutorizacionAsync(pago.PagoId, saga.CorrelationId, ct);
        if (!verificacion.Exito)
        {
            saga.FallarPaso(paso, $"No se pudo verificar la autorización. {verificacion.Detalle}", ahora);
            return;
        }

        if (verificacion.Texto("estado") != "Autorizado")
        {
            saga.FallarPaso(paso, "Identity revocó la autorización antes de liberar los fondos.", ahora);
            return;
        }

        try
        {
            pago.Liberar(ahora);
        }
        catch (DomainException conflicto)
        {
            saga.FallarPaso(paso, conflicto.Message, ahora);
            return;
        }

        saga.CompletarPaso(paso, "Fondos liberados al transportista", ahora);
    }

    // Paso 3. Fleet libera vehiculo y conductor. Es el paso que puede fallar por una caida del servicio,
    // asi que tiene reintentos con espera creciente (2 s, 4 s, 8 s). Agotados, el Saga compensa.
    private async Task LiberarRecursosAsync(SagaLiberacionPago saga, Pago pago, SagaPaso paso, DateTime ahora, CancellationToken ct)
    {
        var respuesta = await servicios.LiberarRecursosAsync(saga.CargaId, saga.VehiculoId, saga.ConductorId, saga.CorrelationId, ct);
        if (respuesta.Exito)
        {
            pago.RegistrarEvento("Vehículo y conductor liberados en Fleet", ahora);
            saga.CompletarPaso(paso, $"Recursos liberados (intento {paso.Intentos + 1})", ahora);
            return;
        }

        if (!respuesta.Permanente && paso.Intentos + 1 < MaximoIntentos())
        {
            var proximo = ahora + Espera(paso.Intentos);
            saga.ProgramarReintento(paso, respuesta.Detalle, proximo, ahora);
            registro.LogWarning(
                "Saga {SagaId}: {Paso} falló (intento {Intento} de {Maximo}); reintento a las {Proximo:HH:mm:ss}: {Motivo}",
                saga.SagaId, paso.Nombre, paso.Intentos, MaximoIntentos(), proximo, respuesta.Detalle);
            return;
        }

        saga.FallarPaso(paso, $"No se pudieron liberar los recursos: {respuesta.Detalle}", ahora);
        registro.LogWarning("Saga {SagaId}: {Paso} agotó sus intentos; se inicia la compensación", saga.SagaId, paso.Nombre);
    }

    // ---- Compensación (en orden inverso a como se completaron los pasos) --------------------

    private async Task CompensarSiguienteAsync(SagaLiberacionPago saga, Pago pago, CancellationToken ct)
    {
        var paso = saga.PasoPorCompensar;
        if (paso is null)
        {
            return;
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        switch (paso.Nombre)
        {
            case PasosDeLiberacion.LiberarFondos:
                // Los fondos ya salieron: se revierten a una retención en disputa y queda la nota en la auditoría.
                if (pago.EstadoEscrow == EstadoEscrow.Liberado)
                {
                    pago.RevertirLiberacion(ahora);
                }

                saga.MarcarCompensado(paso, "Liberación revertida: fondos en disputa", ahora);
                registro.LogWarning("Saga {SagaId}: compensada la liberación de fondos del pago {PagoId}", saga.SagaId, pago.PagoId);
                break;

            case PasosDeLiberacion.AutorizarPago:
                await RevocarAutorizacionAsync(saga, pago, paso, ahora, ct);
                break;

            default:
                throw new InvalidOperationException($"El paso {paso.Nombre} no tiene compensación.");
        }
    }

    private async Task RevocarAutorizacionAsync(SagaLiberacionPago saga, Pago pago, SagaPaso paso, DateTime ahora, CancellationToken ct)
    {
        var respuesta = await servicios.RevocarAutorizacionAsync(pago.PagoId, saga.CorrelationId, ct);

        // Un 404 significa que Identity no tiene autorizacion para este pago: no hay nada que revocar.
        if (respuesta.Exito || respuesta.Estado == StatusCodes.Status404NotFound)
        {
            pago.RegistrarEvento("Autorización de Identity revocada", ahora);
            saga.MarcarCompensado(paso, "Autorización revocada en Identity", ahora);
            return;
        }

        if (!respuesta.Permanente && paso.IntentosCompensacion + 1 < MaximoIntentos())
        {
            saga.ProgramarReintentoCompensacion(paso, respuesta.Detalle, ahora + Espera(paso.IntentosCompensacion), ahora);
            registro.LogWarning(
                "Saga {SagaId}: no se pudo revocar la autorización (intento {Intento}); se reintenta: {Motivo}",
                saga.SagaId, paso.IntentosCompensacion, respuesta.Detalle);
            return;
        }

        saga.RequerirIntervencion(paso, respuesta.Detalle, ahora);
        registro.LogError(
            "ALERTA Saga {SagaId}: la compensación de {Paso} no pudo completarse y necesita intervención manual: {Motivo}",
            saga.SagaId, paso.Nombre, respuesta.Detalle);
    }

    // ---- Política de reintentos --------------------------------------------------------------

    /// <summary>Intentos totales por paso: el primero más tres reintentos, como pide el ADR 0002.</summary>
    private int MaximoIntentos() => configuracion.GetValue("Saga:MaximoIntentos", 4);

    /// <summary>Espera antes del siguiente intento: base, 2·base, 4·base… (2 s, 4 s y 8 s con la base por defecto).</summary>
    private TimeSpan Espera(int intentosFallidos) =>
        TimeSpan.FromSeconds(configuracion.GetValue("Saga:EsperaBaseSegundos", 2.0)) * Math.Pow(2, intentosFallidos);
}
