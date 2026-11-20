using System.Text.RegularExpressions;

namespace LanForge.Client.ViewModels;

/// <summary>Resuelve la caché local del logotipo global de una LAN party.</summary>
public static class LanPartyLogoResolver
{
    private static readonly Regex FileNamePattern = new(
        "^lan-party-[a-f0-9]{64}\\.(png|jpg|jpeg|webp)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string BrandingDirectory(string dataDirectory) => Path.Combine(dataDirectory, "branding");

    /// <summary>Ruta cacheada segura, o null si el nombre anunciado no es válido.</summary>
    public static string? TargetPath(string dataDirectory, string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) || !FileNamePattern.IsMatch(fileName)
            ? null
            : Path.Combine(BrandingDirectory(dataDirectory), fileName);

    public static string? CachedPath(string dataDirectory, string? fileName)
    {
        var path = TargetPath(dataDirectory, fileName);
        return path is not null && File.Exists(path) ? path : null;
    }
}
