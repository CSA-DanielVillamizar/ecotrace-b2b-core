using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// La caché de cargas en SQLite, en su propio archivo (no en el de la cola, que es compartido y lo define el
/// Mobile.Core). <b>Nunca</b> guarda tokens ni contraseñas: eso vive en SecureStorage (ADR 0005).
/// </summary>
public sealed class CacheDeCargasSqlite : ICacheDeCargas
{
    private const string Columnas = "CargaId, Descripcion, Origen, Destino, PesoKg, Estado, ActualizadaEnUtc";

    private readonly string _cadenaDeConexion;
    private readonly SemaphoreSlim _inicio = new(1, 1);
    private bool _creada;

    /// <param name="rutaBaseDeDatos">Archivo SQLite, por ejemplo dentro de <c>FileSystem.AppDataDirectory</c>.</param>
    public CacheDeCargasSqlite(string rutaBaseDeDatos)
    {
        _cadenaDeConexion = new SqliteConnectionStringBuilder { DataSource = rutaBaseDeDatos, Pooling = false }.ToString();
    }

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

    private async Task<SqliteConnection> AbrirConexionAsync(CancellationToken ct)
    {
        var conexion = new SqliteConnection(_cadenaDeConexion);
        await conexion.OpenAsync(ct);
        await using var espera = conexion.CreateCommand();
        espera.CommandText = "PRAGMA busy_timeout = 5000;";
        await espera.ExecuteNonQueryAsync(ct);
        return conexion;
    }

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

    private static CargaEnCache Leer(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3),
        r.GetInt32(4), r.GetString(5), Fecha(r.GetString(6)));

    private static string Texto(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Fecha(string texto) =>
        DateTime.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
