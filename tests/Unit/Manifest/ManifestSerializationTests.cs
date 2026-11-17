using LanForge.Shared;
using LanForge.Shared.Manifest;
using LanForge.Shared.Security;

namespace LanForge.UnitTests.Manifest;

public sealed class ManifestSerializationTests
{
    private static SignedPackageManifest CreateSignedManifest()
    {
        var keys = Ed25519.GenerateKeyPair();
        var payload = PackageManifestPayload.Create(
            "warcraft3", "warcraft3", "Warcraft III", "1.27a",
            new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            Convert.ToBase64String(keys.PublicKey),
            [ManifestFileEntry.Create("war3.exe", new string('f', 64), 512)],
            entryPoint: "war3.exe");
        return ManifestSigner.Sign(payload, keys);
    }

    [Fact]
    public void CanonicalBytes_AreDeterministicAcrossInstances()
    {
        var keys = Ed25519.GenerateKeyPair();
        var entry = ManifestFileEntry.Create("bin/run.exe", new string('a', 64), 42);
        PackageManifestPayload Factory() => PackageManifestPayload.Create(
            "id", "game", "Game", "1.0", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Convert.ToBase64String(keys.PublicKey), [entry], entryPoint: "bin/run.exe");

        var first = SharedJson.SerializeCanonical(Factory());
        var second = SharedJson.SerializeCanonical(Factory());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Json_RoundTrip_PreservesAllFields()
    {
        var manifest = CreateSignedManifest();
        var json = manifest.ToJson();

        var restored = SignedPackageManifest.FromJson(json);

        Assert.NotNull(restored);
        Assert.Equal(manifest.PackageId, restored.PackageId);
        Assert.Equal(manifest.Version, restored.Version);
        Assert.Equal(manifest.Files, restored.Files);
        Assert.Equal(manifest.Signature, restored.Signature);
    }

    [Fact]
    public void Verify_AfterJsonRoundTrip_StillTrue()
    {
        var manifest = CreateSignedManifest();

        var restored = SignedPackageManifest.FromJson(manifest.ToJsonBytes());

        Assert.NotNull(restored);
        Assert.True(ManifestVerifier.Verify(restored));
    }

    [Fact]
    public void Json_UsesCamelCase()
    {
        var json = CreateSignedManifest().ToJson();

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"entryPoint\"", json);
    }
}
