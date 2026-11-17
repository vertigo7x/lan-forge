using LanForge.Shared.Security;

namespace LanForge.Shared.Manifest;

/// <summary>Verifica firma e identidad de un manifiesto. Usado por el cliente antes de instalar.</summary>
public static class ManifestVerifier
{
    public static bool Verify(SignedPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!ManifestSchema.IsSupported(manifest.SchemaVersion))
        {
            return false;
        }

        byte[] publicKey;
        byte[] signature;
        try
        {
            publicKey = Convert.FromBase64String(manifest.PublicKey);
            signature = Convert.FromBase64String(manifest.Signature);
        }
        catch (FormatException)
        {
            return false;
        }

        if (publicKey.Length != KeyPair.PublicKeyLength || signature.Length != 64)
        {
            return false;
        }

        var canonical = SharedJson.SerializeCanonical(manifest.ToPayload());
        return Ed25519.Verify(publicKey, canonical, signature);
    }

    /// <summary>El fingerprint anunciado por el servidor debe coincidir con la clave que firmó el manifiesto.</summary>
    public static bool MatchesFingerprint(SignedPackageManifest manifest, Fingerprint trustedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(trustedFingerprint);

        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(manifest.PublicKey);
        }
        catch (FormatException)
        {
            return false;
        }

        return Fingerprint.FromPublicKey(publicKey).Equals(trustedFingerprint);
    }
}
