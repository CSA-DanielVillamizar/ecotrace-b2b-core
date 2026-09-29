using EcoTrace.Identity.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Infrastructure;

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

    /// <summary>
    /// Igual que <see cref="GuardarAsync"/>, pero para el caso de una carrera legitima (dos
    /// solicitudes concurrentes intentando crear el mismo recurso unico): en vez de lanzar un
    /// conflicto, devuelve false para que quien pierda la carrera pueda responder con el
    /// resultado de quien gano, en lugar de un error.
    /// </summary>
    public static async Task<bool> IntentarGuardarAsync(this DbContext db, CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException
        {
            SqliteExtendedErrorCode: PrimaryKeyViolation or UniqueViolation
        })
        {
            return false;
        }
    }
}
