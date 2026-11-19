using LanForge.Server.Application;
using LanForge.Server.Infrastructure;

namespace LanForge.Server.Application;

/// <summary>
/// Adjunta la portada de un juego usando el servicio de catalogo y el almacen de
/// portadas del propio servidor.
///
/// La UI del organizador vive en el mismo proceso que el host web, asi que la
/// portada se asigna en proceso: no hace falta tocar el contrato JSON de
/// <c>POST /api/v1/packages</c> ni el manifiesto firmado.
/// </summary>
public sealed class CatalogCoverUploadService : ICoverUploadService
{
    private readonly CatalogService _catalog;
    private readonly ICoverStore _covers;

    public CatalogCoverUploadService(CatalogService catalog, ICoverStore covers)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(covers);

        _catalog = catalog;
        _covers = covers;
    }

    /// <summary>
    /// Valida la imagen y la asigna como portada del juego. La validacion se repite
    /// aqui (el selector del formulario tambien valida) porque esta es la ultima
    /// linea de defensa antes de escribir en disco.
    /// </summary>
    public async Task<string> UploadAsync(
        string gameId,
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("El fichero de la imagen ya no existe.", imagePath);
        }

        var info = new FileInfo(imagePath);
        var error = CoverImageRules.Validate(info.Name, null, info.Length);
        if (error is not null)
        {
            throw new ArgumentException(error, nameof(imagePath));
        }

        return await _catalog.SetCoverAsync(gameId, imagePath, _covers, cancellationToken);
    }
}
