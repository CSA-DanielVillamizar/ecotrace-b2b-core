namespace EcoTrace.CargoTracking.Domain;

/// <summary>
/// Un apunte que dice: «esta operación del teléfono ya la procesé».
/// <para>
/// Para qué sirve: a veces el teléfono no sabe si su acción llegó (por ejemplo, se cortó la señal) y la manda otra vez
/// con el mismo número. Con este apunte, el servidor reconoce ese número y no repite nada: ni crea otro seguimiento ni
/// vuelve a disparar el pago. Solo devuelve la respuesta que ya había dado.
/// </para>
/// <para>
/// Es la tabla <c>OperacionesProcesadas</c> de la base de datos de Cargo. Solo se guardan las operaciones que salieron
/// bien. Es parecido a <c>EventoRecibido</c> de Billing, pero para las acciones que llegan del teléfono (ADR 0005).
/// </para>
/// </summary>
public sealed class OperacionProcesada
{
    /// <summary>
    /// Constructor privado: nadie crea el objeto con <c>new</c>. La base de datos lo usa para leer un apunte, y para crear
    /// uno nuevo se usa <see cref="Registrar"/>.
    /// </summary>
    private OperacionProcesada()
    {
        Resultado = string.Empty;
    }

    /// <summary>
    /// El número de la operación. Lo inventa el teléfono al guardar la acción (se llama <c>OperationId</c>) y llega en el
    /// encabezado <c>Idempotency-Key</c>. No puede haber dos apuntes con el mismo número.
    /// </summary>
    public Guid OperacionId { get; private set; }

    /// <summary>
    /// La carga sobre la que se hizo la operación. Sirve para detectar si alguien usa por error el mismo número en otra carga.
    /// </summary>
    public Guid CargaId { get; private set; }

    /// <summary>
    /// La respuesta que se le dio al teléfono la primera vez, guardada como texto (JSON). Si la operación se repite, se
    /// devuelve este mismo texto.
    /// </summary>
    public string Resultado { get; private set; }

    /// <summary>Cuándo se procesó la operación por primera vez.</summary>
    public DateTime ProcesadaEn { get; private set; }

    /// <summary>
    /// Crea el apunte de una operación que ya se procesó. Se llama dentro de la misma transacción que el cambio de la
    /// carga, así que se guardan los dos juntos o ninguno: nunca hay un cambio sin su apunte, ni un apunte sin su cambio.
    /// Lanza un error de validación si falta el número, la carga o la respuesta.
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
