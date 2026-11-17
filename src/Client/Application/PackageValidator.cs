using LanForge.Shared.Manifest;
using LanForge.Shared.Security;

namespace LanForge.Client.Application;

/// <summary>Validación ANTES de instalar: firma del manifiesto, fingerprint de confianza y sha256 de cada archivo.</summary>
public static class PackageValidator
{
    /// <summary>Sin I/O: válido antes de descargar. Lanza <see cref="PackageValidationException"/> si falla.</summary>
    public static void ValidateSignature(SignedPackageManifest manifest, Fingerprint trustedFingerprint)
    {
        if (!ManifestVerifier.Verify(manifest))
        {
            throw new PackageValidationException("La firma del manifiesto no es válida.");
        }

        if (!ManifestVerifier.MatchesFingerprint(manifest, trustedFingerprint))
        {
            throw new PackageValidationException("El manifiesto no fue firmado por el servidor de confianza (fingerprint distinto).");
        }
    }

    public static async Task ValidateFilesAsync(
        SignedPackageManifest manifest,
        string stagingDirectory,
        CancellationToken cancellationToken = default)
    {
        var stagingRoot = Path.GetFullPath(stagingDirectory);
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(Path.Combine(stagingRoot, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(stagingRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new PackageValidationException($"Ruta de staging insegura: '{file.Path}'.");
            }

            if (!File.Exists(path))
            {
                throw new PackageValidationException($"Falta el archivo descargado: '{file.Path}'.");
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await Sha256Hash.ComputeHexAsync(stream, cancellationToken);
            if (!string.Equals(hash, file.Sha256, StringComparison.Ordinal))
            {
                throw new PackageValidationException($"Hash sha256 inválido en '{file.Path}': el paquete está corrupto o fue alterado.");
            }
        }
    }
}
