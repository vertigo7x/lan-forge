namespace LanForge.Shared.Manifest;

/// <summary>
/// Contenido firmable del manifiesto. Su serialización canónica (SharedJson) es
/// EXACTAMENTE lo que se firma y se verifica. No incluye la firma.
/// </summary>
public sealed record PackageManifestPayload
{
    public required int SchemaVersion { get; init; }
    public required string PackageId { get; init; }
    public required string GameId { get; init; }
    public required string GameTitle { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string PublicKey { get; init; } // base64, clave pública Ed25519 del firmante
    public required IReadOnlyList<ManifestFileEntry> Files { get; init; } // ordenadas por Path ordinal
    public required string EntryPoint { get; init; }
    public string? Arguments { get; init; }

    public static PackageManifestPayload Create(
        string packageId,
        string gameId,
        string gameTitle,
        string version,
        DateTimeOffset createdUtc,
        string publicKeyBase64,
        IEnumerable<ManifestFileEntry> files,
        string entryPoint,
        string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        var ordered = files
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToArray();

        if (ordered.Length == 0)
        {
            throw new ArgumentException("Un manifiesto debe contener al menos un archivo.", nameof(files));
        }

        var normalizedEntryPoint = ManifestFileEntry.ValidatePath(entryPoint);
        if (!ordered.Any(f => f.Path == normalizedEntryPoint))
        {
            throw new ArgumentException($"El punto de entrada '{entryPoint}' no existe en la lista de archivos.", nameof(entryPoint));
        }

        return new PackageManifestPayload
        {
            SchemaVersion = ManifestSchema.Current,
            PackageId = packageId,
            GameId = gameId,
            GameTitle = gameTitle,
            Version = version,
            CreatedUtc = createdUtc,
            PublicKey = publicKeyBase64,
            Files = ordered,
            EntryPoint = normalizedEntryPoint,
            Arguments = arguments,
        };
    }
}
