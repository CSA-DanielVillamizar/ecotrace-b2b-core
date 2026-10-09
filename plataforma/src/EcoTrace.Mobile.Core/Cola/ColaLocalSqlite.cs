using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EcoTrace.Mobile.Core.Cola;

/// <summary>
/// La cola en SQLite. Guarda acciones y el último contacto con el servidor; <b>nunca</b> guarda tokens ni
/// contraseñas (eso vive en SecureStorage, ADR 0005).
/// </summary>
public sealed class ColaLocalSqlite : IColaLocal
{
    private const string Columnas =
        "Secuencia, OperationId, Modulo, Tipo, Payload, Estado, TipoConflicto, MotivoRechazo, Intentos, CreadaEnUtc, ActualizadaEnUtc";

    private readonly string _cadenaDeConexion;
    private readonly TimeProvider _reloj;
    private readonly SemaphoreSlim _inicio = new(1, 1);
    private bool _creada;

    /// <param name="rutaBaseDeDatos">Archivo SQLite, por ejemplo dentro de <c>FileSystem.AppDataDirectory</c>.</param>
    public ColaLocalSqlite(string rutaBaseDeDatos, TimeProvider? reloj = null)
    {
        _cadenaDeConexion = new SqliteConnectionStringBuilder { DataSource = rutaBaseDeDatos, Pooling = false }.ToString();
        _reloj = reloj ?? TimeProvider.System;
    }

    public event EventHandler? Cambio;

    public async Task<OperacionPendiente> EncolarAsync(string modulo, string tipo, string payloadJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulo);
        ArgumentException.ThrowIfNullOrWhiteSpace(tipo);
        ArgumentNullException.ThrowIfNull(payloadJson);

        var operacion = new OperacionPendiente
        {
            OperationId = Guid.NewGuid(),
            Modulo = modulo,
            Tipo = tipo,
            Payload = payloadJson,
            Estado = EstadoOperacion.PendienteSync,
            CreadaEnUtc = Ahora()
        };

        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText =
            "INSERT INTO ColaOperaciones (OperationId, Modulo, Tipo, Payload, Estado, Intentos, CreadaEnUtc) " +
            "VALUES ($id, $modulo, $tipo, $payload, $estado, 0, $creada); SELECT last_insert_rowid();";
        comando.Parameters.AddWithValue("$id", operacion.OperationId.ToString("D"));
        comando.Parameters.AddWithValue("$modulo", modulo);
        comando.Parameters.AddWithValue("$tipo", tipo);
        comando.Parameters.AddWithValue("$payload", payloadJson);
        comando.Parameters.AddWithValue("$estado", operacion.Estado.ToString());
        comando.Parameters.AddWithValue("$creada", Texto(operacion.CreadaEnUtc));
        var secuencia = (long)(await comando.ExecuteScalarAsync(ct))!;

