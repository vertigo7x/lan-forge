using LanForge.Client.Domain;
using LanForge.Shared.Manifest;

namespace LanForge.Client.Application;

/// <summary>Instala el staging en el directorio confinado de juegos y limpia versiones antiguas.</summary>
public sealed class GameInstaller
{
    private readonly IClientStore _store;
    private readonly string _installRoot;

    public GameInstaller(IClientStore store, string installRoot)
    {
        _store = store;
        _installRoot = Path.GetFullPath(installRoot);
        Directory.CreateDirectory(_installRoot);
    }

    public async Task<InstalledGame> InstallAsync(
        SignedPackageManifest manifest, string stagingDirectory, CancellationToken cancellationToken = default)
    {
        var gameDirectory = GetConfiniedPath(_installRoot, manifest.GameId);
        var targetDirectory = GetConfiniedPath(gameDirectory, manifest.Version);

        if (Directory.Exists(targetDirectory))
        {
            Directory.Delete(targetDirectory, recursive: true);
        }

        Directory.CreateDirectory(gameDirectory);
        await CopyDirectoryAsync(Path.GetFullPath(stagingDirectory), targetDirectory, cancellationToken);

        // Actualización: conservar solo la versión recién instalada.
        foreach (var directory in Directory.GetDirectories(gameDirectory))
        {
            if (!string.Equals(Path.GetFullPath(directory), targetDirectory, StringComparison.Ordinal))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        var installed = new InstalledGame(
            manifest.GameId,
            manifest.PackageId,
            manifest.Version,
            targetDirectory,
            manifest.EntryPoint,
            manifest.Arguments,
            DateTimeOffset.UtcNow);
        await _store.SaveInstalledAsync(installed, cancellationToken);
        return installed;
    }

    public async Task UninstallAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var gameDirectory = Path.GetFullPath(Path.Combine(_installRoot, gameId));
        if (Directory.Exists(gameDirectory))
        {
            Directory.Delete(gameDirectory, recursive: true);
        }

        await _store.RemoveInstalledAsync(gameId, cancellationToken);
    }

    private static string GetConfiniedPath(string root, string segment)
    {
        var full = Path.GetFullPath(Path.Combine(root, segment));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new PackageValidationException($"Ruta de instalación insegura: '{segment}'.");
        }

        return full;
    }

    private static async Task CopyDirectoryAsync(string source, string target, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}
