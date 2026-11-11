namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Los estados de una carga con los mismos nombres que usa el servidor, y a qué estados puede pasar la app desde cada uno.
/// <para>
/// Las reglas de aquí repiten a propósito las del servidor (<c>Carga.TransicionPermitida</c>), solo para no mostrar botones
/// que el servidor va a rechazar. La autoridad sigue siendo el servidor: si el estado cambió mientras no había señal, él
/// rechaza la acción con un conflicto de tipo B y gana (ADR 0005).
/// </para>
/// </summary>
public static class EstadosDeCarga
{
    /// <summary>La carga se creó pero todavía no tiene vehículo ni conductor.</summary>
    public const string Pendiente = "Pendiente";
    /// <summary>La carga ya tiene vehículo y conductor, pero todavía no salió.</summary>
    public const string Asignado = "Asignado";
    /// <summary>La carga va en camino.</summary>
    public const string EnTransito = "EnTransito";
    /// <summary>Pasó algo en el camino (un retraso, un daño...). Siempre lleva una nota que lo explica.</summary>
    public const string ConNovedad = "ConNovedad";
    /// <summary>La carga llegó. Es el estado final y el que dispara el pago.</summary>
    public const string Entregado = "Entregado";

    /// <summary>
    /// Dice a qué estados se puede pasar desde el estado dado. Por ejemplo, desde «En tránsito» se puede pasar a «Con novedad»
    /// o a «Entregado». Si no se puede pasar a ninguno, devuelve una lista vacía.
    /// </summary>
    public static IReadOnlyList<string> SiguientesPermitidos(string estado) => estado switch
    {
        Asignado => [EnTransito],
        EnTransito => [ConNovedad, Entregado],
        ConNovedad => [EnTransito, Entregado],
        _ => []
    };

    /// <summary>Dice si se puede pasar del estado <paramref name="actual"/> al estado <paramref name="siguiente"/>.</summary>
    public static bool TransicionPermitida(string actual, string siguiente) =>
        SiguientesPermitidos(actual).Contains(siguiente);

    /// <summary>El nombre del estado como se le muestra al conductor (por ejemplo «En tránsito»).</summary>
    public static string Texto(string estado) => estado switch
    {
        Pendiente => "Sin asignar",
        Asignado => "Asignada",
        EnTransito => "En tránsito",
        ConNovedad => "Con novedad",
        Entregado => "Entregada",
        _ => estado
    };

    /// <summary>El texto del botón que lleva la carga a ese estado (por ejemplo «Confirmar entrega»).</summary>
    public static string Boton(string destino) => destino switch
    {
        EnTransito => "En tránsito",
        ConNovedad => "Reportar novedad",
        Entregado => "Confirmar entrega",
        _ => Texto(destino)
    };
}
