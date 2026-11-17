using System.Security.Cryptography;
using NSec.Cryptography;

namespace LanForge.Shared.Security;

/// <summary>Envoltorio sobre NSec/libsodium para Ed25519. Punto único de cambio si se cambia la librería.</summary>
public static class Ed25519
{
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    public static KeyPair GenerateKeyPair()
    {
        using var key = Key.Create(Algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        return new KeyPair(
            key.Export(KeyBlobFormat.RawPrivateKey),
            key.Export(KeyBlobFormat.RawPublicKey));
    }

    public static byte[] Sign(ReadOnlySpan<byte> privateKeyRaw, ReadOnlySpan<byte> message)
    {
        using var key = ImportPrivate(privateKeyRaw);
        return Algorithm.Sign(key, message);
    }

    public static bool Verify(ReadOnlySpan<byte> publicKeyRaw, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        try
        {
            var publicKey = PublicKey.Import(Algorithm, publicKeyRaw, KeyBlobFormat.RawPublicKey);
            return Algorithm.Verify(publicKey, message, signature);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static Key ImportPrivate(ReadOnlySpan<byte> privateKeyRaw)
    {
        try
        {
            return Key.Import(Algorithm, privateKeyRaw, KeyBlobFormat.RawPrivateKey);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException)
        {
            throw new ArgumentException("Clave privada Ed25519 inválida.", nameof(privateKeyRaw), ex);
        }
    }
}
