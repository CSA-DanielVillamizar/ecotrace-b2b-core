namespace EcoTrace.CargoTracking.Domain;

/// <summary>
/// Ciclo de vida de una carga. Asignado, EnTransito, ConNovedad y Entregado son los estados
/// de seguimiento del ADR 0001. Pendiente es el estado inicial, antes de asignar vehículo y
/// conductor; el ADR no lo nombra, pero hace falta para representar una carga recién creada.
/// </summary>
public enum EstadoCarga
{
    Pendiente = 0,
    Asignado = 1,
    EnTransito = 2,
    ConNovedad = 3,
    Entregado = 4
}
