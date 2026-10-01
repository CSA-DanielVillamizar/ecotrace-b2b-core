namespace EcoTrace.CargoTracking.Domain;

/// <summary>
/// Estados de un mensaje de Outbox, tal como los nombra la especificación del Trabajo 2:
/// "Estados de un mensaje: Pendiente, Publicado, Muerto".
/// </summary>
public enum EstadoMensaje
{
    Pendiente = 0,   // recién creado, esperando que el publicador lo envíe
    Publicado = 1,   // Billing respondió 2xx: ya no hay nada más que hacer
    Muerto = 2       // fallo permanente, o se agotaron los reintentos
}

/// <summary>
/// Evento EntregaConfirmada pendiente de enviar a Billing (patrón Transactional Outbox, ADR
/// 0002). Se crea en la MISMA transacción que el Seguimiento que lo origina, dentro de
/// <see cref="Api.Controllers.CargasController.RegistrarSeguimiento"/> -- así, si Billing está
/// caído en el momento de la entrega, el evento nunca se pierde: queda guardado localmente,
/// esperando a que el publicador (<see cref="Infrastructure.OutboxPublicador"/>) lo entregue.
///
/// Por qué esto resuelve el problema de "Cargo no debe quedar bloqueado esperando a Billing"
/// (ADR 0002): registrar la entrega solo implica guardar esta fila -- una operación local que
/// nunca depende de que otro servicio esté disponible.
///
/// Sigue el mismo estilo inmutable que <see cref="Carga"/>: constructor privado, propiedades
/// de solo lectura, y toda modificación pasa por un método que valida la regla correspondiente
/// (<see cref="MarcarPublicado"/>, <see cref="RegistrarReintento"/>,
/// <see cref="MarcarMuertoPermanente"/>, <see cref="Reprocesar"/>). Así nadie -- ni el
/// publicador, ni el controlador -- puede dejar el mensaje en un estado inconsistente.
/// </summary>
public sealed class MensajeOutbox
{
    /// <summary>Máximo de intentos antes de pasar a Muerto (especificación: espera 2s, 4s, 8s, 16s...).</summary>
    public const int MaxIntentos = 6;

