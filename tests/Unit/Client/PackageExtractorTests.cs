using System.IO.Compression;
using LanForge.Client.Application;

namespace LanForge.UnitTests.Client;

public sealed class PackageExtractorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Extract_ValidArchive_WritesFiles()
    {
        var zip = Path.Combine(_temp.Path, "ok.zip");
        await File.WriteAllBytesAsync(zip, PackageExtractor.BuildArchive(new Dictionary<string, byte[]>
        {
            ["x/y.txt"] = "hola"u8.ToArray(),
            ["z.txt"] = "adios"u8.ToArray(),
        }));
        var target = Path.Combine(_temp.Path, "out");

        await PackageExtractor.ExtractAsync(zip, target);

        Assert.Equal("hola", File.ReadAllText(Path.Combine(target, "x", "y.txt")));
        Assert.Equal("adios", File.ReadAllText(Path.Combine(target, "z.txt")));
    }

    [Fact]
    public async Task Extract_ZipSlipEntry_ThrowsAndDoesNotWriteOutside()
    {
        var zip = Path.Combine(_temp.Path, "evil.zip");
        using (var stream = new FileStream(zip, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("malicious");
        }

        var target = Path.Combine(_temp.Path, "out");

        await Assert.ThrowsAsync<PackageValidationException>(() => PackageExtractor.ExtractAsync(zip, target));
        Assert.False(File.Exists(Path.Combine(_temp.Path, "escape.txt")));
    }
}
