using System.Text.RegularExpressions;

namespace EcoTrace.FleetManagement.Domain;

/// <summary>
/// Persona que conduce para un transportista (ADR 0001). Las tres propiedades marcadas con
/// [ReferenciaExterna] apuntan a Identity y se guardan como Guid plano: Fleet Management
/// nunca consulta la base de datos de Identity ni valida el TenantId en vivo.
/// </summary>
public sealed partial class Conductor
{
    private Conductor()
    {
        Nombre = string.Empty;
        Licencia = string.Empty;
    }

    public Guid ConductorId { get; private set; }

    [ReferenciaExterna("Identity", "Transportista dueño de la flota")]
    public Guid TenantId { get; private set; }

    [ReferenciaExterna("Identity", "Usuario que registró al conductor")]
    public Guid RegistradoPorUserId { get; private set; }

    [ReferenciaExterna("Identity", "Cuenta de usuario del conductor, si ya tiene una")]
    public Guid? UserId { get; private set; }

    public string Nombre { get; private set; }

    public string Licencia { get; private set; }

    public DateTime CreadoEn { get; private set; }

    /// <summary>Disponible mientras no esté reservado para una carga.</summary>
    public EstadoRecurso Estado { get; private set; }

    [ReferenciaExterna("CargoTracking", "Carga para la que está reservado el conductor")]
    public Guid? ReservadoParaCargaId { get; private set; }

    public static Conductor Crear(
        Guid tenantId, Guid registradoPorUserId, Guid? userId, string? nombre, string? licencia, DateTime ahoraUtc)
    {
        if (tenantId == Guid.Empty)
        {
            throw DomainException.Validation("El TenantId del transportista es obligatorio.");
        }

        if (registradoPorUserId == Guid.Empty)
        {
            throw DomainException.Validation("El usuario que registra al conductor es obligatorio.");
        }

        var nombreLimpio = (nombre ?? string.Empty).Trim();
        if (nombreLimpio.Length is < 2 or > 120)
        {
            throw DomainException.Validation("El nombre del conductor debe tener entre 2 y 120 caracteres.");
        }

        var licenciaNormalizada = (licencia ?? string.Empty).Trim().ToUpperInvariant();
        if (!FormatoLicencia().IsMatch(licenciaNormalizada))
        {
            throw DomainException.Validation(
                "La licencia debe tener entre 4 y 20 caracteres: letras, números o guiones.");
        }

        return new Conductor
        {
            ConductorId = Guid.NewGuid(),
            TenantId = tenantId,
            RegistradoPorUserId = registradoPorUserId,
            UserId = userId == Guid.Empty ? null : userId,
            Nombre = nombreLimpio,
            Licencia = licenciaNormalizada,
            CreadoEn = ahoraUtc,
            Estado = EstadoRecurso.Disponible
        };
    }

    /// <summary>
    /// Reserva al conductor para una carga. Devuelve true solo si el estado cambió: reservarlo otra
    /// vez para la misma carga no hace nada, y es lo que permite responder 200 en lugar de 201.
    /// </summary>
    public bool Reservar(Guid cargaId)
    {
        if (cargaId == Guid.Empty)
        {
            throw DomainException.Validation("La carga es obligatoria para reservar al conductor.");
        }

        if (Estado == EstadoRecurso.Reservado)
        {
            // Ya reservado para esta misma carga: no hay nada que hacer. Para otra: no se le quita.
            if (ReservadoParaCargaId == cargaId)
            {
                return false;
            }

            throw DomainException.Conflict("El conductor ya está reservado para otra carga.");
        }

        Estado = EstadoRecurso.Reservado;
        ReservadoParaCargaId = cargaId;
        return true;
    }

    /// <summary>
    /// Libera al conductor de una carga. Liberar uno que ya está disponible devuelve false sin
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
            throw DomainException.Conflict("El conductor está reservado para otra carga.");
        }

        Estado = EstadoRecurso.Disponible;
        ReservadoParaCargaId = null;
        return true;
    }

    [GeneratedRegex("^[A-Z0-9-]{4,20}$")]
    private static partial Regex FormatoLicencia();
}
