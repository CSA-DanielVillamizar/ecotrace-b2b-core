namespace EcoTrace.Identity.Domain;

/// <summary>Estado de una autorizacion de pago (paso 1 del Saga Liberar Pago en Escrow, ADR 0003).</summary>
public enum EstadoAutorizacion
{
    Autorizado,
    Revocado
}
