namespace EcoTrace.Billing.Domain;

/// <summary>
/// Marca una propiedad que apunta a una entidad de OTRO Bounded Context. Es siempre un
/// identificador plano: sin propiedad de navegacion, sin clave foranea, sin consulta directa
/// a la base de datos ajena (regla de oro del ADR 0001).
/// </summary>
/// <param name="contexto">Contexto dueño de la entidad referenciada (Identity, FleetManagement, CargoTracking).</param>
/// <param name="descripcion">Que representa el identificador en este modelo.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ReferenciaExternaAttribute(string contexto, string descripcion) : Attribute
{
    public string Contexto { get; } = contexto;

    public string Descripcion { get; } = descripcion;
}
