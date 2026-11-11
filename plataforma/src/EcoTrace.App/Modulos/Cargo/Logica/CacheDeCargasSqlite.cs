using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// La copia de las cargas guardada en un archivo SQLite del teléfono, en su propio archivo (no en el de la cola, que es
/// compartido). <b>Nunca</b> guarda tokens ni contraseñas: eso vive en SecureStorage (ADR 0005).
/// </summary>
public sealed class CacheDeCargasSqlite : ICacheDeCargas
{
    /// <summary>Las columnas de la tabla, en el orden en que se leen y se escriben.</summary>
    private const string Columnas = "CargaId, Descripcion, Origen, Destino, PesoKg, Estado, ActualizadaEnUtc";

    /// <summary>Cómo conectarse al archivo SQLite.</summary>
    private readonly string _cadenaDeConexion;
    /// <summary>Un candado para que la tabla se cree una sola vez.</summary>
    private readonly SemaphoreSlim _inicio = new(1, 1);
    /// <summary>Si la tabla ya se creó.</summary>
    private bool _creada;

    /// <summary>Prepara la copia sobre el archivo indicado (por ejemplo, dentro del almacenamiento privado de la app).</summary>
    /// <param name="rutaBaseDeDatos">La ruta del archivo SQLite.</param>
    public CacheDeCargasSqlite(string rutaBaseDeDatos)
    {
        _cadenaDeConexion = new SqliteConnectionStringBuilder { DataSource = rutaBaseDeDatos, Pooling = false }.ToString();
    }

    /// <summary>
    /// Borra lo que había y guarda la lista nueva, todo en una transacción: si algo falla a mitad, se descarta y queda la copia
    /// anterior intacta.
    /// </summary>
    public async Task ReemplazarAsync(IReadOnlyList<CargaEnCache> cargas, CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);

        // Todo o nada: si algo falla a mitad, la transacción se descarta y queda la copia anterior intacta.
        using var transaccion = conexion.BeginTransaction();

        await using (var borrar = conexion.CreateCommand())
        {
            borrar.Transaction = transaccion;
            borrar.CommandText = "DELETE FROM CargasCache";
            await borrar.ExecuteNonQueryAsync(ct);
        }

        foreach (var carga in cargas)
        {
            await using var insertar = conexion.CreateCommand();
            insertar.Transaction = transaccion;
            insertar.CommandText =
                $"INSERT INTO CargasCache ({Columnas}) " +
                "VALUES ($id, $descripcion, $origen, $destino, $peso, $estado, $actualizada)";
            insertar.Parameters.AddWithValue("$id", carga.CargaId.ToString("D"));
            insertar.Parameters.AddWithValue("$descripcion", carga.Descripcion);
            insertar.Parameters.AddWithValue("$origen", carga.Origen);
            insertar.Parameters.AddWithValue("$destino", carga.Destino);
            insertar.Parameters.AddWithValue("$peso", carga.PesoKg);
            insertar.Parameters.AddWithValue("$estado", carga.Estado);
            insertar.Parameters.AddWithValue("$actualizada", Texto(carga.ActualizadaEnUtc));
            await insertar.ExecuteNonQueryAsync(ct);
        }

        transaccion.Commit();
    }

    /// <summary>Lee las cargas guardadas, ordenadas por descripción.</summary>
    public async Task<IReadOnlyList<CargaEnCache>> ListarAsync(CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = $"SELECT {Columnas} FROM CargasCache ORDER BY Descripcion, CargaId";

        var resultado = new List<CargaEnCache>();
        await using var lector = await comando.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            resultado.Add(Leer(lector));
        }

        return resultado;
    }

    /// <summary>Abre la conexión. La primera vez, crea antes la tabla.</summary>
    private async Task<SqliteConnection> AbrirAsync(CancellationToken ct)
    {
        if (!_creada)
        {
            await _inicio.WaitAsync(ct);
            try
            {
                if (!_creada)
                {
                    await CrearEsquemaAsync(ct);
                    _creada = true;
                }
            }
            finally
            {
                _inicio.Release();
            }
        }

        return await AbrirConexionAsync(ct);
    }

    /// <summary>Abre la conexión con el archivo y le pide a SQLite que espere hasta 5 segundos si está ocupado.</summary>
    private async Task<SqliteConnection> AbrirConexionAsync(CancellationToken ct)
    {
        var conexion = new SqliteConnection(_cadenaDeConexion);
        await conexion.OpenAsync(ct);
        await using var espera = conexion.CreateCommand();
        espera.CommandText = "PRAGMA busy_timeout = 5000;";
        await espera.ExecuteNonQueryAsync(ct);
        return conexion;
    }

    /// <summary>Crea la tabla <c>CargasCache</c> si todavía no existe.</summary>
    private async Task CrearEsquemaAsync(CancellationToken ct)
    {
        await using var conexion = await AbrirConexionAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText =
            """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS CargasCache (
                CargaId TEXT PRIMARY KEY,
                Descripcion TEXT NOT NULL,
                Origen TEXT NOT NULL,
                Destino TEXT NOT NULL,
                PesoKg INTEGER NOT NULL,
                Estado TEXT NOT NULL,
                ActualizadaEnUtc TEXT NOT NULL
            );
            """;
        await comando.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Convierte una fila de la tabla en una <see cref="CargaEnCache"/>.</summary>
    private static CargaEnCache Leer(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3),
        r.GetInt32(4), r.GetString(5), Fecha(r.GetString(6)));

    /// <summary>Convierte una fecha en texto para guardarla en la tabla.</summary>
    private static string Texto(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Convierte el texto guardado de vuelta en una fecha UTC.</summary>
    private static DateTime Fecha(string texto) =>
        DateTime.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
