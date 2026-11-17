using LanForge.Shared.Manifest;

namespace LanForge.Server.Application;

/// <summary>Stream de descarga con la entrada de manifiesto correspondiente (nombre, tamaño, hash).</summary>
public sealed record PackageFileStream(Stream Stream, ManifestFileEntry Entry);
