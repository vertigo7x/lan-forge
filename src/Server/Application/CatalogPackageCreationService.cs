using LanForge.Server.Domain;

namespace LanForge.Server.Application;

/// <summary>
/// Adaptador local del formulario de creación de paquetes. Publica mediante el
/// mismo caso de uso del catálogo, sin hacer una petición HTTP al propio servidor.
/// </summary>
public sealed class CatalogPackageCreationService : IPackageCreationService
{
    private readonly CatalogService _catalog;

    public CatalogPackageCreationService(CatalogService catalog)
    {
        _catalog = catalog;
    }

    public async Task<PackageCreationResult> CreateAsync(
        PackageCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var entry = await _catalog.PublishPackageAsync(
                request.GameId,
                request.Title,
                request.Version,
                request.SourceDirectory,
                request.EntryPoint,
                request.Arguments,
                cancellationToken);

            return PackageCreationResult.Success(new PackageCreationSummary(
                entry.PackageId,
                entry.GameId,
                entry.Title,
                entry.Version,
                entry.FileCount,
                entry.TotalSizeBytes));
        }
        catch (DirectoryNotFoundException ex)
        {
            return Invalid(PackageCreationFields.SourceDirectory, ex.Message);
        }
        catch (EmptySourceDirectoryException ex)
        {
            return Invalid(PackageCreationFields.SourceDirectory, ex.Message);
        }
        catch (ArgumentException ex) when (ex.ParamName == "entryPoint")
        {
            return Invalid(PackageCreationFields.EntryPoint, ex.Message);
        }
        catch (PackageVersionConflictException ex)
        {
            return PackageCreationResult.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return PackageCreationResult.Failure(ex.Message);
        }
    }

    private static PackageCreationResult Invalid(string field, string message) =>
        PackageCreationResult.Invalid(
            new Dictionary<string, string>(StringComparer.Ordinal) { [field] = message },
            message);
}
