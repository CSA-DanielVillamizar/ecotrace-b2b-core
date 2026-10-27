namespace EcoTrace.Identity.Domain;

/// <summary>
/// Una acción de la app que Identity ya procesó, identificada por el <c>OperationId</c> que la app generó al
/// guardarla en el teléfono y que llega en el encabezado <c>Idempotency-Key</c> (ADR 0005).
/// <para>
/// Si la respuesta se pierde por un corte de red, la app reenvía la acción con la misma clave. Con este registro
/// Identity no la aplica otra vez y devuelve el resultado de la primera vez. Sin él, reenviar un cambio de perfil
/// viejo podría pisar uno más nuevo que llegó en el medio.
/// </para>
/// <para>
/// Solo se guardan las que tuvieron éxito, en la misma transacción que el cambio. Una rechazada no se guarda: al
/// reintentarla se vuelve a evaluar contra el estado actual y da el mismo rechazo.
/// </para>
/// </summary>
public sealed class OperacionProcesada
{
    private OperacionProcesada()
    {
        Tipo = string.Empty;
        Resultado = string.Empty;
    }

    /// <summary>El OperationId de la app. Es la clave primaria: la base no deja que se repita.</summary>
    public Guid OperacionId { get; private set; }

    /// <summary>Usuario que hizo la acción. Una clave no sirve para la acción de otro usuario.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Qué acción fue (por ejemplo "ActualizarPerfil"). Una clave no sirve para otra acción.</summary>
    public string Tipo { get; private set; }

    /// <summary>Respuesta que se dio la primera vez, en JSON. Es la que se devuelve si la acción se repite.</summary>
    public string Resultado { get; private set; }

    public DateTime ProcesadaEn { get; private set; }

    public static OperacionProcesada Registrar(Guid operacionId, Guid userId, string tipo, string resultado, DateTime ahoraUtc)
    {
        if (operacionId == Guid.Empty)
        {
            throw DomainException.Validation("La operación necesita un identificador.");
        }

        if (string.IsNullOrWhiteSpace(tipo) || string.IsNullOrWhiteSpace(resultado))
        {
            throw DomainException.Validation("La operación necesita su tipo y su resultado.");
        }

        return new OperacionProcesada
        {
            OperacionId = operacionId,
            UserId = userId,
            Tipo = tipo,
            Resultado = resultado,
            ProcesadaEn = ahoraUtc
        };
    }

    /// <summary>True si la clave ya se usó para esta misma acción de este mismo usuario.</summary>
    public bool EsLaMisma(Guid userId, string tipo) => UserId == userId && string.Equals(Tipo, tipo, StringComparison.Ordinal);
}
