using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EcoTrace.Identity.Infrastructure;

/// <summary>
/// SQLite guarda las fechas como texto y pierde el Kind al leerlas. Con este conversor todo
/// DateTime sale como UTC y el JSON incluye la "Z", asi el cliente no adivina la zona horaria.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            escrita => escrita.Kind == DateTimeKind.Utc ? escrita : escrita.ToUniversalTime(),
            leida => DateTime.SpecifyKind(leida, DateTimeKind.Utc))
    {
    }
}
