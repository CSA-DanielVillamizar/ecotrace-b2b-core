namespace EcoTrace.FleetManagement.Domain.Constants;

public static class ErrorMessages
{
    public static class Generic
    {
        public const string RequiredField = "El campo {0} es obligatorio.";
        public const string InvalidId = "El identificador proporcionado no es válido.";
    }

    public static class Driver
    {
        public const string FullNameRequired = "El nombre completo del conductor es obligatorio.";
        public const string LicenseNumberRequired = "El número de licencia de conducción es obligatorio.";
        public const string LicenseAlreadyExists = "Ya existe un conductor registrado con este número de licencia.";
        public const string DriverNotFound = "El conductor solicitado no fue encontrado.";
        public const string DriverNotAvailable = "El conductor no se encuentra en estado Disponible para asignación.";
        public const string InvalidStatusTransition = "Transición de estado no permitida para el conductor.";
    }

    public static class Vehicle
    {
        public const string PlateNumberRequired = "La placa del vehículo es obligatoria.";
        public const string PlateAlreadyExists = "Ya existe un vehículo registrado con esta placa.";
        public const string BrandRequired = "La marca del vehículo es obligatoria.";
        public const string ModelRequired = "El modelo del vehículo es obligatorio.";
        public const string CapacityMustBeGreaterThanZero = "La capacidad de carga debe ser mayor a cero.";
        public const string VehicleNotFound = "El vehículo solicitado no fue encontrado.";
        public const string VehicleNotAvailable = "El vehículo no se encuentra en estado Disponible para asignación.";
        public const string InvalidStatusTransition = "Transición de estado no permitida para el vehículo.";
    }
}