namespace EcoTrace.Cargo_Tracking.Domain.Constants;

public static class ErrorMessages
{
    public static class Generic
    {
        public const string RequiredField = "El campo {0} es obligatorio.";
        public const string InvalidId = "El identificador proporcionado no es válido.";
        public const string TenantsMustBeDifferent = "El tenant generador y el transportista deben ser distintos.";
    }

    public static class Cargo
    {
        public const string DescriptionRequired = "La descripción de la carga es obligatoria.";
        public const string WeightMustBeGreaterThanZero = "El peso de la carga debe ser mayor a cero.";
        public const string OriginRequired = "La dirección de origen es obligatoria.";
        public const string DestinationRequired = "La dirección de destino es obligatoria.";
        public const string CargoNotFound = "La carga solicitada no fue encontrada.";
        public const string CargoNotAvailableForAssignment = "La carga no se encuentra en estado disponible o reservado para asignación.";
        public const string InvalidStatusTransition = "Transición de estado no permitida para la carga.";
    }

    public static class CargoAssignment
    {
        public const string CargoRequired = "El identificador de la carga es obligatorio.";
        public const string DriverRequired = "El identificador del conductor es obligatorio.";
        public const string VehicleRequired = "El identificador del vehículo es obligatorio.";
        public const string AssignmentNotFound = "La asignación solicitada no fue encontrada.";
        public const string CargoAlreadyAssigned = "La carga ya cuenta con una asignación activa.";
    }

    public static class Tracking
    {
        public const string TrackingNotFound = "La sesión de seguimiento no fue encontrada.";
        public const string InvalidCoordinates = "Las coordenadas geográficas proporcionadas no son válidas.";
        public const string TrackingAlreadyCompleted = "La sesión de seguimiento ya fue completada o cancelada.";
    }
}