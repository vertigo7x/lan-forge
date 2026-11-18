using LanForge.Server.Domain;
using LanForge.Shared.Logging;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using LanForge.Shared.Security;

namespace LanForge.Server.Application;

/// <summary>Casos de uso del catálogo: juegos, publicación de paquetes firmados y consultas de descarga.</summary>
public sealed class CatalogService
{
    private readonly ICatalogStore _store;
    private readonly IPackageContentStore _content;
    private readonly IServerIdentity _identity;
    private readonly IServerLog? _log;

    public CatalogService(ICatalogStore store, IPackageContentStore content, IServerIdentity identity, IServerLog? log = null)
    {
        _store = store;
        _content = content;
        _identity = identity;
        _log = log;
    }

    public async Task<Game> AddOrUpdateGameAsync(
        string gameId, string title, string? description = null, CancellationToken cancellationToken = default)
    {
        var existing = await _store.GetGameAsync(gameId, cancellationToken);
        var game = existing is null
            ? new Game(gameId, title, description)
            : existing with { Title = title, Description = description ?? existing.Description };
        await _store.UpsertGameAsync(game, cancellationToken);
        return game;
    }

    /// <summary>
    /// Publica un juego portable: hashea los archivos, importa el contenido, genera
    /// el manifiesto y lo firma con la identidad del servidor.
    /// </summary>
    public async Task<CatalogEntry> PublishPackageAsync(
        string gameId,
        string title,
        string version,
        string sourceDirectory,
        string entryPoint,
        string? arguments = null,
        CancellationToken cancellationToken = default)
    {
        GameId.Validate(gameId);
        var packageVersion = new PackageVersion(version);

        var source = new DirectoryInfo(sourceDirectory);
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"El directorio fuente no existe: '{sourceDirectory}'.");
        }

        var existing = await _store.GetPackageAsync(gameId, cancellationToken);
        if (existing is not null && string.Equals(existing.Version, packageVersion.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Ya está publicada la versión '{version}' de '{gameId}'.");
        }

        // La fila del juego puede existir aunque todavia no haya paquete (el organizador
        // guarda la portada y la descripcion antes de publicar). Publicar no puede borrar
        // esos datos: los hereda y solo fija el titulo.
        var existingGame = await _store.GetGameAsync(gameId, cancellationToken);

        var files = await HashFilesAsync(source, cancellationToken);
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"El directorio fuente no contiene archivos: '{sourceDirectory}'.");
        }

        await _content.ImportArchiveAsync(gameId, source.FullName, cancellationToken);

        var payload = PackageManifestPayload.Create(
            packageId: gameId,
            gameId: gameId,
            gameTitle: title,
            version: packageVersion.Value,
            createdUtc: DateTimeOffset.UtcNow,
            publicKeyBase64: Convert.ToBase64String(_identity.KeyPair.PublicKey),
            files: files,
            entryPoint: entryPoint,
            arguments: arguments);
        var manifest = ManifestSigner.Sign(payload, _identity.KeyPair);

        var package = new Package
        {
            PackageId = gameId,
            GameId = gameId,
            Version = packageVersion.Value,
            EntryPoint = manifest.EntryPoint,
            Arguments = manifest.Arguments,
            TotalSizeBytes = files.Sum(f => f.Size),
            FileCount = files.Count,
            CreatedUtc = manifest.CreatedUtc,
            Manifest = manifest,
        };

        // El manifiesto firmado lleva el titulo, asi que la fila del catalogo debe llevar el
        // mismo: si divergen, el cliente muestra un nombre y el manifiesto otro. Descripcion y
        // portada se heredan de la fila existente: publicar no borra los datos del organizador
        // (la edicion de metadatos sigue siendo de AddOrUpdateGameAsync).
        var game = existingGame is null
            ? new Game(gameId, title)
            : existingGame with { Title = title };
        await _store.UpsertGameAsync(game, cancellationToken);

        if (existing is null)
        {
            await _store.AddPackageAsync(package, cancellationToken);
        }
        else
        {
            await _store.ReplacePackageAsync(package, cancellationToken);
        }

        await WriteLogAsync(null, "catalog.publish", LogSeverity.Info, $"Publicado {gameId} v{packageVersion.Value} ({files.Count} archivos)", cancellationToken);
        return new CatalogEntry(gameId, title, gameId, packageVersion.Value, package.TotalSizeBytes, package.FileCount);
    }

    public async Task<IReadOnlyList<CatalogEntry>> ListCatalogAsync(CancellationToken cancellationToken = default)
    {
        var packages = await _store.ListLatestPackagesAsync(cancellationToken);
        var entries = new List<CatalogEntry>(packages.Count);
        foreach (var package in packages)
        {
            var game = await _store.GetGameAsync(package.GameId, cancellationToken);
            entries.Add(new CatalogEntry(
                package.GameId,
                game?.Title ?? package.GameId,
                package.PackageId,
                package.Version,
                package.TotalSizeBytes,
                package.FileCount,
                game?.CoverFile));
        }

        return entries;
    }

    /// <summary>Asigna una imagen de portada a un juego (la sirve el endpoint /api/v1/covers).</summary>
    public async Task<string> SetCoverAsync(string gameId, string sourceImagePath, ICoverStore covers, CancellationToken cancellationToken = default)
    {
        GameId.Validate(gameId);
        var game = await _store.GetGameAsync(gameId, cancellationToken)
            ?? throw new InvalidOperationException($"El juego '{gameId}' no existe.");

        var fileName = await covers.SaveAsync(gameId, sourceImagePath, cancellationToken);
        await _store.UpsertGameAsync(game with { CoverFile = fileName }, cancellationToken);
        return fileName;
    }

    /// <summary>
    /// Borra un juego del catalogo: el paquete publicado, el .zip en disco, su portada
    /// y las filas del store. Es la operacion que usa el organizador para quitar un juego.
    /// </summary>
    public async Task<bool> DeleteGameAsync(string gameId, ICoverStore covers, CancellationToken cancellationToken = default)
    {
        GameId.Validate(gameId);
        var game = await _store.GetGameAsync(gameId, cancellationToken);
        var package = await _store.GetPackageAsync(gameId, cancellationToken);
        if (game is null && package is null)
        {
            return false;
        }

        await _content.DeleteArchiveAsync(gameId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(game?.CoverFile))
        {
            await covers.DeleteAsync(game.CoverFile, cancellationToken);
        }

        await _store.DeleteGameAsync(gameId, cancellationToken);
        await WriteLogAsync(null, "catalog.delete", LogSeverity.Info, $"Juego eliminado del catalogo: {gameId}", cancellationToken);
        return true;
    }

    public async Task<SignedPackageManifest?> GetManifestAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var package = await _store.GetPackageAsync(packageId, cancellationToken);
        return package?.Manifest;
    }

    /// <summary>Abre el .zip completo del paquete para descarga (una sola transferencia).</summary>
    public async Task<PackageFileStream?> OpenArchiveDownloadAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var package = await _store.GetPackageAsync(packageId, cancellationToken);
        if (package is null)
        {
            return null;
        }

        var stream = await _content.OpenArchiveAsync(packageId, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        var totalBytes = package.TotalSizeBytes;
        return new PackageFileStream(stream, new ManifestFileEntry(packageId, string.Empty, totalBytes));
    }

    /// <summary>Registra un cliente visto a través del header X-LanForge-Client-Id.</summary>
    public async Task RecordSeenClientForHeaderAsync(
        string? clientId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        await _store.UpsertSeenClientAsync(new SeenClient(clientId, DateTimeOffset.UtcNow, ipAddress), cancellationToken);
    }

    /// <summary>Abre un archivo para descarga solo si está declarado en el manifiesto firmado del paquete.</summary>
    public async Task<PackageFileStream?> OpenDownloadAsync(
        string packageId, string relativePath, CancellationToken cancellationToken = default)
    {
        var package = await _store.GetPackageAsync(packageId, cancellationToken);
        if (package is null)
        {
            return null;
        }

        string normalized;
        try
        {
            normalized = ManifestFileEntry.ValidatePath(relativePath);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var entry = package.Manifest.Files.FirstOrDefault(f => f.Path == normalized);
        if (entry is null)
        {
            return null;
        }

        var stream = await _content.OpenReadAsync(packageId, normalized, cancellationToken);
        return stream is null ? null : new PackageFileStream(stream, entry);
    }

    /// <summary>Registra una operación en el store (vista "operaciones" de la UI).</summary>
    public async Task RecordOperationAsync(ServerOperation operation, CancellationToken cancellationToken = default)
    {
        await _store.RecordOperationAsync(operation, cancellationToken);
    }

    private async Task WriteLogAsync(
        string? clientId, string eventName, LogSeverity severity, string message,
        CancellationToken cancellationToken)
    {
        if (_log is null)
        {
            return;
        }

        await _log.LogAsync(
            new LogEntry(DateTimeOffset.UtcNow, clientId ?? "server", Guid.NewGuid().ToString("n"), severity, eventName, message),
            cancellationToken);
    }

    private static async Task<IReadOnlyList<ManifestFileEntry>> HashFilesAsync(DirectoryInfo source, CancellationToken cancellationToken)
    {
        var files = new List<ManifestFileEntry>();
        foreach (var file in source.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(source.FullName, file.FullName).Replace('\\', '/');
            await using var stream = file.OpenRead();
            var hash = await Sha256Hash.ComputeHexAsync(stream, cancellationToken);
            files.Add(ManifestFileEntry.Create(relativePath, hash, file.Length));
        }

        return files;
    }
}
