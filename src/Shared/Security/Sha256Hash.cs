using System.Security.Cryptography;

namespace LanForge.Shared.Security;

/// <summary>SHA-256 en hex minúscula; usado para hashes de archivos de paquetes.</summary>
public static class Sha256Hash
{
    public static string ComputeHex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static async Task<string> ComputeHexAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
