using System.IO.Compression;

namespace LanForge.Client.Application;

/// <summary>Extracción segura de zips de paquetes: rechaza entradas que salgan del directorio destino (ZipSlip).</summary>
public static class PackageExtractor
{
    public static async Task ExtractAsync(string zipPath, string targetDirectory, CancellationToken cancellationToken = default)
    {
        var targetRoot = Path.GetFullPath(targetDirectory);
        Directory.CreateDirectory(targetRoot);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Las carpetas aparecen como entradas terminadas en '/'.
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (entry.Length == 0 && entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(targetRoot, relative));
            if (!destination.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new PackageValidationException($"Entrada zip insegura: '{entry.FullName}'.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    /// <summary>Crea un zip en memoria a partir de un conjunto de archivos (usado en tests y stubs).</summary>
    public static byte[] BuildArchive(IReadOnlyDictionary<string, byte[]> files)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = archive.CreateEntry(path);
                using var stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return memory.ToArray();
    }
}
