using LanForge.Client.Application;
using LanForge.Client.Domain;
using LanForge.Shared.Protocol;
using LanForge.Shared.Security;
using LanForge.UnitTests.Client.Fakes;

namespace LanForge.UnitTests.Client;

public sealed class ClientSessionTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private async Task<(ClientSession Session, Fakes.InMemoryClientStore Store)> CreateSessionAsync(
        StubServerApi api, Fakes.InMemoryClientStore? store = null)
    {
        store ??= new Fakes.InMemoryClientStore();
        var trust = new TrustManager(store);
        var hello = await api.GetHelloAsync();
        await trust.PinAsync(hello, api.BaseUrl);
        var sync = new CatalogSyncService(api, trust, store);
        var session = new ClientSession(
            api, sync, trust, new PackageDownloader(api),
            new GameInstaller(store, Path.Combine(_temp.Path, "games")),
            new GameLauncher(new ThrowingProcessStarter()),
            store,
            Path.Combine(_temp.Path, "downloads"),
            clientId: "test-client");
        return (session, store);
    }

    [Fact]
    public async Task Install_FullFlow_InstallsAndCleansStaging()
    {
        var (manifest, keys, files) = ManifestTestHelper.Create("quake3", "1.32", ("run.exe", "bin"), ("data/pak.bin", "pak"));
        var api = new StubServerApi(manifest, files, identityKeys: keys);
        var (session, store) = await CreateSessionAsync(api);
        var progress = new List<DownloadProgress>();

        var installed = await session.InstallAsync("quake3", new Progress<DownloadProgress>(progress.Add));

        Assert.Equal("1.32", installed.Version);
        Assert.Equal("bin", File.ReadAllText(Path.Combine(installed.InstallPath, "run.exe")));
        Assert.False(Directory.Exists(Path.Combine(_temp.Path, "downloads", "quake3")), "El staging debe limpiarse tras instalar.");
        Assert.Contains(progress, p => p.Finished);

        var operations = await store.ListOperationsAsync();
        Assert.Contains(operations, o => o.Type == "game.install" && o.Status == ClientOperationStatus.Completed);
    }

    [Fact]
    public async Task Install_TamperedContent_ThrowsAndCleansStaging()
    {
        var (manifest, keys, files) = ManifestTestHelper.Create("game", "1.0", ("a.txt", "original"));
        files["a.txt"] = [9, 9, 9]; // contenido que no coincide con el hash del manifiesto
        var api = new StubServerApi(manifest, files, identityKeys: keys);
        var (session, store) = await CreateSessionAsync(api);

        await Assert.ThrowsAsync<PackageValidationException>(() => session.InstallAsync("game"));

        Assert.Null(await store.GetInstalledAsync("game"));
        var operations = await store.ListOperationsAsync();
        Assert.Contains(operations, o => o.Type == "game.install" && o.Status == ClientOperationStatus.Failed);
    }

    [Fact]
    public async Task Sync_FirstContact_RequiresConfirmation()
    {
        var (manifest, keys, files) = ManifestTestHelper.Create();
        var api = new StubServerApi(manifest, files, identityKeys: keys);
        var store = new Fakes.InMemoryClientStore();
        var trust = new TrustManager(store);
        var sync = new CatalogSyncService(api, trust, store);
        var session = new ClientSession(
            api, sync, trust, new PackageDownloader(api),
            new GameInstaller(store, Path.Combine(_temp.Path, "games")),
            new GameLauncher(new ThrowingProcessStarter()),
            store, Path.Combine(_temp.Path, "downloads"), "test-client");

        await Assert.ThrowsAsync<TrustRequiredException>(() => session.SyncAsync(confirmNewServer: false));

        var result = await session.SyncAsync(confirmNewServer: true);
        Assert.Equal(1, result.EntryCount);
    }

    private sealed class ThrowingProcessStarter : IProcessStarter
    {
        public void Start(string fileName, string? arguments, string workingDirectory) =>
            throw new InvalidOperationException("No se debe lanzar procesos en este test.");
    }
}
