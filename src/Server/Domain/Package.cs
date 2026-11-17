using LanForge.Shared.Manifest;

namespace LanForge.Server.Domain;

/// <summary>Paquete publicado. MVP: PackageId == GameId (un paquete activo por juego).</summary>
public sealed record Package
{
    public required string PackageId { get; init; }
    public required string GameId { get; init; }
    public required string Version { get; init; }
    public required string EntryPoint { get; init; }
    public required string? Arguments { get; init; }
    public required long TotalSizeBytes { get; init; }
    public required int FileCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required SignedPackageManifest Manifest { get; init; }
}
