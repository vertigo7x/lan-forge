using System.Globalization;
using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Shared;
using LanForge.Shared.Manifest;
using Microsoft.Data.Sqlite;

namespace LanForge.Server.Infrastructure;

/// <summary>Persistencia SQLite del servidor: juegos, paquetes, clientes vistos y operaciones.</summary>
public sealed class SqliteCatalogStore : ICatalogStore, IDisposable
{
    private const string DateFormat = "O";

    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteCatalogStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        ExecuteSchema();
    }

    public async Task UpsertGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO games (game_id, title, description, cover_file) VALUES ($id, $title, $description, $cover)
                ON CONFLICT(game_id) DO UPDATE SET title = $title, description = $description, cover_file = $cover;
                """;
            command.Parameters.AddWithValue("$id", game.Id);
            command.Parameters.AddWithValue("$title", game.Title);
            command.Parameters.AddWithValue("$description", (object?)game.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("$cover", (object?)game.CoverFile ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<Game?> GetGameAsync(string gameId, CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT game_id, title, description, cover_file FROM games WHERE game_id = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadGame(reader) : null;
    }

    public async Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken cancellationToken = default)
    {
        var games = new List<Game>();
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT game_id, title, description, cover_file FROM games ORDER BY game_id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            games.Add(ReadGame(reader));
        }

        return games;
    }

    private static Game ReadGame(System.Data.Common.DbDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3));

    public async Task AddPackageAsync(Package package, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await InsertPackageAsync(package, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ReplacePackageAsync(Package package, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            await using (var delete = _connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM packages WHERE package_id = $id;";
                delete.Parameters.AddWithValue("$id", package.PackageId);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            await InsertPackageAsync(package, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteGameAsync(string gameId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
            var affected = 0;
            await using (var deletePackages = _connection.CreateCommand())
            {
                deletePackages.Transaction = transaction;
                deletePackages.CommandText = "DELETE FROM packages WHERE game_id = $id;";
                deletePackages.Parameters.AddWithValue("$id", gameId);
                affected += await deletePackages.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteGame = _connection.CreateCommand())
            {
                deleteGame.Transaction = transaction;
                deleteGame.CommandText = "DELETE FROM games WHERE game_id = $id;";
                deleteGame.Parameters.AddWithValue("$id", gameId);
                affected += await deleteGame.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return affected > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<Package?> GetPackageAsync(string packageId, CancellationToken cancellationToken = default)
    {
        await using var command = CreatePackageSelect("WHERE p.package_id = $id");
        command.Parameters.AddWithValue("$id", packageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPackage(reader) : null;
    }

    public async Task<IReadOnlyList<Package>> ListLatestPackagesAsync(CancellationToken cancellationToken = default)
    {
        var packages = new List<Package>();
        await using var command = CreatePackageSelect(string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            packages.Add(ReadPackage(reader));
        }

        return packages;
    }

    public async Task UpsertSeenClientAsync(SeenClient client, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO seen_clients (client_id, last_seen_utc, ip_address) VALUES ($id, $seen, $ip)
                ON CONFLICT(client_id) DO UPDATE SET last_seen_utc = $seen, ip_address = $ip;
                """;
            command.Parameters.AddWithValue("$id", client.ClientId);
            command.Parameters.AddWithValue("$seen", FormatDate(client.LastSeenUtc));
            command.Parameters.AddWithValue("$ip", (object?)client.IpAddress ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<SeenClient>> ListSeenClientsAsync(CancellationToken cancellationToken = default)
    {
        var clients = new List<SeenClient>();
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT client_id, last_seen_utc, ip_address FROM seen_clients ORDER BY client_id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            clients.Add(new SeenClient(
                reader.GetString(0),
                ParseDate(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return clients;
    }

    public async Task RecordOperationAsync(ServerOperation operation, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO operations (operation_id, client_id, type, status, started_utc, finished_utc, detail)
                VALUES ($id, $client, $type, $status, $started, $finished, $detail)
                ON CONFLICT(operation_id) DO UPDATE SET
                    status = $status, finished_utc = $finished, detail = $detail;
                """;
            command.Parameters.AddWithValue("$id", operation.OperationId);
            command.Parameters.AddWithValue("$client", operation.ClientId);
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

    public async Task<IReadOnlyList<ServerOperation>> ListOperationsAsync(CancellationToken cancellationToken = default)
    {
        var operations = new List<ServerOperation>();
        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT operation_id, client_id, type, status, started_utc, finished_utc, detail FROM operations ORDER BY started_utc;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            operations.Add(new ServerOperation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<OperationStatus>(reader.GetString(3)),
                ParseDate(reader.GetString(4)),
                reader.IsDBNull(5) ? null : ParseDate(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return operations;
    }

    public void Dispose() => _connection.Dispose();

    private SqliteCommand CreatePackageSelect(string whereClause)
    {
        var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT p.package_id, p.game_id, p.version, p.entry_point, p.arguments,
                   p.manifest_json, p.total_size_bytes, p.file_count, p.created_utc
            FROM packages p
            {whereClause}
            ORDER BY p.game_id;
            """;
        return command;
    }

    private async Task InsertPackageAsync(Package package, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO packages (package_id, game_id, version, entry_point, arguments,
                                  manifest_json, total_size_bytes, file_count, created_utc)
            VALUES ($id, $game, $version, $entry, $args, $manifest, $size, $count, $created);
            """;
        command.Parameters.AddWithValue("$id", package.PackageId);
        command.Parameters.AddWithValue("$game", package.GameId);
        command.Parameters.AddWithValue("$version", package.Version);
        command.Parameters.AddWithValue("$entry", package.EntryPoint);
        command.Parameters.AddWithValue("$args", (object?)package.Arguments ?? DBNull.Value);
        command.Parameters.AddWithValue("$manifest", package.Manifest.ToJson());
        command.Parameters.AddWithValue("$size", package.TotalSizeBytes);
        command.Parameters.AddWithValue("$count", package.FileCount);
        command.Parameters.AddWithValue("$created", FormatDate(package.CreatedUtc));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"El paquete '{package.PackageId}' ya existe.", ex);
        }
    }

    private static Package ReadPackage(SqliteDataReader reader)
    {
        var manifestJson = reader.GetString(5);
        var manifest = SignedPackageManifest.FromJson(manifestJson)
            ?? throw new InvalidDataException("manifest_json corrupto en la base de datos.");
        return new Package
        {
            PackageId = reader.GetString(0),
            GameId = reader.GetString(1),
            Version = reader.GetString(2),
            EntryPoint = reader.GetString(3),
            Arguments = reader.IsDBNull(4) ? null : reader.GetString(4),
            Manifest = manifest,
            TotalSizeBytes = reader.GetInt64(6),
            FileCount = reader.GetInt32(7),
            CreatedUtc = ParseDate(reader.GetString(8)),
        };
    }

    private void ExecuteSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS games (
                game_id     TEXT PRIMARY KEY,
                title       TEXT NOT NULL,
                description TEXT NULL,
                cover_file  TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS packages (
                package_id       TEXT PRIMARY KEY,
                game_id          TEXT NOT NULL REFERENCES games(game_id),
                version          TEXT NOT NULL,
                entry_point      TEXT NOT NULL,
                arguments        TEXT NULL,
                manifest_json    TEXT NOT NULL,
                total_size_bytes INTEGER NOT NULL,
                file_count       INTEGER NOT NULL,
                created_utc      TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS operations (
                operation_id TEXT PRIMARY KEY,
                client_id    TEXT NOT NULL,
                type         TEXT NOT NULL,
                status       TEXT NOT NULL,
                started_utc  TEXT NOT NULL,
                finished_utc TEXT NULL,
                detail       TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS seen_clients (
                client_id      TEXT PRIMARY KEY,
                last_seen_utc  TEXT NOT NULL,
                ip_address     TEXT NULL
            );
            """;
        command.ExecuteNonQuery();

        // Migración ligera: añadir columnas nuevas a bases de datos de versiones anteriores.
        EnsureColumn("games", "cover_file", "TEXT NULL");
    }

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
