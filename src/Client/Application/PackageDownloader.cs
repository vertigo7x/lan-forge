using LanForge.Shared.Manifest;

namespace LanForge.Client.Application;

public sealed record DownloadProgress(
    string GameId,
    string FilePath,
    long CompletedBytes,
    long TotalBytes,
    bool Finished);

/// <summary>
/// Descarga el paquete como un único .zip (reanudable) y lo extrae en staging.
/// La integridad se garantiza después extrayendo: firma del manifiesto + sha256 por archivo.
/// </summary>
public sealed class PackageDownloader
{
    public const string ArchiveFileName = "package.zip";
    public const string ExtractedDirectoryName = "files";

    private readonly IServerApi _api;

    public PackageDownloader(IServerApi api)
    {
        _api = api;
    }

    /// <summary>Descarga y extrae el paquete. Devuelve el directorio con los archivos extraídos.</summary>
    public async Task<string> DownloadAsync(
        SignedPackageManifest manifest,
        string stagingDirectory,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stagingRoot = Path.GetFullPath(stagingDirectory);
        Directory.CreateDirectory(stagingRoot);
        var zipPath = Path.Combine(stagingRoot, ArchiveFileName);
        var filesDirectory = Path.Combine(stagingRoot, ExtractedDirectoryName);

        await DownloadArchiveAsync(manifest, zipPath, progress, cancellationToken);
        await PackageExtractor.ExtractAsync(zipPath, filesDirectory, cancellationToken);
        return filesDirectory;
    }

    private async Task DownloadArchiveAsync(
        SignedPackageManifest manifest,
        string zipPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        // El zip completo solo existe con nombre final tras descargarse entero (move atómico),
        // así que si está presente es reutilizable; lo parcial vive en ".part" y se reanuda.
        if (File.Exists(zipPath))
        {
            progress?.Report(new DownloadProgress(manifest.GameId, ArchiveFileName, 1, 1, Finished: true));
            return;
        }

        var partialPath = zipPath + ".part";
        var existingBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

        await using var input = await _api.OpenArchiveDownloadAsync(manifest.PackageId, existingBytes, cancellationToken);
        var totalFromHeader = TryGetContentLength(input) ?? -1;
        var totalBytes = totalFromHeader > 0 ? existingBytes + totalFromHeader : manifest.Files.Sum(f => f.Size);
        long received = 0;

        await using (var output = new FileStream(
            partialPath, existingBytes > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                progress?.Report(new DownloadProgress(
                    manifest.GameId, ArchiveFileName, existingBytes + received, totalBytes, Finished: false));
            }
        }

        File.Move(partialPath, zipPath);
        progress?.Report(new DownloadProgress(
            manifest.GameId, ArchiveFileName, existingBytes + received, existingBytes + received, Finished: true));
    }

    private static long? TryGetContentLength(Stream stream) =>
        stream.CanSeek ? stream.Length : null;
}
