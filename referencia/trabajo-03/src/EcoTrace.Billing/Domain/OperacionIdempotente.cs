namespace EcoTrace.Billing.Domain;

/// <summary>
/// Una acción que ya se aplicó, identificada por la <c>Idempotency-Key</c> con la que llegó (ADR 0005). Guarda lo que se respondió,
/// para devolverlo igual si la app reintenta tras un corte de red sin repetir el efecto. Son todo textos y números: aquí no hay
/// identificadores de otros contextos.
/// </summary>
public sealed class OperacionIdempotente
{
    private OperacionIdempotente()
    {
        Clave = string.Empty;
        Alcance = string.Empty;
        Huella = string.Empty;
        Respuesta = string.Empty;
    }

    public long OperacionId { get; private set; }

    public string Clave { get; private set; }

    /// <summary>Quién y a qué ruta: la misma clave en otra ruta o de otra persona es otra operación.</summary>
    public string Alcance { get; private set; }

    /// <summary>Resumen del contenido de la solicitud. Una clave que vuelve con otro contenido no es un reintento.</summary>
    public string Huella { get; private set; }

    public int EstadoHttp { get; private set; }

    public string Respuesta { get; private set; }

    public DateTime CreadaEn { get; private set; }

    public static OperacionIdempotente Registrar(
        string clave, string alcance, string huella, int estadoHttp, string respuesta, DateTime ahoraUtc) => new()
    {
        Clave = clave,
        Alcance = alcance,
        Huella = huella,
        EstadoHttp = estadoHttp,
        Respuesta = respuesta,
        CreadaEn = ahoraUtc
    };
}
