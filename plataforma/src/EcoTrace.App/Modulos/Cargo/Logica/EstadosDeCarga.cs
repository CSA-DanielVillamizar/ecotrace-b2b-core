namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Los estados de una carga tal como los escribe el servidor de Cargo &amp; Tracking, y los cambios que la app le
/// ofrece al conductor desde cada uno.
/// <para>
/// Las reglas de aquí repiten a propósito las del servidor (<c>Carga.TransicionPermitida</c>), solo para no mostrar
/// botones que el servidor va a rechazar. La autoridad sigue siendo el servidor: si el estado cambió mientras no
/// había señal, él rechaza la acción con un conflicto de tipo B y gana (ADR 0005).
/// </para>
/// </summary>
public static class EstadosDeCarga
{
    public const string Pendiente = "Pendiente";
    public const string Asignado = "Asignado";
    public const string EnTransito = "EnTransito";
    public const string ConNovedad = "ConNovedad";
    public const string Entregado = "Entregado";

    /// <summary>A qué estados puede pasar una carga desde <paramref name="estado"/>. Vacío si no hay cambios posibles.</summary>
    public static IReadOnlyList<string> SiguientesPermitidos(string estado) => estado switch
    {
        Asignado => [EnTransito],
        EnTransito => [ConNovedad, Entregado],
        ConNovedad => [EnTransito, Entregado],
        _ => []
    };

    public static bool TransicionPermitida(string actual, string siguiente) =>
        SiguientesPermitidos(actual).Contains(siguiente);

    /// <summary>Cómo se le muestra el estado al conductor.</summary>
    public static string Texto(string estado) => estado switch
    {
        Pendiente => "Sin asignar",
        Asignado => "Asignada",
        EnTransito => "En tránsito",
        ConNovedad => "Con novedad",
        Entregado => "Entregada",
        _ => estado
    };

    /// <summary>El texto del botón que lleva la carga a <paramref name="destino"/>.</summary>
    public static string Boton(string destino) => destino switch
    {
        EnTransito => "En tránsito",
        ConNovedad => "Reportar novedad",
        Entregado => "Confirmar entrega",
        _ => Texto(destino)
    };
}
