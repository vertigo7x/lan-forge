using LanForge.Client.Application;
using LanForge.Shared.Security;

namespace LanForge.UnitTests.Client;

public sealed class PackageValidatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string StageFiles(Dictionary<string, byte[]> files, string? subDirectory = null)
    {
        var staging = Path.Combine(_temp.Path, subDirectory ?? "staging");
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(staging, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content);
        }

        return staging;
    }

    [Fact]
    public async Task Validate_ValidStaging_Passes()
    {
        var (manifest, keys, files) = ManifestTestHelper.Create(files: [("a.txt", "hola"), ("dir/b.bin", "mundo")]);
        var staging = StageFiles(files);

        await PackageValidator.ValidateFilesAsync(manifest, staging);
        PackageValidator.ValidateSignature(manifest, Fingerprint.FromPublicKey(keys.PublicKey));
    }

    [Fact]
    public async Task Validate_TamperedFile_Throws()
    {
        var (manifest, keys, files) = ManifestTestHelper.Create(files: [("a.txt", "hola")]);
        files["a.txt"] = [1, 2, 3]; // alterado tras calcular hashes
        var staging = StageFiles(files);

        await Assert.ThrowsAsync<PackageValidationException>(() => PackageValidator.ValidateFilesAsync(manifest, staging));
    }

    [Fact]
    public async Task Validate_MissingFile_Throws()
    {
        var (manifest, _, files) = ManifestTestHelper.Create(files: [("a.txt", "hola"), ("b.txt", "adios")]);
        files.Remove("b.txt");
        var staging = StageFiles(files);

        await Assert.ThrowsAsync<PackageValidationException>(() => PackageValidator.ValidateFilesAsync(manifest, staging));
    }

    [Fact]
    public void ValidateSignature_WrongFingerprint_Throws()
    {
        var (manifest, _, _) = ManifestTestHelper.Create();
        var other = Fingerprint.FromPublicKey(Ed25519.GenerateKeyPair().PublicKey);

        Assert.Throws<PackageValidationException>(() => PackageValidator.ValidateSignature(manifest, other));
    }

    [Fact]
    public void ValidateSignature_TamperedManifest_Throws()
    {
        var (manifest, keys, _) = ManifestTestHelper.Create();
        var tampered = manifest with { Version = "9.9" };

        Assert.Throws<PackageValidationException>(() => PackageValidator.ValidateSignature(tampered, Fingerprint.FromPublicKey(keys.PublicKey)));
    }
}
