namespace EcoTrace.Billing.Domain;

public enum DomainErrorKind
{
    /// <summary>El dato recibido no cumple una regla de validacion (HTTP 400).</summary>
    Validation,

    /// <summary>La operacion choca con el estado actual del recurso (HTTP 409).</summary>
    Conflict,

    /// <summary>El recurso pedido no existe dentro de este contexto (HTTP 404).</summary>
    NotFound
}

/// <summary>
/// Clasificación de conflictos del ADR 0005, que la app móvil usa para decidir qué hacer con una acción rechazada.
/// </summary>
public enum TipoConflicto
{
    /// <summary>Validación recuperable: el dato se puede corregir y reintentar.</summary>
    A,

    /// <summary>Estado autoritativo: el servidor cambió mientras la app no tenía señal. El servidor gana.</summary>
    B,

    /// <summary>Edición concurrente: dos personas legítimas cambiaron lo mismo. Nunca gana el último.</summary>
    C
}

/// <summary>
/// Error de regla de negocio. El dominio lo lanza; la capa Api lo traduce a ProblemDetails.
/// </summary>
public sealed class DomainException : Exception
{
    private DomainException(DomainErrorKind kind, string message, TipoConflicto? tipo) : base(message)
    {
        Kind = kind;
        Tipo = tipo;
    }

    public DomainErrorKind Kind { get; }

    /// <summary>A, B o C cuando el error es un rechazo que la app tiene que clasificar; nulo si no aplica (un 404).</summary>
    public TipoConflicto? Tipo { get; }

    /// <summary>Una validación (400) es un conflicto de tipo A: el conductor puede corregir el dato.</summary>
    public static DomainException Validation(string message) => new(DomainErrorKind.Validation, message, TipoConflicto.A);

    /// <summary>Un conflicto (409) es de tipo B, el estado cambió y el servidor decidió, salvo que se diga otro.</summary>
    public static DomainException Conflict(string message, TipoConflicto tipo = TipoConflicto.B) => new(DomainErrorKind.Conflict, message, tipo);

    public static DomainException NotFound(string message) => new(DomainErrorKind.NotFound, message, null);
}
