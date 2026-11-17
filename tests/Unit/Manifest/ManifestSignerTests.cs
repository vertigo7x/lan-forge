using LanForge.Shared;
using LanForge.Shared.Manifest;
using LanForge.Shared.Security;

namespace LanForge.UnitTests.Manifest;

public sealed class ManifestSignerTests
{
    private static (PackageManifestPayload Payload, KeyPair Keys) CreatePayload(KeyPair? keys = null)
    {
        keys ??= Ed25519.GenerateKeyPair();
        var payload = PackageManifestPayload.Create(
            packageId: "quake3",
            gameId: "quake3",
            gameTitle: "Quake III Arena",
            version: "1.32",
            createdUtc: new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            publicKeyBase64: Convert.ToBase64String(keys.PublicKey),
            files:
            [
                ManifestFileEntry.Create("game.exe", new string('a', 64), 1024),
                ManifestFileEntry.Create("data/pak0.pk3", new string('b', 64), 2048),
            ],
            entryPoint: "game.exe",
            arguments: "+set fs_game baseq3");
        return (payload, keys);
    }

    [Fact]
    public void Sign_ThenVerify_ReturnsTrue()
    {
        var (payload, keys) = CreatePayload();

        var manifest = ManifestSigner.Sign(payload, keys);

        Assert.True(ManifestVerifier.Verify(manifest));
    }

    [Fact]
    public void Verify_TamperedContent_ReturnsFalse()
    {
        var (payload, keys) = CreatePayload();
        var manifest = ManifestSigner.Sign(payload, keys);

        var tampered = manifest with { GameTitle = "Quake III Arena (pirateada)" };

        Assert.False(ManifestVerifier.Verify(tampered));
    }

    [Fact]
    public void Verify_ReorderedFiles_ReturnsFalse()
    {
        var (payload, keys) = CreatePayload();
        var manifest = ManifestSigner.Sign(payload, keys);

        var tampered = manifest with { Files = manifest.Files.Reverse().ToArray() };

        Assert.False(ManifestVerifier.Verify(tampered));
    }

    [Fact]
    public void Verify_SignatureFromOtherKey_ReturnsFalse()
    {
        var (payload, _) = CreatePayload();            // payload que afirma la clave A
        var attackerKeys = Ed25519.GenerateKeyPair();  // clave B
        var canonical = SharedJson.SerializeCanonical(payload);
        var fakeSignature = Ed25519.Sign(attackerKeys.PrivateKey, canonical);
        var manifest = SignedPackageManifest.FromPayload(payload, Convert.ToBase64String(fakeSignature));

        Assert.False(ManifestVerifier.Verify(manifest));
    }

    [Fact]
    public void Sign_PayloadPublicKeyMismatch_Throws()
    {
        var (payload, _) = CreatePayload();
        var otherKeys = Ed25519.GenerateKeyPair();

        Assert.Throws<ArgumentException>(() => ManifestSigner.Sign(payload, otherKeys));
    }

    [Fact]
    public void Verify_GarbageBase64_ReturnsFalse()
    {
        var (payload, keys) = CreatePayload();
        var manifest = ManifestSigner.Sign(payload, keys) with { Signature = "!!!not-base64!!!" };

        Assert.False(ManifestVerifier.Verify(manifest));
    }

    [Fact]
    public void MatchesFingerprint_CorrectKey_ReturnsTrue()
    {
        var (payload, keys) = CreatePayload();
        var manifest = ManifestSigner.Sign(payload, keys);
        var fingerprint = Fingerprint.FromPublicKey(keys.PublicKey);

        Assert.True(ManifestVerifier.MatchesFingerprint(manifest, fingerprint));
    }

    [Fact]
    public void MatchesFingerprint_OtherFingerprint_ReturnsFalse()
    {
        var (payload, keys) = CreatePayload();
        var manifest = ManifestSigner.Sign(payload, keys);
        var otherFingerprint = Fingerprint.FromPublicKey(Ed25519.GenerateKeyPair().PublicKey);

        Assert.False(ManifestVerifier.MatchesFingerprint(manifest, otherFingerprint));
    }

    [Fact]
    public void Create_EntryPointNotInFiles_Throws()
    {
        var keys = Ed25519.GenerateKeyPair();

        Assert.Throws<ArgumentException>(() => PackageManifestPayload.Create(
            "p", "g", "G", "1.0", DateTimeOffset.UtcNow, Convert.ToBase64String(keys.PublicKey),
            [ManifestFileEntry.Create("readme.txt", new string('c', 64), 10)],
            entryPoint: "game.exe"));
    }

    [Fact]
    public void Create_OrdersFilesByPath()
    {
        var keys = Ed25519.GenerateKeyPair();
        var payload = PackageManifestPayload.Create(
            "p", "g", "G", "1.0", DateTimeOffset.UtcNow, Convert.ToBase64String(keys.PublicKey),
            [
                ManifestFileEntry.Create("z/last.bin", new string('d', 64), 1),
                ManifestFileEntry.Create("a/first.bin", new string('e', 64), 1),
            ],
            entryPoint: "z/last.bin");

        Assert.Equal(["a/first.bin", "z/last.bin"], payload.Files.Select(f => f.Path).ToArray());
    }
}
