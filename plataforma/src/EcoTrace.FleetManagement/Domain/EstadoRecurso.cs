namespace EcoTrace.FleetManagement.Domain;

/// <summary>
/// Disponibilidad de un vehículo o de un conductor (ADR 0003, Saga "Iniciar Transporte"):
/// un recurso Reservado pertenece a una carga y no puede reservarse para otra.
/// </summary>
public enum EstadoRecurso
{
    Disponible = 1,
    Reservado = 2
}
