namespace LanForge.Shared.Models;

/// <summary>Entrada del catálogo tal como se publica por HTTP. CoverPath es el nombre de archivo
/// de la imagen en el servidor (GET /api/v1/covers/{CoverPath}); null si el juego no tiene.</summary>
public sealed record CatalogEntry(
    string GameId,
    string Title,
    string PackageId,
    string Version,
    long TotalSizeBytes,
    int FileCount,
    string? CoverPath = null);
