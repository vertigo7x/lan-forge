using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using LanForge.Shared.Protocol;

namespace LanForge.Client.Application;

/// <summary>Puerto de acceso HTTP al servidor. Implementado por HttpServerApi; mockeable en tests.</summary>
public interface IServerApi
{
    string BaseUrl { get; }

    string ClientId { get; }

    Task<HelloResponse> GetHelloAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task<SignedPackageManifest> GetManifestAsync(string packageId, CancellationToken cancellationToken = default);

    /// <summary>Abre la descarga de un archivo. <paramref name="resumeFromBytes"/> &gt; 0 solicita reanudación (Range).</summary>
    Task<Stream> OpenDownloadAsync(string packageId, string relativePath, long resumeFromBytes, CancellationToken cancellationToken = default);

    /// <summary>Abre la descarga del .zip completo del paquete (una sola transferencia, reanudable con Range).</summary>
    Task<Stream> OpenArchiveDownloadAsync(string packageId, long resumeFromBytes, CancellationToken cancellationToken = default);

    /// <summary>Descarga una portada de juego del servidor; null si no existe.</summary>
    Task<byte[]?> DownloadCoverAsync(string fileName, CancellationToken cancellationToken = default);
}
