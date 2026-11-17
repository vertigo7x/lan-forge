namespace LanForge.Shared.Manifest;

/// <summary>Formato alambre del manifiesto firmado. La firma cubre el payload canónico.</summary>
public sealed record SignedPackageManifest
{
    public required int SchemaVersion { get; init; }
    public required string PackageId { get; init; }
    public required string GameId { get; init; }
    public required string GameTitle { get; init; }
    public required string Version { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string PublicKey { get; init; }
    public required IReadOnlyList<ManifestFileEntry> Files { get; init; }
    public required string EntryPoint { get; init; }
    public string? Arguments { get; init; }
    public required string Signature { get; init; } // base64, Ed25519 sobre el payload canónico

    public PackageManifestPayload ToPayload() => new()
    {
        SchemaVersion = SchemaVersion,
        PackageId = PackageId,
        GameId = GameId,
        GameTitle = GameTitle,
        Version = Version,
        CreatedUtc = CreatedUtc,
        PublicKey = PublicKey,
        Files = Files,
        EntryPoint = EntryPoint,
        Arguments = Arguments,
    };

    public static SignedPackageManifest FromPayload(PackageManifestPayload payload, string signatureBase64) => new()
    {
        SchemaVersion = payload.SchemaVersion,
        PackageId = payload.PackageId,
        GameId = payload.GameId,
        GameTitle = payload.GameTitle,
        Version = payload.Version,
        CreatedUtc = payload.CreatedUtc,
        PublicKey = payload.PublicKey,
        Files = payload.Files,
        EntryPoint = payload.EntryPoint,
        Arguments = payload.Arguments,
        Signature = signatureBase64,
    };

    public byte[] ToJsonBytes() => SharedJson.SerializeCanonical(this);

    public string ToJson() => System.Text.Encoding.UTF8.GetString(ToJsonBytes());

    public static SignedPackageManifest? FromJson(ReadOnlySpan<byte> utf8Json) =>
        SharedJson.Deserialize<SignedPackageManifest>(utf8Json);

    public static SignedPackageManifest? FromJson(string json) =>
        SharedJson.Deserialize<SignedPackageManifest>(json);
}
