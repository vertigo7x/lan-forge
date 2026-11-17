using System.Text;
using LanForge.Shared.Manifest;
using LanForge.Shared.Security;

namespace LanForge.UnitTests;

/// <summary>Fábrica de manifiestos firmados para tests del cliente.</summary>
public static class ManifestTestHelper
{
    public static (SignedPackageManifest Manifest, KeyPair Keys, Dictionary<string, byte[]> Files) Create(
        string gameId = "game",
        string version = "1.0",
        params (string Path, string Content)[] files)
    {
        if (files.Length == 0)
        {
            files = [("a.txt", "content")];
        }

        var keys = Ed25519.GenerateKeyPair();
        var entries = files
            .Select(f => ManifestFileEntry.Create(
                f.Path,
                Sha256Hash.ComputeHex(Encoding.UTF8.GetBytes(f.Content)),
                Encoding.UTF8.GetBytes(f.Content).Length))
            .ToArray();
        var payload = PackageManifestPayload.Create(
            gameId, gameId, $"Title of {gameId}", version, DateTimeOffset.UtcNow,
            Convert.ToBase64String(keys.PublicKey), entries, files[0].Path);
        var manifest = ManifestSigner.Sign(payload, keys);
        var content = files.ToDictionary(f => f.Path, f => Encoding.UTF8.GetBytes(f.Content));
        return (manifest, keys, content);
    }
}
