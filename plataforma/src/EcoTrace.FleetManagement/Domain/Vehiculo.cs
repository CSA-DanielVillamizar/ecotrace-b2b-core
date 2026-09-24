using System.Text.RegularExpressions;

namespace EcoTrace.FleetManagement.Domain;

/// <summary>Vehículo de un transportista (ADR 0001). Mismo criterio de referencias que Conductor.</summary>
public sealed partial class Vehiculo
{
    public const int CapacidadMaximaKg = 80_000;

    private Vehiculo()
    {
        Placa = string.Empty;
    }

    public Guid VehiculoId { get; private set; }

    [ReferenciaExterna("Identity", "Transportista dueño de la flota")]
    public Guid TenantId { get; private set; }

    [ReferenciaExterna("Identity", "Usuario que registró el vehículo")]
    public Guid RegistradoPorUserId { get; private set; }

    public string Placa { get; private set; }

    public int CapacidadKg { get; private set; }

    public DateTime CreadoEn { get; private set; }

    /// <summary>Disponible mientras no esté reservado para una carga.</summary>
    public EstadoRecurso Estado { get; private set; }

    [ReferenciaExterna("CargoTracking", "Carga para la que está reservado el vehículo")]
    public Guid? ReservadoParaCargaId { get; private set; }

    public static Vehiculo Crear(
        Guid tenantId, Guid registradoPorUserId, string? placa, int capacidadKg, DateTime ahoraUtc)
    {
        if (tenantId == Guid.Empty)
        {
            throw DomainException.Validation("El TenantId del transportista es obligatorio.");
        }

        if (registradoPorUserId == Guid.Empty)
        {
            throw DomainException.Validation("El usuario que registra el vehículo es obligatorio.");
        }

        var placaNormalizada = (placa ?? string.Empty).Replace(" ", "").Replace("-", "").ToUpperInvariant();
        if (!FormatoPlaca().IsMatch(placaNormalizada))
        {
            throw DomainException.Validation("La placa debe tener entre 5 y 8 letras o números.");
        }

        if (capacidadKg is < 1 or > CapacidadMaximaKg)
        {
            throw DomainException.Validation($"La capacidad debe estar entre 1 y {CapacidadMaximaKg:N0} kg.");
        }

        return new Vehiculo
        {
            VehiculoId = Guid.NewGuid(),
            TenantId = tenantId,
            RegistradoPorUserId = registradoPorUserId,
            Placa = placaNormalizada,
            CapacidadKg = capacidadKg,
            CreadoEn = ahoraUtc,
            Estado = EstadoRecurso.Disponible
        };
    }

    /// <summary>
    /// Reserva el vehículo para una carga. Devuelve true solo si el estado cambió: reservarlo otra
    /// vez para la misma carga no hace nada, y es lo que permite responder 200 en lugar de 201.
    /// </summary>
    public bool Reservar(Guid cargaId)
    {
        if (cargaId == Guid.Empty)
        {
            throw DomainException.Validation("La carga es obligatoria para reservar el vehículo.");
        }

        if (Estado == EstadoRecurso.Reservado)
        {
            // Ya reservado para esta misma carga: no hay nada que hacer. Para otra: no se le quita.
            if (ReservadoParaCargaId == cargaId)
            {
                return false;
            }

            throw DomainException.Conflict("El vehículo ya está reservado para otra carga.");
        }

        Estado = EstadoRecurso.Reservado;
        ReservadoParaCargaId = cargaId;
        return true;
    }

    /// <summary>
    /// Libera el vehículo de una carga. Liberar uno que ya está disponible devuelve false sin
    /// tocar nada: Billing reintenta esta orden hasta cuatro veces y ninguna puede fallar.
    /// </summary>
    public bool Liberar(Guid cargaId)
    {
        if (Estado == EstadoRecurso.Disponible)
        {
            return false;
        }

        if (ReservadoParaCargaId != cargaId)
        {
            throw DomainException.Conflict("El vehículo está reservado para otra carga.");
        }

        Estado = EstadoRecurso.Disponible;
        ReservadoParaCargaId = null;
        return true;
    }

    [GeneratedRegex("^[A-Z0-9]{5,8}$")]
    private static partial Regex FormatoPlaca();
}
