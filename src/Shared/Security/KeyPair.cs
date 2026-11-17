namespace LanForge.Shared.Security;

/// <summary>Par de claves Ed25519 en formato raw exportable (bytes).</summary>
public sealed record KeyPair
{
    public const int PrivateKeyLength = 32; // seed Ed25519 tal como lo exporta NSec (RawPrivateKey)
    public const int PublicKeyLength = 32;

    public byte[] PrivateKey { get; }
    public byte[] PublicKey { get; }

    public KeyPair(byte[] privateKey, byte[] publicKey)
    {
        if (privateKey is null || privateKey.Length != PrivateKeyLength)
        {
            throw new ArgumentException($"La clave privada debe tener {PrivateKeyLength} bytes.", nameof(privateKey));
        }

        if (publicKey is null || publicKey.Length != PublicKeyLength)
        {
            throw new ArgumentException($"La clave pública debe tener {PublicKeyLength} bytes.", nameof(publicKey));
        }

        PrivateKey = privateKey;
        PublicKey = publicKey;
    }
}
