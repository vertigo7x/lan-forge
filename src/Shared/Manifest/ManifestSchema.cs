namespace LanForge.Shared.Manifest;

/// <summary>Versión actual del esquema de manifiesto.</summary>
public static class ManifestSchema
{
    public const int Current = 1;

    public static bool IsSupported(int schemaVersion) => schemaVersion == Current;
}