        Notificar();
        return operacion with { Secuencia = secuencia };
    }

    public async Task<OperacionPendiente?> ObtenerAsync(Guid operationId, CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = $"SELECT {Columnas} FROM ColaOperaciones WHERE OperationId = $id";
        comando.Parameters.AddWithValue("$id", operationId.ToString("D"));
        await using var lector = await comando.ExecuteReaderAsync(ct);
        return await lector.ReadAsync(ct) ? Leer(lector) : null;
    }

    public async Task<IReadOnlyList<OperacionPendiente>> ListarAsync(
        EstadoOperacion? estado = null, string? modulo = null, CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText =
            $"SELECT {Columnas} FROM ColaOperaciones " +
            "WHERE ($estado IS NULL OR Estado = $estado) AND ($modulo IS NULL OR Modulo = $modulo) " +
            "ORDER BY Secuencia";
        comando.Parameters.AddWithValue("$estado", estado is { } e ? e.ToString() : DBNull.Value);
        comando.Parameters.AddWithValue("$modulo", (object?)modulo ?? DBNull.Value);

        var resultado = new List<OperacionPendiente>();
        await using var lector = await comando.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            resultado.Add(Leer(lector));
        }

        return resultado;
    }

    public async Task<int> ContarAsync(EstadoOperacion estado, CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT COUNT(*) FROM ColaOperaciones WHERE Estado = $estado";
        comando.Parameters.AddWithValue("$estado", estado.ToString());
        return (int)(long)(await comando.ExecuteScalarAsync(ct))!;
    }

    public Task MarcarSincronizandoAsync(Guid operationId, CancellationToken ct = default) =>
        TransicionarAsync(operationId, EstadoOperacion.PendienteSync, EstadoOperacion.Sincronizando,
            "Intentos = Intentos + 1", null, ct);

    public Task MarcarSincronizadaAsync(Guid operationId, CancellationToken ct = default) =>
        TransicionarAsync(operationId, EstadoOperacion.Sincronizando, EstadoOperacion.Sincronizado,
            "MotivoRechazo = NULL, TipoConflicto = NULL", null, ct);

    public Task DevolverAPendienteAsync(Guid operationId, string? motivo, CancellationToken ct = default) =>
        TransicionarAsync(operationId, EstadoOperacion.Sincronizando, EstadoOperacion.PendienteSync,
            "MotivoRechazo = $motivo", c => c.Parameters.AddWithValue("$motivo", (object?)motivo ?? DBNull.Value), ct);

    public Task RechazarAsync(Guid operationId, TipoConflicto? tipo, string motivo, CancellationToken ct = default) =>
        TransicionarAsync(operationId, EstadoOperacion.Sincronizando, EstadoOperacion.Rechazado,
            "TipoConflicto = $tipo, MotivoRechazo = $motivo",
            c =>
            {
                c.Parameters.AddWithValue("$tipo", tipo is { } t ? t.ToString() : DBNull.Value);
                c.Parameters.AddWithValue("$motivo", motivo);
            }, ct);

    public async Task<int> RecuperarInterrumpidasAsync(CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText =
            "UPDATE ColaOperaciones SET Estado = $pendiente, ActualizadaEnUtc = $ahora WHERE Estado = $sincronizando";
        comando.Parameters.AddWithValue("$pendiente", EstadoOperacion.PendienteSync.ToString());
        comando.Parameters.AddWithValue("$sincronizando", EstadoOperacion.Sincronizando.ToString());
        comando.Parameters.AddWithValue("$ahora", Texto(Ahora()));
        var filas = await comando.ExecuteNonQueryAsync(ct);
        if (filas > 0)
        {
            Notificar();
        }

        return filas;
    }

    public async Task<OperacionPendiente> ReintentarCorregidaAsync(
        Guid operationIdRechazada, string nuevoPayloadJson, CancellationToken ct = default)
    {
        var rechazada = await ObtenerAsync(operationIdRechazada, ct)
            ?? throw new InvalidOperationException("No existe esa acción en la cola.");

        if (rechazada.Estado != EstadoOperacion.Rechazado || rechazada.TipoConflicto != TipoConflicto.A)
        {
            throw new InvalidOperationException(
                "Solo una acción rechazada por un conflicto de tipo A se puede corregir y reintentar. " +
                "Un conflicto B lo decidió el servidor y un C pide revisión.");
        }

        return await EncolarAsync(rechazada.Modulo, rechazada.Tipo, nuevoPayloadJson, ct);
    }

    public async Task<DateTime?> UltimaSincronizacionAsync(CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT Valor FROM Metadatos WHERE Clave = 'UltimaSincronizacionUtc'";
        return await comando.ExecuteScalarAsync(ct) is string texto ? Fecha(texto) : null;
    }

    public async Task RegistrarSincronizacionAsync(DateTime utc, CancellationToken ct = default)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText =
            "INSERT INTO Metadatos (Clave, Valor) VALUES ('UltimaSincronizacionUtc', $valor) " +
            "ON CONFLICT(Clave) DO UPDATE SET Valor = excluded.Valor";
        comando.Parameters.AddWithValue("$valor", Texto(utc));
        await comando.ExecuteNonQueryAsync(ct);
        Notificar();
    }

    /// <summary>Cambia de estado solo si la acción está en el estado esperado: dos hilos no pisan la misma fila.</summary>
    private async Task TransicionarAsync(
        Guid operationId, EstadoOperacion desde, EstadoOperacion hacia,
        string asignacionesExtra, Action<SqliteCommand>? parametros, CancellationToken ct)
    {
        await using var conexion = await AbrirAsync(ct);
        await using var comando = conexion.CreateCommand();
        comando.CommandText =
            $"UPDATE ColaOperaciones SET Estado = $hacia, ActualizadaEnUtc = $ahora, {asignacionesExtra} " +
            "WHERE OperationId = $id AND Estado = $desde";
        comando.Parameters.AddWithValue("$hacia", hacia.ToString());
        comando.Parameters.AddWithValue("$desde", desde.ToString());
        comando.Parameters.AddWithValue("$ahora", Texto(Ahora()));
        comando.Parameters.AddWithValue("$id", operationId.ToString("D"));
        parametros?.Invoke(comando);

        if (await comando.ExecuteNonQueryAsync(ct) == 0)
        {
            throw new InvalidOperationException(
                $"La acción {operationId} no estaba en {desde}; no se puede pasar a {hacia}.");
        }

        Notificar();
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
            CREATE TABLE IF NOT EXISTS ColaOperaciones (
                Secuencia INTEGER PRIMARY KEY AUTOINCREMENT,
                OperationId TEXT NOT NULL UNIQUE,
                Modulo TEXT NOT NULL,
                Tipo TEXT NOT NULL,
                Payload TEXT NOT NULL,
                Estado TEXT NOT NULL,
                TipoConflicto TEXT NULL,
                MotivoRechazo TEXT NULL,
                Intentos INTEGER NOT NULL DEFAULT 0,
                CreadaEnUtc TEXT NOT NULL,
                ActualizadaEnUtc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ColaOperaciones_Estado ON ColaOperaciones (Estado, Secuencia);
            CREATE TABLE IF NOT EXISTS Metadatos (Clave TEXT PRIMARY KEY, Valor TEXT NOT NULL);
            """;
        await comando.ExecuteNonQueryAsync(ct);
    }

    private static OperacionPendiente Leer(SqliteDataReader r) => new()
    {
        Secuencia = r.GetInt64(0),
        OperationId = Guid.Parse(r.GetString(1)),
        Modulo = r.GetString(2),
        Tipo = r.GetString(3),
        Payload = r.GetString(4),
        Estado = Enum.Parse<EstadoOperacion>(r.GetString(5)),
        TipoConflicto = r.IsDBNull(6) ? null : Enum.Parse<TipoConflicto>(r.GetString(6)),
        MotivoRechazo = r.IsDBNull(7) ? null : r.GetString(7),
        Intentos = r.GetInt32(8),
        CreadaEnUtc = Fecha(r.GetString(9)),
        ActualizadaEnUtc = r.IsDBNull(10) ? null : Fecha(r.GetString(10))
    };

    private DateTime Ahora() => _reloj.GetUtcNow().UtcDateTime;

    private static string Texto(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime Fecha(string texto) =>
        DateTime.Parse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private void Notificar() => Cambio?.Invoke(this, EventArgs.Empty);
}
