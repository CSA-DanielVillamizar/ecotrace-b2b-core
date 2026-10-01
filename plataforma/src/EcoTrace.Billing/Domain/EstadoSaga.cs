namespace EcoTrace.Billing.Domain;

public enum EstadoSaga
{
    EnCurso,
    Completada,
    Compensando,
    Compensada,
    Fallida,
    RequiereIntervencion
}