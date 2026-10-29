using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EcoTrace.App.Modulos.Fleet;

/// <summary>
/// Lo que el conductor ve sin señal: su registro de conductor y el vehículo con el que trabaja, tal como lo
/// devolvió Fleet la última vez que hubo conexión (<c>GET /api/conductores/yo</c>).
/// </summary>
public sealed record AsignacionEnCache(
    Guid ConductorId,
    string NombreConductor,
    string Licencia,
    string EstadoConductor,
    Guid? VehiculoId,
    string? Placa,
    int? CapacidadKg,
    string? EstadoVehiculo,
    Guid? CargaId,
    DateTime GuardadaEnUtc)
{
    public bool TieneVehiculo => VehiculoId is not null;
}

/// <summary>
/// La caché del módulo Fleet en SQLite, en su propio archivo (el de la cola es del Mobile.Core). Es una sola fila:
/// la última asignación que respondió el servidor. <b>Nunca</b> guarda tokens: esos viven en SecureStorage y los
/// maneja Identity (ADR 0005).
/// </summary>
public sealed class CacheDeFleetSqlite
{
    private readonly string _cadenaDeConexion;
    private readonly SemaphoreSlim _inicio = new(1, 1);
    private bool _creada;

    /// <param name="rutaBaseDeDatos">Archivo SQLite, por ejemplo dentro de <c>FileSystem.AppDataDirectory</c>.</param>
    public CacheDeFleetSqlite(string rutaBaseDeDatos)
    {
        _cadenaDeConexion = new SqliteConnectionStringBuilder { DataSource = rutaBaseDeDatos, Pooling = false }.ToString();
    }

    /// <summary>Reemplaza la asignación guardada por la nueva.</summary>
    public async Task GuardarAsync(AsignacionEnCache asignacion, CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText =
            "INSERT OR REPLACE INTO AsignacionFleet (Id, Json, GuardadaEnUtc) VALUES (1, $json, $guardada)";
        comando.Parameters.AddWithValue("$json", JsonSerializer.Serialize(asignacion));
        comando.Parameters.AddWithValue("$guardada", asignacion.GuardadaEnUtc.ToString("O", CultureInfo.InvariantCulture));
        await comando.ExecuteNonQueryAsync(ct);
    }

    /// <summary>La última asignación guardada, o null si nunca hubo conexión.</summary>
    public async Task<AsignacionEnCache?> LeerAsync(CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT Json FROM AsignacionFleet WHERE Id = 1";
        var json = await comando.ExecuteScalarAsync(ct) as string;
        return json is null ? null : JsonSerializer.Deserialize<AsignacionEnCache>(json);
    }

    private async Task<SqliteConnection> AbrirAsync(CancellationToken ct)
    {
        var conexion = new SqliteConnection(_cadenaDeConexion);
        await conexion.OpenAsync(ct);
        if (_creada)
        {
            return conexion;
        }

        await _inicio.WaitAsync(ct);
        try
        {
            if (!_creada)
            {
                await using var crear = conexion.CreateCommand();
                crear.CommandText =
                    "CREATE TABLE IF NOT EXISTS AsignacionFleet (" +
                    "Id INTEGER PRIMARY KEY CHECK (Id = 1), Json TEXT NOT NULL, GuardadaEnUtc TEXT NOT NULL)";
                await crear.ExecuteNonQueryAsync(ct);
                _creada = true;
            }
        }
        finally
        {
            _inicio.Release();
        }

        return conexion;
    }
}