    private MensajeOutbox()
    {
        EventId = string.Empty;
        CorrelationId = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Identificador del MENSAJE en sí -- deliberadamente string, no Guid. La especificación es
    /// explícita: "Los identificadores de mensajes y de trazas son texto, no Guid... Guardarlos
    /// como string es lo correcto: identifican un mensaje o una traza, no una entidad de otro
    /// contexto." Un Guid...Id activaría la prueba de arquitectura que exige [ReferenciaExterna],
    /// y este campo no referencia ninguna entidad ajena -- es la huella de este evento.
    /// </summary>
    public string EventId { get; private set; }

    /// <summary>Viaja igual en las llamadas de todo el Saga, para poder rastrearlo de punta a punta.</summary>
    public string CorrelationId { get; private set; }

    public Guid CargaId { get; private set; }

    // --- Referencias externas: copias planas de los IDs que Billing necesita ---
    // Se marcan con [ReferenciaExterna] igual que en Carga/AsignacionCarga, aunque
    // aquí son una copia (no la fuente original) -- la prueba de arquitectura no
    // distingue el propósito, solo exige que todo Guid...Id que apunte a otro
    // contexto quede declarado.
    [ReferenciaExterna("FleetManagement", "Vehículo que transportó la carga entregada")]
    public Guid VehiculoId { get; private set; }

    [ReferenciaExterna("FleetManagement", "Conductor que transportó la carga entregada")]
    public Guid ConductorId { get; private set; }

    [ReferenciaExterna("Identity", "Organización que generó la carga")]
    public Guid GeneradorTenantId { get; private set; }

    [ReferenciaExterna("Identity", "Organización transportista")]
    public Guid TransportistaTenantId { get; private set; }

    public DateTime OcurrioEn { get; private set; }

    public EstadoMensaje Estado { get; private set; }

    /// <summary>Cuántas veces se ha intentado publicar. Se agota en <see cref="MaxIntentos"/>.</summary>
    public int Intentos { get; private set; }

    /// <summary>Cuándo puede reintentarse la próxima vez -- el publicador solo toma mensajes cuya hora ya llegó.</summary>
    public DateTime ProximoIntentoEn { get; private set; }

    /// <summary>Último error registrado, para poder diagnosticar sin adivinar por qué un mensaje quedó Muerto.</summary>
    public string? UltimoError { get; private set; }

    /// <summary>
    /// Se llama desde <see cref="Api.Controllers.CargasController"/> al confirmar una entrega --
    /// por eso es <c>public</c>, a diferencia de <see cref="AsignacionCarga.Crear"/> y
    /// <see cref="Seguimiento.Crear"/> (esos son <c>internal</c> porque solo los invoca
    /// <see cref="Carga"/>, desde dentro del mismo ensamblado Domain). MensajeOutbox no vive
    /// dentro del agregado Carga, así que la capa Api lo crea directamente, igual que hace con
    /// <see cref="Carga.Crear"/>.
    /// </summary>
    public static MensajeOutbox Crear(
        Guid cargaId, Guid vehiculoId, Guid conductorId,
        Guid generadorTenantId, Guid transportistaTenantId,
        string correlationId, DateTime ahoraUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid().ToString(),
            CorrelationId = correlationId,
            CargaId = cargaId,
            VehiculoId = vehiculoId,
            ConductorId = conductorId,
            GeneradorTenantId = generadorTenantId,
            TransportistaTenantId = transportistaTenantId,
            OcurrioEn = ahoraUtc,
            Estado = EstadoMensaje.Pendiente,
            Intentos = 0,
            ProximoIntentoEn = ahoraUtc,
            UltimoError = null
        };

    /// <summary>El publicador llama esto cuando Billing respondió 2xx: ya no hay nada más que hacer.</summary>
    public void MarcarPublicado()
    {
        Estado = EstadoMensaje.Publicado;
        UltimoError = null;
    }

    /// <summary>
    /// El publicador llama esto ante un fallo TRANSITORIO (5xx, 408, 429, timeout, red caída) --
    /// la especificación distingue estos de los fallos permanentes porque vale la pena reintentar:
    /// el problema puede resolverse solo en el próximo intento.
    ///
    /// Agenda el siguiente intento con la espera creciente que calculó quien llama (el publicador,
    /// que conoce la tabla 2s/4s/8s/16s/32s), o pasa a Muerto si ya se agotaron los
    /// <see cref="MaxIntentos"/> intentos permitidos -- ahí es donde se agrega el prefijo ALERTA
    /// que pide la especificación, para que quede claro que alguien debe revisarlo manualmente.
    /// </summary>
    public void RegistrarReintento(string error, DateTime proximoIntentoEn)
    {
        Intentos++;
        UltimoError = error;

        if (Intentos >= MaxIntentos)
        {
            Estado = EstadoMensaje.Muerto;
            UltimoError = $"ALERTA: se agotaron los {MaxIntentos} intentos. Último error: {error}";
        }
        else
        {
            ProximoIntentoEn = proximoIntentoEn;
        }
    }

    /// <summary>
    /// El publicador llama esto ante un fallo PERMANENTE (4xx, salvo 408/429). La especificación
    /// es explícita: "Los 4xx son permanentes para el publicador de Cargo: repetirlos no los
    /// arregla" -- por ejemplo, si Billing responde 404 porque no existe un pago para esa carga,
    /// reintentar mil veces no va a hacer que ese pago aparezca. Por eso este método NO reintenta,
    /// pasa a Muerto de inmediato.
    /// </summary>
    public void MarcarMuertoPermanente(string motivo)
    {
        Estado = EstadoMensaje.Muerto;
        UltimoError = $"ALERTA: fallo permanente. {motivo}";
    }

    /// <summary>
    /// Devuelve un mensaje Muerto a Pendiente, con los intentos en cero -- la "ruta de reproceso"
    /// que exige la especificación, para usar después de que alguien resolvió la causa raíz
    /// (por ejemplo, Billing estaba caído por mantenimiento y ya volvió).
    ///
    /// La regla "solo se reprocesa lo que está Muerto" vive AQUÍ, en el dominio, no en
    /// <see cref="Api.Controllers.OutboxController"/> -- el mismo principio que ya sigue
    /// <see cref="Carga.RegistrarSeguimiento"/> con sus transiciones: la entidad es quien conoce
    /// y hace cumplir sus propias reglas, la capa Api solo orquesta. Así, nadie puede saltarse
    /// esta validación llamando desde otro lugar del código.
    /// </summary>
    public void Reprocesar(DateTime ahoraUtc)
    {
        if (Estado != EstadoMensaje.Muerto)
        {
            throw DomainException.Conflict("Solo se pueden reprocesar mensajes en estado Muerto.");
        }

        Estado = EstadoMensaje.Pendiente;
        Intentos = 0;
        ProximoIntentoEn = ahoraUtc;
        UltimoError = null;
    }
}
