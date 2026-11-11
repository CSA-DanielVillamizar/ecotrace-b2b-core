namespace EcoTrace.FleetManagement.Domain;

public enum TipoNovedad
{
    Mecanica,
    Llanta,
    Accidente,
    Otra
}

/// <summary>
/// Algo que le pasó a un vehículo y que el conductor reporta desde la app (Trabajo 3). La app puede guardarla
/// sin señal y enviarla horas después, por eso guarda dos fechas: cuándo ocurrió (la del teléfono) y cuándo
/// la recibió el servidor.
/// </summary>
public sealed class Novedad
{
    public const int LargoMinimoDescripcion = 5;
    public const int LargoMaximoDescripcion = 500;

    /// <summary>Margen para el reloj del teléfono, que puede ir un poco adelantado respecto al del servidor.</summary>
    private static readonly TimeSpan ToleranciaReloj = TimeSpan.FromMinutes(5);

    private Novedad()
    {
        Descripcion = string.Empty;
    }

    public Guid NovedadId { get; private set; }

    public Guid VehiculoId { get; private set; }

    [ReferenciaExterna("Identity", "Transportista dueño del vehículo")]
    public Guid TenantId { get; private set; }

    [ReferenciaExterna("Identity", "Usuario que reportó la novedad")]
    public Guid ReportadaPorUserId { get; private set; }

    [ReferenciaExterna("CargoTracking", "Carga en curso cuando se reportó, si había una")]
    public Guid? CargaId { get; private set; }

    public TipoNovedad Tipo { get; private set; }

    public string Descripcion { get; private set; }

    public DateTime OcurridaEn { get; private set; }

    public DateTime RecibidaEn { get; private set; }

    /// <summary>
    /// El OperationId que generó la app al guardar la acción, recibido en el encabezado Idempotency-Key.
    /// Es único: si la misma acción llega dos veces, la segunda no crea otra novedad (ADR 0005).
    /// </summary>
    public Guid? ClaveIdempotencia { get; private set; }

    public static Novedad Reportar(
        Vehiculo vehiculo, Guid reportadaPorUserId, Guid? cargaId, TipoNovedad tipo, string? descripcion,
        DateTime ocurridaEnUtc, DateTime ahoraUtc, Guid claveIdempotencia)
    {
        if (claveIdempotencia == Guid.Empty)
        {
            throw DomainException.Validation("Falta el encabezado Idempotency-Key con el OperationId de la acción.");
        }

        if (reportadaPorUserId == Guid.Empty)
        {
            throw DomainException.Validation("El usuario que reporta la novedad es obligatorio.");
        }

        if (!Enum.IsDefined(tipo))
        {
            throw DomainException.Validation("El tipo de novedad no es válido.");
        }

        var texto = (descripcion ?? string.Empty).Trim();
        if (texto.Length is < LargoMinimoDescripcion or > LargoMaximoDescripcion)
        {
            throw DomainException.Validation(
                $"La descripción debe tener entre {LargoMinimoDescripcion} y {LargoMaximoDescripcion} caracteres.");
        }

        if (ocurridaEnUtc > ahoraUtc + ToleranciaReloj)
        {
            throw DomainException.Validation("La fecha de la novedad no puede estar en el futuro.");
        }

        // Conflicto B del ADR 0005: mientras el conductor no tenía señal, el vehículo se reservó para otra carga.
        // El servidor manda: la novedad era de un viaje que ya no es de este conductor y no se registra.
        // Si el vehículo quedó disponible (la carga terminó), la novedad sí se acepta: sigue siendo del vehículo.
        if (cargaId is not null && vehiculo.Estado == EstadoRecurso.Reservado && vehiculo.ReservadoParaCargaId != cargaId)
        {
            throw DomainException.Conflict(
                "El vehículo fue reservado para otra carga mientras no tenías señal. La novedad no se registró.");
        }

        return new Novedad
        {
            NovedadId = Guid.NewGuid(),
            VehiculoId = vehiculo.VehiculoId,
            TenantId = vehiculo.TenantId,
            ReportadaPorUserId = reportadaPorUserId,
            CargaId = cargaId == Guid.Empty ? null : cargaId,
            Tipo = tipo,
            Descripcion = texto,
            OcurridaEn = ocurridaEnUtc,
            RecibidaEn = ahoraUtc,
            ClaveIdempotencia = claveIdempotencia
        };
    }
}
