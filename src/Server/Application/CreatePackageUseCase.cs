using LanForge.Server.Domain;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using LanForge.Shared.Security;

namespace LanForge.Server.Application;

/// <summary>
/// Caso de uso para crear o reemplazar un paquete. Valida la solicitud, aplica
/// las reglas de publicación y persiste el resultado mediante los puertos del catálogo.
/// </summary>
public sealed class CreatePackageUseCase
{
    private readonly ICatalogStore _store;
    private readonly IPackageContentStore _content;
    private readonly IServerIdentity _identity;

    public CreatePackageUseCase(ICatalogStore store, IPackageContentStore content, IServerIdentity identity)
    {
        _store = store;
        _content = content;
        _identity = identity;
    }

    public async Task<CreatePackageResponse> CreateAsync(
        CreatePackageRequest? request,
        CancellationToken cancellationToken = default)
    {
        var errors = CreatePackageValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new PackageValidationException(errors);
        }

        var gameId = request!.GameId!;
        var title = request.Title!;
        var version = request.Version!;
        var sourceDirectory = request.SourceDirectory!;
        var entryPoint = request.EntryPoint!;

        var source = new DirectoryInfo(sourceDirectory);
        if (!source.Exists)
        {
            throw new PackageSourceNotFoundException(sourceDirectory);
        }

        var packageVersion = new PackageVersion(version);
        var existing = await _store.GetPackageAsync(gameId, cancellationToken);
        if (existing is not null && string.Equals(existing.Version, packageVersion.Value, StringComparison.Ordinal))
        {
            throw new PackageVersionConflictException(gameId, packageVersion.Value);
        }

        var files = await HashFilesAsync(source, cancellationToken);
        if (files.Count == 0)
        {
            throw new EmptySourceDirectoryException(sourceDirectory);
        }

        var normalizedEntryPoint = ManifestFileEntry.ValidatePath(entryPoint);
        if (!files.Any(file => file.Path == normalizedEntryPoint))
        {
            throw new PackageEntryPointNotFoundException(entryPoint);
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
            entryPoint: normalizedEntryPoint,
            arguments: request.Arguments);
        var manifest = ManifestSigner.Sign(payload, _identity.KeyPair);

        var package = new Package
        {
            PackageId = gameId,
            GameId = gameId,
            Version = packageVersion.Value,
            EntryPoint = manifest.EntryPoint,
            Arguments = manifest.Arguments,
            TotalSizeBytes = files.Sum(file => file.Size),
            FileCount = files.Count,
            CreatedUtc = manifest.CreatedUtc,
            Manifest = manifest,
        };

        if (existing is null)
        {
            await _store.UpsertGameAsync(new Game(gameId, title), cancellationToken);
            await _store.AddPackageAsync(package, cancellationToken);
        }
        else
        {
            await _store.ReplacePackageAsync(package, cancellationToken);
        }

        return new CreatePackageResponse(
            gameId,
            gameId,
            title,
            packageVersion.Value,
            package.EntryPoint,
            package.Arguments,
            package.TotalSizeBytes,
            package.FileCount,
            package.CreatedUtc,
            existing?.Version);
    }

    private static async Task<IReadOnlyList<ManifestFileEntry>> HashFilesAsync(
        DirectoryInfo source,
        CancellationToken cancellationToken)
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
