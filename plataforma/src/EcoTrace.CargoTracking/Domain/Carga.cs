namespace EcoTrace.CargoTracking.Domain;

/// <summary>
/// Carga de residuos a transportar. Es la raíz del agregado: la asignación y el seguimiento
/// solo se modifican a través de sus métodos, que aplican la máquina de estados.
/// Entidad de dos organizaciones (ADR 0001): quien genera y paga, y quien transporta y cobra.
/// </summary>
public sealed class Carga
{
    public const int PesoMaximoKg = 80_000;

    private readonly List<Seguimiento> _seguimientos = [];

    private Carga()
    {
        Descripcion = string.Empty;
        Origen = string.Empty;
        Destino = string.Empty;
    }

    public Guid CargaId { get; private set; }

    [ReferenciaExterna("Identity", "Organización que genera la carga y paga el servicio")]
    public Guid GeneradorTenantId { get; private set; }

    [ReferenciaExterna("Identity", "Transportista que ejecuta el servicio y cobra")]
    public Guid TransportistaTenantId { get; private set; }

    public string Descripcion { get; private set; }

    public string Origen { get; private set; }

    public string Destino { get; private set; }

    public int PesoKg { get; private set; }

    public EstadoCarga Estado { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public AsignacionCarga? Asignacion { get; private set; }

    public IReadOnlyList<Seguimiento> Seguimientos => _seguimientos;

    public static Carga Crear(
        Guid generadorTenantId, Guid transportistaTenantId, string? descripcion,
        string? origen, string? destino, int pesoKg, DateTime ahoraUtc)
    {
        if (generadorTenantId == Guid.Empty || transportistaTenantId == Guid.Empty)
        {
            throw DomainException.Validation("La carga necesita el generador y el transportista.");
        }

        if (generadorTenantId == transportistaTenantId)
        {
            throw DomainException.Validation(
                "El generador y el transportista deben ser organizaciones distintas.");
        }

        var descripcionLimpia = (descripcion ?? string.Empty).Trim();
        if (descripcionLimpia.Length is < 3 or > 200)
        {
            throw DomainException.Validation("La descripción debe tener entre 3 y 200 caracteres.");
        }

        var origenLimpio = (origen ?? string.Empty).Trim();
        var destinoLimpio = (destino ?? string.Empty).Trim();
        if (origenLimpio.Length is < 2 or > 120 || destinoLimpio.Length is < 2 or > 120)
        {
            throw DomainException.Validation("El origen y el destino deben tener entre 2 y 120 caracteres.");
        }

        if (string.Equals(origenLimpio, destinoLimpio, StringComparison.OrdinalIgnoreCase))
        {
            throw DomainException.Validation("El origen y el destino no pueden ser el mismo lugar.");
        }

        if (pesoKg is < 1 or > PesoMaximoKg)
        {
            throw DomainException.Validation($"El peso debe estar entre 1 y {PesoMaximoKg:N0} kg.");
        }

        return new Carga
        {
            CargaId = Guid.NewGuid(),
            GeneradorTenantId = generadorTenantId,
            TransportistaTenantId = transportistaTenantId,
            Descripcion = descripcionLimpia,
            Origen = origenLimpio,
            Destino = destinoLimpio,
            PesoKg = pesoKg,
            Estado = EstadoCarga.Pendiente,
            CreadoEn = ahoraUtc
        };
    }

    /// <summary>
    /// Asigna vehículo y conductor. Solo procede desde Pendiente y deja el primer evento de
    /// seguimiento. VehiculoId y ConductorId son identificadores de Fleet Management: aquí no se
    /// comprueba que existan ni que pertenezcan al transportista, porque eso exigiria consultar
    /// otro contexto (Trabajo 2, ADR 0002).
    /// </summary>
    public void Asignar(Guid vehiculoId, Guid conductorId, DateTime ahoraUtc)
    {
        if (vehiculoId == Guid.Empty || conductorId == Guid.Empty)
        {
            throw DomainException.Validation("La asignación necesita un vehículo y un conductor.");
        }

        if (Estado != EstadoCarga.Pendiente)
        {
            throw DomainException.Conflict("La carga ya fue asignada.");
        }

        Asignacion = AsignacionCarga.Crear(CargaId, vehiculoId, conductorId, ahoraUtc);
        Estado = EstadoCarga.Asignado;
        _seguimientos.Add(Seguimiento.Crear(CargaId, EstadoCarga.Asignado, Origen, "Carga asignada", ahoraUtc));
    }

    /// <summary>Registra un evento de recorrido y mueve la carga al nuevo estado si la transición es válida.</summary>
    public void RegistrarSeguimiento(EstadoCarga nuevoEstado, string? ubicacion, string? nota, DateTime ahoraUtc)
    {
        if (!TransicionPermitida(Estado, nuevoEstado))
        {
            throw DomainException.Conflict(
                $"No se puede pasar de {Estado} a {nuevoEstado}. " +
                $"Desde {Estado} se permite: {DescribirSiguientes(Estado)}.");
        }

        var notaLimpia = string.IsNullOrWhiteSpace(nota) ? null : nota.Trim();
        if (nuevoEstado == EstadoCarga.ConNovedad && notaLimpia is null)
        {
            throw DomainException.Validation("Una novedad necesita una nota que explique qué ocurrió.");
        }

        _seguimientos.Add(Seguimiento.Crear(CargaId, nuevoEstado, ubicacion, notaLimpia, ahoraUtc));
        Estado = nuevoEstado;
    }

    public static bool TransicionPermitida(EstadoCarga actual, EstadoCarga siguiente) =>
        (actual, siguiente) switch
        {
            (EstadoCarga.Asignado, EstadoCarga.EnTransito) => true,
            (EstadoCarga.EnTransito, EstadoCarga.ConNovedad) => true,
            (EstadoCarga.EnTransito, EstadoCarga.Entregado) => true,
            (EstadoCarga.ConNovedad, EstadoCarga.EnTransito) => true,
            (EstadoCarga.ConNovedad, EstadoCarga.Entregado) => true,
            _ => false
        };

    private static string DescribirSiguientes(EstadoCarga actual)
    {
        var siguientes = Enum.GetValues<EstadoCarga>().Where(e => TransicionPermitida(actual, e)).ToArray();
        return siguientes.Length == 0 ? "ninguna transición" : string.Join(", ", siguientes);
    }
}
