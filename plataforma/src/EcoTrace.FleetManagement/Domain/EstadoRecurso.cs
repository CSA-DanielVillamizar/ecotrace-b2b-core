namespace EcoTrace.FleetManagement.Domain;

/// <summary>
/// Disponibilidad de un recurso de la flota. Se agrega en el Trabajo 2 para el Saga "Liberar Pago
/// en Escrow" (ADR 0003): un vehiculo o un conductor quedan Reservado mientras atienden una carga
/// y vuelven a Disponible cuando Billing confirma la entrega.
/// </summary>
public enum EstadoRecurso
{
    Disponible,
    Reservado
}
