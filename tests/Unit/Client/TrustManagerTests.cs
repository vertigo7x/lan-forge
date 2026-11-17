using LanForge.Client.Application;
using LanForge.Shared.Protocol;
using LanForge.Shared.Security;

namespace LanForge.UnitTests.Client;

public sealed class TrustManagerTests
{
    private static HelloResponse HelloFor(KeyPair keys, string serverId = "server-1") =>
        new(ProtocolVersion.Current, Fingerprint.FromPublicKey(keys.PublicKey).ToString(), "Servidor", serverId);

    [Fact]
    public async Task Check_WithoutPin_ReturnsNewServer()
    {
        var manager = new TrustManager(new Fakes.InMemoryClientStore());

        var check = await manager.CheckAsync(HelloFor(Ed25519.GenerateKeyPair()));

        Assert.Equal(TrustState.NewServer, check.State);
        Assert.Null(check.Pinned);
    }

    [Fact]
    public async Task Pin_ThenCheck_ReturnsTrusted()
    {
        var store = new Fakes.InMemoryClientStore();
        var manager = new TrustManager(store);
        var keys = Ed25519.GenerateKeyPair();
        var hello = HelloFor(keys);

        await manager.PinAsync(hello, "http://10.0.0.5:8710");
        var check = await manager.CheckAsync(hello);

        Assert.Equal(TrustState.Trusted, check.State);
        Assert.Equal(check.Presented, check.Pinned);
        var pinned = await store.GetServerTrustAsync();
        Assert.Equal("http://10.0.0.5:8710", pinned!.BaseUrl);
    }

    [Fact]
    public async Task Check_DifferentFingerprint_ReturnsMismatch()
    {
        var manager = new TrustManager(new Fakes.InMemoryClientStore());
        var originalKeys = Ed25519.GenerateKeyPair();
        await manager.PinAsync(HelloFor(originalKeys), "http://10.0.0.5:8710");

        var check = await manager.CheckAsync(HelloFor(Ed25519.GenerateKeyPair()));

        Assert.Equal(TrustState.FingerprintMismatch, check.State);
        Assert.NotEqual(check.Pinned, check.Presented);
    }

    [Fact]
    public async Task Check_DifferentServerId_ReturnsMismatch()
    {
        var manager = new TrustManager(new Fakes.InMemoryClientStore());
        var keys = Ed25519.GenerateKeyPair();
        await manager.PinAsync(HelloFor(keys), "http://10.0.0.5:8710");

        var check = await manager.CheckAsync(HelloFor(keys, serverId: "otro-servidor"));

        Assert.Equal(TrustState.FingerprintMismatch, check.State);
    }

    [Fact]
    public async Task GetTrustedFingerprint_WithoutPin_Throws()
    {
        var manager = new TrustManager(new Fakes.InMemoryClientStore());

        await Assert.ThrowsAsync<LanForgeClientException>(() => manager.GetTrustedFingerprintAsync());
    }
}
