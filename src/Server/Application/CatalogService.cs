using LanForge.Server.Domain;
using LanForge.Shared.Logging;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using LanForge.Shared.Security;

namespace LanForge.Server.Application;

/// <summary>Casos de uso del catálogo: juegos, publicación de paquetes firmados y consultas de descarga.</summary>
public sealed class CatalogService
{
    /// <summary>
    /// Antiguedad maxima que se conserva en la tabla de operaciones. Cada sincronizacion
    /// de catalogo y cada descarga inserta una fila, asi que sin esta poda la tabla
    /// creceria durante toda la vida del servidor.
    /// </summary>
    public static readonly TimeSpan OperationRetention = TimeSpan.FromDays(7);

    /// <summary>Cuantos archivos se hashean a la vez al publicar un paquete.</summary>
    private const int MaxConcurrentHashes = 4;

    private readonly ICatalogStore _store;
    private readonly IPackageContentStore _content;
    private readonly IServerIdentity _identity;
    private readonly IServerLog? _log;
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset _lastPruneUtc = DateTimeOffset.MinValue;

    public CatalogService(
        ICatalogStore store, IPackageContentStore content, IServerIdentity identity,
        IServerLog? log = null, TimeProvider? timeProvider = null)
    {
        _store = store;
        _content = content;
        _identity = identity;
        _log = log;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

    /// <summary>
    /// Devuelve el catalogo para los clientes. Lee los juegos una sola vez y los empareja
    /// en memoria: antes hacia una consulta a <c>games</c> por cada paquete (N+1), y este
    /// es el endpoint que todos los clientes llaman en cada sincronizacion.
    /// </summary>
    public async Task<IReadOnlyList<CatalogEntry>> ListCatalogAsync(CancellationToken cancellationToken = default)
    {
        var packages = await _store.ListLatestPackagesAsync(cancellationToken);
        var games = await _store.ListGamesAsync(cancellationToken);
        var gamesById = new Dictionary<string, Game>(games.Count, StringComparer.Ordinal);
        foreach (var game in games)
        {
            gamesById[game.Id] = game;
        }

        var entries = new List<CatalogEntry>(packages.Count);
        foreach (var package in packages)
        {
            gamesById.TryGetValue(package.GameId, out var game);
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

    /// <summary>
    /// Registra una operación en el store (vista "operaciones" de la UI). De paso poda
    /// las operaciones mas antiguas que la ventana de retencion, pero no en cada
    /// registro: como mucho una vez por minuto, para no pagar un DELETE por descarga.
    /// </summary>
    public async Task RecordOperationAsync(ServerOperation operation, CancellationToken cancellationToken = default)
    {
        await _store.RecordOperationAsync(operation, cancellationToken);
        await PruneOperationsIfDueAsync(cancellationToken);
    }

    /// <summary>Poda las operaciones que han superado la ventana de retencion, si toca poda.</summary>
    private async Task PruneOperationsIfDueAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (now - _lastPruneUtc < TimeSpan.FromMinutes(1))
        {
            return;
        }

        _lastPruneUtc = now;
        var pruned = await _store.PruneOperationsAsync(now - OperationRetention, cancellationToken);
        if (pruned > 0)
        {
            await WriteLogAsync(
                null, "operations.pruned", LogSeverity.Debug,
                $"Poda de operaciones: {pruned} fila(s) mas antiguas de {OperationRetention.TotalDays:0} dia(s)",
                cancellationToken);
        }
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

    /// <summary>
    /// Hashea los archivos del directorio fuente. El hashing es una operacion larga y
    /// bloqueante (con varios GB puede tardar minutos), asi que se ejecuta fuera del hilo
    /// que llama: el organizador publica desde el hilo de UI y, sin esto, la ventana se
    /// quedaba congelada. Los archivos se hashean en paralelo, con un tope para no agotar
    /// la memoria ni el disco con un juego enorme.
    /// </summary>
    private static async Task<IReadOnlyList<ManifestFileEntry>> HashFilesAsync(
        DirectoryInfo source, CancellationToken cancellationToken)
    {
        var files = source.EnumerateFiles("*", SearchOption.AllDirectories);
        var entries = new List<ManifestFileEntry>();
        using var throttle = new SemaphoreSlim(MaxConcurrentHashes);

        var tasks = files.Select(async file =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(source.FullName, file.FullName).Replace('\\', '/');
                await using var stream = file.OpenRead();
                var hash = await Sha256Hash.ComputeHexAsync(stream, cancellationToken).ConfigureAwait(false);
                return ManifestFileEntry.Create(relativePath, hash, file.Length);
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        var hashed = await Task.WhenAll(tasks).ConfigureAwait(false);
        entries.AddRange(hashed);
        return entries;
    }
}
