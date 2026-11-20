using LanForge.Client.Application;
using LanForge.Client.Domain;
using LanForge.Client.Infrastructure;
using LanForge.Shared.Models;

namespace LanForge.UnitTests.Client;

public sealed class SqliteClientStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private SqliteClientStore CreateStore() =>
        new(Path.Combine(_temp.Path, $"client-{Guid.NewGuid():n}.db"));

    [Fact]
    public async Task ClientId_StableAcrossCalls()
    {
        using var store = CreateStore();

        var first = await store.GetOrCreateClientIdAsync();
        var second = await store.GetOrCreateClientIdAsync();

        Assert.Equal(first, second);
        Assert.False(string.IsNullOrWhiteSpace(first));
    }

    [Fact]
    public async Task Trust_RoundTrip_AndClear()
    {
        using var store = CreateStore();
        var trust = new ServerTrustInfo("server-1", "abcdef", "http://10.0.0.5:8710", DateTimeOffset.UtcNow);

        await store.SaveServerTrustAsync(trust);
        var loaded = await store.GetServerTrustAsync();

        Assert.NotNull(loaded);
        Assert.Equal(trust.Fingerprint, loaded.Fingerprint);
        Assert.Equal(trust.ServerId, loaded.ServerId);
        Assert.Equal(trust.BaseUrl, loaded.BaseUrl);

        await store.ClearServerTrustAsync();
        Assert.Null(await store.GetServerTrustAsync());
    }

    [Fact]
    public async Task Catalog_Replace_ReplacesAll()
    {
        using var store = CreateStore();
        await store.ReplaceCatalogAsync([new CatalogEntry("a", "A", "a", "1.0", 1, 1)]);

        await store.ReplaceCatalogAsync(
        [
            new CatalogEntry("b", "B", "b", "2.0", 2, 2),
            new CatalogEntry("c", "C", "c", "3.0", 3, 3),
        ]);

        var entries = await store.GetCatalogAsync();
        Assert.Equal(2, entries.Count);
        Assert.DoesNotContain(entries, e => e.GameId == "a");
    }

    [Fact]
    public async Task Catalog_Replace_ConservaLaReferenciaDePortada()
    {
        using var store = CreateStore();
        await store.ReplaceCatalogAsync(
        [
            new CatalogEntry("quake3", "Quake III Arena", "quake3", "1.32", 1, 1,
                "quake3-aabbcc.png"),
        ]);

        var entry = Assert.Single(await store.GetCatalogAsync());

        Assert.Equal("quake3-aabbcc.png", entry.CoverPath);
    }

    [Fact]
    public async Task Installed_SaveGetRemove()
    {
        using var store = CreateStore();
        var game = new InstalledGame("g", "g", "1.0", "/games/g/1.0", "run.exe", "-arg", DateTimeOffset.UtcNow);

        await store.SaveInstalledAsync(game);
        Assert.Equal(game, await store.GetInstalledAsync("g"));

        await store.RemoveInstalledAsync("g");
        Assert.Null(await store.GetInstalledAsync("g"));
    }

    [Fact]
    public async Task Settings_RoundTrip()
    {
        using var store = CreateStore();

        Assert.Null(await store.GetSettingAsync("player_name"));

        await store.SaveSettingAsync("player_name", "DaniPC");
        await store.SaveSettingAsync("confirm_uninstall", "false");

        Assert.Equal("DaniPC", await store.GetSettingAsync("player_name"));
        Assert.Equal("false", await store.GetSettingAsync("confirm_uninstall"));
    }

    [Fact]
    public async Task Operations_Recorded()
    {
        using var store = CreateStore();
        var started = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        await store.RecordOperationAsync(new ClientOperation(
            "op-1", "game.install", ClientOperationStatus.Completed, started, started.AddMinutes(1), "quake3"));

        var operations = await store.ListOperationsAsync();
        var op = Assert.Single(operations);
        Assert.Equal(ClientOperationStatus.Completed, op.Status);
        Assert.Equal("quake3", op.Detail);
    }
}
