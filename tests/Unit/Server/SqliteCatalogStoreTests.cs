using LanForge.Server.Domain;
using LanForge.Server.Infrastructure;
using LanForge.Shared.Manifest;
using LanForge.Shared.Security;

namespace LanForge.UnitTests.Server;

public sealed class SqliteCatalogStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private SqliteCatalogStore CreateStore() =>
        new(Path.Combine(_temp.Path, $"test-{Guid.NewGuid():n}.db"));

    private static Package CreatePackage(string gameId, string version)
    {
        var keys = Ed25519.GenerateKeyPair();
        var payload = PackageManifestPayload.Create(
            gameId, gameId, $"Title of {gameId}", version,
            new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero),
            Convert.ToBase64String(keys.PublicKey),
            [ManifestFileEntry.Create("run.exe", new string('a', 64), 10)],
            entryPoint: "run.exe");
        var manifest = ManifestSigner.Sign(payload, keys);
        return new Package
        {
            PackageId = gameId,
            GameId = gameId,
            Version = version,
            EntryPoint = "run.exe",
            Arguments = null,
            TotalSizeBytes = 10,
            FileCount = 1,
            CreatedUtc = manifest.CreatedUtc,
            Manifest = manifest,
        };
    }

    private async Task<Package> AddPackageWithGameAsync(SqliteCatalogStore store, Package package)
    {
        await store.UpsertGameAsync(new Game(package.GameId, $"Title of {package.GameId}"));
        await store.AddPackageAsync(package);
        return package;
    }

    [Fact]
    public async Task Package_RoundTrip_PreservesManifest()
    {
        using var store = CreateStore();
        var package = await AddPackageWithGameAsync(store, CreatePackage("quake3", "1.32"));

        var loaded = await store.GetPackageAsync("quake3");

        Assert.NotNull(loaded);
        Assert.Equal(package.Version, loaded.Version);
        Assert.Equal(package.TotalSizeBytes, loaded.TotalSizeBytes);
        Assert.True(ManifestVerifier.Verify(loaded.Manifest));
    }

    [Fact]
    public async Task AddPackage_Duplicate_Throws()
    {
        using var store = CreateStore();
        await AddPackageWithGameAsync(store, CreatePackage("game", "1.0"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddPackageAsync(CreatePackage("game", "1.0")));
    }

    [Fact]
    public async Task ReplacePackage_UpdatesManifest()
    {
        using var store = CreateStore();
        await AddPackageWithGameAsync(store, CreatePackage("game", "1.0"));

        await store.ReplacePackageAsync(CreatePackage("game", "1.1"));

        var loaded = await store.GetPackageAsync("game");
        Assert.NotNull(loaded);
        Assert.Equal("1.1", loaded.Version);
    }

    [Fact]
    public async Task Game_Upsert_RoundTrip()
    {
        using var store = CreateStore();
        await store.UpsertGameAsync(new Game("game", "Game", "desc"));
        await store.UpsertGameAsync(new Game("game", "Game v2", "desc"));

        var game = await store.GetGameAsync("game");
        Assert.NotNull(game);
        Assert.Equal("Game v2", game.Title);
        Assert.Equal("desc", game.Description);
        Assert.Single(await store.ListGamesAsync());
    }

    [Fact]
    public async Task Game_Rejected_InvalidId()
    {
        using var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertGameAsync(new Game("bad/id", "Bad")));
    }

    [Fact]
    public async Task SeenClient_Upsert_KeepsLatest()
    {
        using var store = CreateStore();
        var first = new SeenClient("client-1", new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero), "10.0.0.1");
        var second = new SeenClient("client-1", new DateTimeOffset(2026, 11, 16, 11, 0, 0, TimeSpan.Zero), "10.0.0.2");

        await store.UpsertSeenClientAsync(first);
        await store.UpsertSeenClientAsync(second);

        var clients = await store.ListSeenClientsAsync();
        var client = Assert.Single(clients);
        Assert.Equal("10.0.0.2", client.IpAddress);
        Assert.Equal(second.LastSeenUtc, client.LastSeenUtc);
    }

    [Fact]
    public async Task Operation_Recorded_AndListed()
    {
        using var store = CreateStore();
        var started = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        var operation = new ServerOperation(
            "op-1", "client-1", "package.download",
            OperationStatus.Completed, started, started.AddSeconds(3), "quake3");

        await store.RecordOperationAsync(operation);

        var operations = await store.ListOperationsAsync();
        var loaded = Assert.Single(operations);
        Assert.Equal("op-1", loaded.OperationId);
        Assert.Equal(OperationStatus.Completed, loaded.Status);
        Assert.Equal("quake3", loaded.Detail);
        Assert.Equal(started.AddSeconds(3), loaded.FinishedUtc);
    }

    [Fact]
    public async Task ListOperations_DevuelveLasMasRecientesYAcotadas()
    {
        using var store = CreateStore();
        var baseTime = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

        // 250 operaciones: la tabla recibe una fila por cada sincronizacion y descarga.
        for (var i = 0; i < 250; i++)
        {
            await store.RecordOperationAsync(new ServerOperation(
                $"op-{i:D3}", "client-1", "catalog.sync",
                OperationStatus.Completed, baseTime.AddSeconds(i), baseTime.AddSeconds(i), $"n={i}"));
        }

        var limited = await store.ListOperationsAsync(100);

        Assert.Equal(100, limited.Count);
        // De la mas reciente a la mas antigua.
        Assert.Equal("op-249", limited[0].OperationId);
        Assert.Equal("op-150", limited[^1].OperationId);
    }

    [Fact]
    public async Task ListOperations_ElLimiteAcotaLoQueVuelve()
    {
        using var store = CreateStore();
        var baseTime = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 10; i++)
        {
            await store.RecordOperationAsync(new ServerOperation(
                $"op-{i}", "client-1", "catalog.sync",
                OperationStatus.Completed, baseTime.AddSeconds(i), baseTime.AddSeconds(i), null));
        }

        Assert.Equal(3, (await store.ListOperationsAsync(3)).Count);
        Assert.Equal(10, (await store.ListOperationsAsync()).Count);
    }

    [Fact]
    public async Task PruneOperations_BorraLasAntiguasYConservaLasRecientes()
    {
        using var store = CreateStore();
        var baseTime = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        await store.RecordOperationAsync(new ServerOperation(
            "old", "client-1", "catalog.sync", OperationStatus.Completed, baseTime, baseTime, null));
        await store.RecordOperationAsync(new ServerOperation(
            "new", "client-1", "catalog.sync", OperationStatus.Completed, baseTime.AddHours(1), baseTime.AddHours(1), null));

        var pruned = await store.PruneOperationsAsync(baseTime.AddMinutes(30));

        Assert.Equal(1, pruned);
        var remaining = Assert.Single(await store.ListOperationsAsync());
        Assert.Equal("new", remaining.OperationId);
    }

    [Fact]
    public async Task PruneOperations_NoBorraNadaSiNoHayAntiguas()
    {
        using var store = CreateStore();
        var baseTime = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
        await store.RecordOperationAsync(new ServerOperation(
            "op-1", "client-1", "catalog.sync", OperationStatus.Completed, baseTime, baseTime, null));

        Assert.Equal(0, await store.PruneOperationsAsync(baseTime.AddHours(-1)));
        Assert.Single(await store.ListOperationsAsync());
    }

    [Fact]
    public async Task Operations_TienenIndicePorFechaDeInicio()
    {
        // Sin indice por started_utc, la lectura acotada y la poda tienen que ordenar
        // toda la tabla en cada consulta.
        var databasePath = Path.Combine(_temp.Path, $"index-{Guid.NewGuid():n}.db");
        using (var store = new SqliteCatalogStore(databasePath))
        {
            await store.RecordOperationAsync(new ServerOperation(
                "op-1", "client-1", "catalog.sync", OperationStatus.Completed,
                new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 16, 10, 0, 1, TimeSpan.Zero), null));
        }

        var indexes = new List<string>();
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'operations';";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0));
            }
        }
        finally
        {
            connection.Dispose();
        }

        Assert.Contains("ix_operations_started_utc", indexes);
    }
}
