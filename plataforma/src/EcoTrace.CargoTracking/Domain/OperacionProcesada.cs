namespace EcoTrace.CargoTracking.Domain;

/// <summary>
/// Una operación de la app del conductor que el servidor ya procesó (bandeja de entrada de operaciones).
/// <para>
/// ¿Para qué sirve? La app guarda cada acción en el teléfono con un <c>OperationId</c> y la envía cuando hay
/// señal. Si la respuesta se pierde (la red se cae justo después de que el servidor ya trabajó), la app no
/// sabe si la acción se hizo, así que la reenvía con el mismo identificador. Sin este registro, el servidor
/// la trataría como una acción nueva. En el mejor caso respondería 409 y la app la marcaría como rechazada
/// aunque sí se hizo; en el peor, repetiría el efecto, por ejemplo disparando dos veces el Saga de pago.
/// </para>
/// <para>
/// Con este registro el servidor reconoce el identificador, no repite nada y devuelve el resultado de la
/// primera vez. Es la misma idea que <c>EventoRecibido</c> en Billing (ADR 0002), aplicada a las acciones
/// que llegan desde el teléfono (ADR 0005).
/// </para>
/// <para>
/// Solo se guardan las operaciones que tuvieron éxito. Si una fue rechazada, no se registra: al reintentarla
/// el servidor vuelve a evaluarla contra el estado actual y da la misma respuesta de rechazo.
/// </para>
/// </summary>
public sealed class OperacionProcesada
{
    private OperacionProcesada()
    {
        Resultado = string.Empty;
    }

    /// <summary>
    /// Identificador de la operación. Es el <c>OperationId</c> que la app genera en el teléfono al guardar la
    /// acción y que manda en el encabezado <c>Idempotency-Key</c>. Es la clave primaria de la tabla, así que
    /// la base de datos no deja que dos filas lo repitan.
    /// </summary>
    public Guid OperacionId { get; private set; }

    /// <summary>
    /// Carga sobre la que se hizo la operación. Es una referencia interna a este mismo contexto (tiene clave
    /// foránea hacia Cargas). Sirve para detectar que la misma clave se usó, por error, en otra carga.
    /// </summary>
    public Guid CargaId { get; private set; }

    /// <summary>
    /// Respuesta que se le dio al cliente la primera vez, guardada como JSON. Si la operación se repite, se
    /// devuelve este mismo texto, así que el cliente recibe el "resultado anterior" que pide el contrato.
    /// </summary>
    public string Resultado { get; private set; }

    /// <summary>Momento en que el servidor procesó la operación por primera vez.</summary>
    public DateTime ProcesadaEn { get; private set; }

    /// <summary>
    /// Anota que la operación ya se procesó. Se llama en la misma transacción que el cambio de negocio, así
    /// que quedan los dos o no queda ninguno: nunca hay un cambio sin su registro ni un registro sin cambio.
    /// </summary>
    public static OperacionProcesada Registrar(Guid operacionId, Guid cargaId, string resultado, DateTime ahoraUtc)
    {
        if (operacionId == Guid.Empty)
        {
            throw DomainException.Validation("La operación necesita un identificador.");
        }

        if (cargaId == Guid.Empty)
        {
            throw DomainException.Validation("La operación necesita la carga sobre la que se hizo.");
        }

        if (string.IsNullOrWhiteSpace(resultado))
        {
            throw DomainException.Validation("La operación necesita guardar su resultado para poder repetirlo.");
        }

        return new OperacionProcesada
        {
            OperacionId = operacionId,
            CargaId = cargaId,
            Resultado = resultado,
            ProcesadaEn = ahoraUtc
        };
    }
}
