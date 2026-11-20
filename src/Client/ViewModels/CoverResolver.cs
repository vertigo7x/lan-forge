using System.Text.RegularExpressions;

namespace LanForge.Client.ViewModels;

/// <summary>
/// Resuelve de donde sale la portada de una game-card: la copia cacheada por el cliente
/// o, si todavia no esta descargada, una portada de reserva del design.
/// Es logica pura (sin UI) para poder testearla.
/// </summary>
public static class CoverResolver
{
    /// <summary>Solo nombres planos de portada: sin rutas, sin traversal, con extension de imagen.</summary>
    private static readonly Regex FileNamePattern = new(
        "^[a-zA-Z0-9_-]+\\.(png|jpg|jpeg|webp)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Ruta local donde se cachean las portadas descargadas del servidor.</summary>
    public static string CoversDirectory(string dataDirectory) => Path.Combine(dataDirectory, "covers");

    /// <summary>
    /// Ruta donde se guarda la portada de un juego, exista o no todavia.
    /// Devuelve null si el nombre no es valido (nada de rutas ni traversal).
    /// </summary>
    public static string? CoverTargetPath(string dataDirectory, string? coverFileName)
    {
        if (string.IsNullOrWhiteSpace(coverFileName) || !FileNamePattern.IsMatch(coverFileName))
        {
            return null;
        }

        return Path.Combine(CoversDirectory(dataDirectory), coverFileName);
    }

    /// <summary>
    /// Ruta local de la portada cacheada, o null si no existe o el nombre no es valido.
    /// </summary>
    public static string? CachedCoverPath(string dataDirectory, string? coverFileName)
    {
        var path = CoverTargetPath(dataDirectory, coverFileName);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>URI file:// de la portada cacheada, o null si no esta descargada.</summary>
    public static string? CachedCoverUri(string dataDirectory, string? coverFileName)
    {
        var path = CachedCoverPath(dataDirectory, coverFileName);
        return path is null ? null : new Uri(path).AbsoluteUri;
    }

}
