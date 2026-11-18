namespace LanForge.Shared.Models;

/// <summary>
/// Paquete publicado tras una creación correcta (201 Created).
/// Refleja el estado final del paquete: el manifiesto ya está firmado.
/// </summary>
public sealed record CreatePackageResponse(
    string PackageId,
    string GameId,
    string Title,
    string Version,
    string EntryPoint,
    string? Arguments,
    long TotalSizeBytes,
    int FileCount,
    DateTimeOffset CreatedUtc,
    string? PreviousVersion = null);
