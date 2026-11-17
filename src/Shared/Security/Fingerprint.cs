using System.Security.Cryptography;

namespace LanForge.Shared.Security;

/// <summary>
/// Identidad de confianza de un servidor: SHA-256 de su clave pública Ed25519.
/// Se muestra agrupada en hex minúscula y se compara fuera de banda (TOFU).
/// </summary>
public sealed class Fingerprint : IEquatable<Fingerprint>
{
    public const int HashLength = 32;
    private const int GroupSize = 8;

    public byte[] Hash { get; }

    private Fingerprint(byte[] hash)
    {
        Hash = hash;
    }

    public static Fingerprint FromPublicKey(ReadOnlySpan<byte> publicKeyRaw)
    {
        if (publicKeyRaw.Length != KeyPair.PublicKeyLength)
        {
            throw new ArgumentException($"La clave pública debe tener {KeyPair.PublicKeyLength} bytes.", nameof(publicKeyRaw));
        }

        return new Fingerprint(SHA256.HashData(publicKeyRaw));
    }

    public static Fingerprint Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var compact = text.Replace("-", string.Empty);
        if (compact.Length != HashLength * 2 ||
            compact.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new FormatException($"Fingerprint inválido: '{text}'. Se esperaban 64 caracteres hex (opcionalmente agrupados con '-').");
        }

        return new Fingerprint(Convert.FromHexString(compact));
    }

    public bool Equals(Fingerprint? other) =>
        other is not null && Hash.AsSpan().SequenceEqual(other.Hash);

    public override bool Equals(object? obj) => Equals(obj as Fingerprint);

    public override int GetHashCode()
    {
        var code = new HashCode();
        code.AddBytes(Hash);
        return code.ToHashCode();
    }

    public override string ToString()
    {
        var hex = Convert.ToHexString(Hash).ToLowerInvariant();
        return string.Join('-', Enumerable.Range(0, HashLength * 2 / GroupSize)
            .Select(i => hex.Substring(i * GroupSize, GroupSize)));
    }

    public static bool operator ==(Fingerprint? left, Fingerprint? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);

    public static bool operator !=(Fingerprint? left, Fingerprint? right) => !(left == right);
}
