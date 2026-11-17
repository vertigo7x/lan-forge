using LanForge.Client.Application;
using LanForge.UnitTests.Client.Fakes;

namespace LanForge.UnitTests.Client;

public sealed class PackageDownloaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Download_ArchiveIsExtractedToStaging()
    {
        var (manifest, _, files) = ManifestTestHelper.Create("game", "1.0", ("a.txt", "aaa"), ("dir/b.txt", "bbbb"));
        var api = new StubServerApi(manifest, files);
        var downloader = new PackageDownloader(api);
        var staging = Path.Combine(_temp.Path, "staging");

        var extracted = await downloader.DownloadAsync(manifest, staging);

        Assert.Equal("aaa", File.ReadAllText(Path.Combine(extracted, "a.txt")));
        Assert.Equal("bbbb", File.ReadAllText(Path.Combine(extracted, "dir", "b.txt")));
        Assert.Single(api.DownloadRequests);
        Assert.Equal(0, api.DownloadRequests[0].ResumeFrom);
        Assert.True(File.Exists(Path.Combine(staging, PackageDownloader.ArchiveFileName)));
    }

    [Fact]
    public async Task Download_PartialArchive_ResumesFromExistingBytes()
    {
        var (manifest, _, files) = ManifestTestHelper.Create("game", "1.0", ("a.txt", "aaaaaaaaaa"));
        var api = new StubServerApi(manifest, files);
        var downloader = new PackageDownloader(api);
        var staging = Path.Combine(_temp.Path, "staging");
        Directory.CreateDirectory(staging);

        var archive = PackageExtractor.BuildArchive(files);
        await File.WriteAllBytesAsync(Path.Combine(staging, PackageDownloader.ArchiveFileName + ".part"), archive[..5]);

        var extracted = await downloader.DownloadAsync(manifest, staging);

        Assert.Equal("aaaaaaaaaa", File.ReadAllText(Path.Combine(extracted, "a.txt")));
        Assert.Single(api.DownloadRequests);
        Assert.Equal(5, api.DownloadRequests[0].ResumeFrom);
    }

    [Fact]
    public async Task Download_CompleteArchive_IsReusedWithoutDownloading()
    {
        var (manifest, _, files) = ManifestTestHelper.Create("game", "1.0", ("a.txt", "aaaa"));
        var api = new StubServerApi(manifest, files);
        var downloader = new PackageDownloader(api);
        var staging = Path.Combine(_temp.Path, "staging");
        Directory.CreateDirectory(staging);
        await File.WriteAllBytesAsync(Path.Combine(staging, PackageDownloader.ArchiveFileName), PackageExtractor.BuildArchive(files));

        var extracted = await downloader.DownloadAsync(manifest, staging);

        Assert.Equal("aaaa", File.ReadAllText(Path.Combine(extracted, "a.txt")));
        Assert.Empty(api.DownloadRequests);
    }

    [Fact]
    public async Task Download_OversizedPartial_ThrowsInsteadOfCorrupting()
    {
        var (manifest, _, files) = ManifestTestHelper.Create("game", "1.0", ("a.txt", "aa"));
        var api = new StubServerApi(manifest, files);
        var downloader = new PackageDownloader(api);
        var staging = Path.Combine(_temp.Path, "staging");
        Directory.CreateDirectory(staging);
        await File.WriteAllBytesAsync(
            Path.Combine(staging, PackageDownloader.ArchiveFileName + ".part"),
            new byte[PackageExtractor.BuildArchive(files).Length + 100]);

        await Assert.ThrowsAnyAsync<Exception>(() => downloader.DownloadAsync(manifest, staging));
    }

    [Fact]
    public async Task Download_ReportsProgressAndFinished()
    {
        var (manifest, _, files) = ManifestTestHelper.Create("game", "1.0", ("a.txt", "aaaaaaaaaa"));
        var downloader = new PackageDownloader(new StubServerApi(manifest, files));
        var staging = Path.Combine(_temp.Path, "staging");
        var progress = new List<DownloadProgress>();

        await downloader.DownloadAsync(manifest, staging, new Progress<DownloadProgress>(progress.Add));

        Assert.Contains(progress, p => p.Finished);
        Assert.All(progress.Where(p => !p.Finished), p => Assert.True(p.CompletedBytes <= Math.Max(p.TotalBytes, 1)));
    }
}
