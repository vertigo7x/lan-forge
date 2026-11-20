using LanForge.Client.Application;
using LanForge.Shared.Models;
using LanForge.Shared.Protocol;
using LanForge.Shared.Security;

namespace LanForge.UnitTests.Client;

public sealed class CatalogSyncServiceTests
{
    private sealed class FakeApi : IServerApi
    {
        public HelloResponse Hello { get; set; } = new(
            ProtocolVersion.Current,
            Fingerprint.FromPublicKey(Ed25519.GenerateKeyPair().PublicKey).ToString(),
            "Servidor LAN", "server-1");

        public List<CatalogEntry> Entries { get; } =
            [new("quake3", "Quake III", "quake3", "1.32", 100, 2)];

        public string BaseUrl => "http://10.0.0.5:8710";
        public string ClientId => "test";

        public Task<HelloResponse> GetHelloAsync(CancellationToken cancellationToken = default) => Task.FromResult(Hello);

        public Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogEntry>>(Entries);

        public Task<Shared.Manifest.SignedPackageManifest> GetManifestAsync(string packageId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenDownloadAsync(string packageId, string relativePath, long resumeFromBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenArchiveDownloadAsync(string packageId, long resumeFromBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> DownloadCoverAsync(string fileName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]?> DownloadLanPartyLogoAsync(string fileName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static (CatalogSyncService Service, FakeApi Api, Fakes.InMemoryClientStore Store) Create()
    {
        var store = new Fakes.InMemoryClientStore();
        var api = new FakeApi();
        var trust = new TrustManager(store);
        return (new CatalogSyncService(api, trust, store), api, store);
    }

    [Fact]
    public async Task Sync_NewServer_WithoutConfirmation_ThrowsTrustRequired()
    {
        var (service, _, _) = Create();

        var ex = await Assert.ThrowsAsync<TrustRequiredException>(() => service.SyncAsync(confirmNewServer: false));

        Assert.Contains("Confirma su fingerprint", ex.Message);
    }

    [Fact]
    public async Task Sync_NewServer_Confirmed_PinsAndReplacesCatalog()
    {
        var (service, _, store) = Create();

        var result = await service.SyncAsync(confirmNewServer: true);

        Assert.Equal(1, result.EntryCount);
        Assert.Null(result.LanPartyLogoPath);
        Assert.Single(await store.GetCatalogAsync());
        Assert.NotNull(await store.GetServerTrustAsync());
    }

    [Fact]
    public async Task Sync_PinnedTrustedServer_SucceedsSilently()
    {
        var (service, api, store) = Create();
        var trust = new TrustManager(store);
        await trust.PinAsync(api.Hello, api.BaseUrl);

        var result = await service.SyncAsync(confirmNewServer: false);

        Assert.Equal(1, result.EntryCount);
    }

    [Fact]
    public async Task Sync_ChangedFingerprint_ThrowsMismatch()
    {
        var (service, api, store) = Create();
        var trust = new TrustManager(store);
        await trust.PinAsync(api.Hello, api.BaseUrl);
        api.Hello = api.Hello with { Fingerprint = Fingerprint.FromPublicKey(Ed25519.GenerateKeyPair().PublicKey).ToString() };

        await Assert.ThrowsAsync<FingerprintMismatchException>(() => service.SyncAsync());
    }

    [Fact]
    public async Task Sync_ProtocolMismatch_Throws()
    {
        var (service, api, _) = Create();
        api.Hello = api.Hello with { ProtocolVersion = ProtocolVersion.Current + 1 };

        var ex = await Assert.ThrowsAsync<ProtocolMismatchException>(() => service.SyncAsync(confirmNewServer: true));
        Assert.Contains("protocolo", ex.Message);
    }
}
