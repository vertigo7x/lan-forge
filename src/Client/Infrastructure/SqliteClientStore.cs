using System.Globalization;
using LanForge.Client.Application;
using LanForge.Client.Domain;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using Microsoft.Data.Sqlite;

namespace LanForge.Client.Infrastructure;

/// <summary>Persistencia SQLite del cliente: clientId, confianza, catálogo, instalados y operaciones.</summary>
public sealed class SqliteClientStore : IClientStore, IDisposable
{
    private const string DateFormat = "O";

    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteClientStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        ExecuteSchema();
    }

    public async Task<string> GetOrCreateClientIdAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM client_meta WHERE key = 'client_id';";
        var existing = await command.ExecuteScalarAsync(cancellationToken);
        if (existing is string clientId)
        {
            return clientId;
        }

        var created = Guid.NewGuid().ToString("n");
        await SetMetaAsync("client_id", created, cancellationToken);
        return created;
    }

    public async Task<ServerTrustInfo?> GetServerTrustAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT server_id, fingerprint, base_url, pinned_utc FROM server_trust WHERE single_row = 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ServerTrustInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            ParseDate(reader.GetString(3)));
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT value FROM client_meta WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    public async Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await SetMetaAsync(key, value, cancellationToken);
    }

    public async Task SaveServerTrustAsync(ServerTrustInfo trust, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO server_trust (single_row, server_id, fingerprint, base_url, pinned_utc)
                VALUES (1, $id, $fingerprint, $url, $pinned)
                ON CONFLICT(single_row) DO UPDATE SET
                    server_id = $id, fingerprint = $fingerprint, base_url = $url, pinned_utc = $pinned;
                """;
            command.Parameters.AddWithValue("$id", trust.ServerId);
            command.Parameters.AddWithValue("$fingerprint", trust.Fingerprint);
            command.Parameters.AddWithValue("$url", trust.BaseUrl);
            command.Parameters.AddWithValue("$pinned", FormatDate(trust.PinnedUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ClearServerTrustAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM server_trust;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<CatalogEntry>();
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            SELECT game_id, title, package_id, version, total_size_bytes, file_count, cover_path
            FROM catalog
            ORDER BY title;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new CatalogEntry(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt64(4), reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return entries;
    }

    public async Task ReplaceCatalogAsync(IEnumerable<CatalogEntry> entries, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using (var delete = _connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM catalog;";
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var entry in entries)
            {
                await using var insert = _connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO catalog (
                        game_id, title, package_id, version, total_size_bytes, file_count, cover_path, synced_utc)
                    VALUES ($game, $title, $package, $version, $size, $count, $cover, $synced);
                    """;
                insert.Parameters.AddWithValue("$game", entry.GameId);
                insert.Parameters.AddWithValue("$title", entry.Title);
                insert.Parameters.AddWithValue("$package", entry.PackageId);
                insert.Parameters.AddWithValue("$version", entry.Version);
                insert.Parameters.AddWithValue("$size", entry.TotalSizeBytes);
                insert.Parameters.AddWithValue("$count", entry.FileCount);
                insert.Parameters.AddWithValue("$cover", (object?)entry.CoverPath ?? DBNull.Value);
                insert.Parameters.AddWithValue("$synced", FormatDate(DateTimeOffset.UtcNow));
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<InstalledGame?> GetInstalledAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var all = await ListInstalledAsync(cancellationToken);
        return all.FirstOrDefault(g => string.Equals(g.GameId, gameId, StringComparison.Ordinal));
    }

    public async Task<IReadOnlyList<InstalledGame>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var games = new List<InstalledGame>();
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT game_id, package_id, version, install_path, entry_point, arguments, installed_utc FROM installed ORDER BY game_id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            games.Add(new InstalledGame(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                ParseDate(reader.GetString(6))));
        }

        return games;
    }

    public async Task SaveInstalledAsync(InstalledGame game, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO installed (game_id, package_id, version, install_path, entry_point, arguments, installed_utc)
                VALUES ($game, $package, $version, $path, $entry, $args, $installed)
                ON CONFLICT(game_id) DO UPDATE SET
                    package_id = $package, version = $version, install_path = $path,
                    entry_point = $entry, arguments = $args, installed_utc = $installed;
                """;
            command.Parameters.AddWithValue("$game", game.GameId);
            command.Parameters.AddWithValue("$package", game.PackageId);
            command.Parameters.AddWithValue("$version", game.Version);
            command.Parameters.AddWithValue("$path", game.InstallPath);
            command.Parameters.AddWithValue("$entry", game.EntryPoint);
            command.Parameters.AddWithValue("$args", (object?)game.Arguments ?? DBNull.Value);
            command.Parameters.AddWithValue("$installed", FormatDate(game.InstalledUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RemoveInstalledAsync(string gameId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM installed WHERE game_id = $game;";
            command.Parameters.AddWithValue("$game", gameId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RecordOperationAsync(ClientOperation operation, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO operations (operation_id, type, status, started_utc, finished_utc, detail)
                VALUES ($id, $type, $status, $started, $finished, $detail)
                ON CONFLICT(operation_id) DO UPDATE SET
                    status = $status, finished_utc = $finished, detail = $detail;
                """;
            command.Parameters.AddWithValue("$id", operation.OperationId);
            command.Parameters.AddWithValue("$type", operation.Type);
            command.Parameters.AddWithValue("$status", operation.Status.ToString());
            command.Parameters.AddWithValue("$started", FormatDate(operation.StartedUtc));
            command.Parameters.AddWithValue("$finished", operation.FinishedUtc is null ? DBNull.Value : FormatDate(operation.FinishedUtc.Value));
            command.Parameters.AddWithValue("$detail", (object?)operation.Detail ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<ClientOperation>> ListOperationsAsync(CancellationToken cancellationToken = default)
    {
        var operations = new List<ClientOperation>();
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT operation_id, type, status, started_utc, finished_utc, detail FROM operations ORDER BY started_utc DESC LIMIT 200;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            operations.Add(new ClientOperation(
                reader.GetString(0), reader.GetString(1),
                Enum.Parse<ClientOperationStatus>(reader.GetString(2)),
                ParseDate(reader.GetString(3)),
                reader.IsDBNull(4) ? null : ParseDate(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return operations;
    }

    public void Dispose() => _connection.Dispose();

    private async Task SetMetaAsync(string key, string value, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "INSERT INTO client_meta (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = $value;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void ExecuteSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS client_meta (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS server_trust (
                single_row INTEGER PRIMARY KEY CHECK (single_row = 1),
                server_id  TEXT NOT NULL,
                fingerprint TEXT NOT NULL,
                base_url   TEXT NOT NULL,
                pinned_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS catalog (
                game_id          TEXT PRIMARY KEY,
                title            TEXT NOT NULL,
                package_id       TEXT NOT NULL,
                version          TEXT NOT NULL,
                total_size_bytes INTEGER NOT NULL,
                file_count       INTEGER NOT NULL,
                cover_path       TEXT NULL,
                synced_utc       TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS installed (
                game_id       TEXT PRIMARY KEY,
                package_id    TEXT NOT NULL,
                version       TEXT NOT NULL,
                install_path  TEXT NOT NULL,
                entry_point   TEXT NOT NULL,
                arguments     TEXT NULL,
                installed_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS operations (
                operation_id TEXT PRIMARY KEY,
                type         TEXT NOT NULL,
                status       TEXT NOT NULL,
                started_utc  TEXT NOT NULL,
                finished_utc TEXT NULL,
                detail       TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn("catalog", "cover_path", "TEXT NULL");
    }

    /// <summary>Aplicar columnas añadidas sin invalidar el catálogo ya sincronizado.</summary>
    private void EnsureColumn(string table, string column, string definition)
    {
        using var check = _connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $name;";
        check.Parameters.AddWithValue("$name", column);
        var exists = (long)(check.ExecuteScalar() ?? 0) > 0;
        if (!exists)
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
            alter.ExecuteNonQuery();
        }
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.ParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
