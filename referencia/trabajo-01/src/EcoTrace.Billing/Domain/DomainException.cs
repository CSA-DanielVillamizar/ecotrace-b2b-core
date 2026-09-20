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
/// Error de regla de negocio. El dominio lo lanza; la capa Api lo traduce a ProblemDetails.
/// </summary>
public sealed class DomainException : Exception
{
    private DomainException(DomainErrorKind kind, string message) : base(message) => Kind = kind;

    public DomainErrorKind Kind { get; }

    public static DomainException Validation(string message) => new(DomainErrorKind.Validation, message);

    public static DomainException Conflict(string message) => new(DomainErrorKind.Conflict, message);

    public static DomainException NotFound(string message) => new(DomainErrorKind.NotFound, message);
}
