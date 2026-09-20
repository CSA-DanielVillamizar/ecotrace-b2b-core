using EcoTrace.FleetManagement.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Infrastructure;

public static class SaveChangesExtensions
{
    // Codigos extendidos de SQLite: 1555 = clave primaria repetida, 2067 = indice unico repetido.
    private const int PrimaryKeyViolation = 1555;
    private const int UniqueViolation = 2067;

    /// <summary>
    /// Guarda los cambios y traduce dos fallos de persistencia a conflictos de dominio, para que
    /// la capa Api responda 409 sin conocer detalles de SQLite: una violacion de unicidad y un
    /// choque de concurrencia (otra solicitud modifico el mismo registro antes).
    /// </summary>
    public static async Task GuardarAsync(this DbContext db, string mensajeSiDuplicado, CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw DomainException.Conflict("El registro cambió mientras se procesaba la solicitud. Consulte el estado actual y reintente.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException
        {
            SqliteExtendedErrorCode: PrimaryKeyViolation or UniqueViolation
        })
        {
            throw DomainException.Conflict(mensajeSiDuplicado);
        }
    }
}
