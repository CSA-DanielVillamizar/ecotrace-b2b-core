namespace EcoTrace.CargoTracking.API.Dtos;

// Datos que el cliente envía para registrar una nueva Carga.
public class CreateCargaDto
{
    public Guid GeneradorTenantId { get; set; }
    public Guid TransportistaTenantId { get; set; }
    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
}

// Datos para asignar un vehículo y conductor a una Carga existente.
public class CreateAsignacionDto
{
    public Guid VehiculoId { get; set; }
    public Guid ConductorId { get; set; }
}

// Datos para registrar un nuevo punto de seguimiento de una Carga.
public class CreateSeguimientoDto
{
    public string Ubicacion { get; set; } = string.Empty;
    public Domain.EstadoRecorrido Estado { get; set; }
    public string Descripcion { get; set; } = string.Empty;
}