using LanForge.Shared.Security;

namespace LanForge.Shared.Manifest;

/// <summary>Firma un manifiesto con la clave del servidor. Usado por el organizador.</summary>
public static class ManifestSigner
{
    public static SignedPackageManifest Sign(PackageManifestPayload payload, KeyPair keyPair)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(keyPair);

        byte[] payloadPublicKey;
        try
        {
            payloadPublicKey = Convert.FromBase64String(payload.PublicKey);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("La clave pública del payload debe ser base64 válido.", nameof(payload), ex);
        }

        if (!payloadPublicKey.AsSpan().SequenceEqual(keyPair.PublicKey))
        {
            throw new ArgumentException("La clave pública del payload no coincide con el par de claves usado para firmar.", nameof(payload));
        }

        var canonical = SharedJson.SerializeCanonical(payload);
        var signature = Ed25519.Sign(keyPair.PrivateKey, canonical);
        return SignedPackageManifest.FromPayload(payload, Convert.ToBase64String(signature));
    }
}
