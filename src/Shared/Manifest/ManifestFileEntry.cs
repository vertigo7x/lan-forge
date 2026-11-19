using System.Text.RegularExpressions;

namespace LanForge.Shared.Manifest;

/// <summary>Archivo dentro de un paquete, con su hash e integridad.</summary>
public sealed record ManifestFileEntry(string Path, string Sha256, long Size)
{
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ManifestFileEntry Create(string relativePath, string sha256Hex, long size)
    {
        var normalized = ValidatePath(relativePath);
        if (sha256Hex is null || !Sha256Pattern.IsMatch(sha256Hex))
        {
            throw new ArgumentException("El sha256 debe ser hex minúscula de 64 caracteres.", nameof(sha256Hex));
        }

        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "El tamaño no puede ser negativo.");
        }

        return new ManifestFileEntry(normalized, sha256Hex, size);
    }

    /// <summary>Normaliza y valida una ruta relativa: sin absolutas, sin '..', separadores '/'.</summary>
    public static string ValidatePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("La ruta del archivo no puede estar vacía.", nameof(relativePath));
        }

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (LooksAbsolute(relativePath) || normalized.Contains(':') || normalized.Split('/').Any(p => p == ".."))
        {
            throw new ArgumentException($"Ruta de archivo inválida o insegura: '{relativePath}'.", nameof(relativePath));
        }

        return normalized;
    }

    /// <summary>
    /// Detecta rutas absolutas por patrón (raíz de Unix, UNC o unidad con letra).
    /// La comprobación se hace sobre la cadena original porque normalizar no
    /// convierte una ruta absoluta en relativa: quitarle la barra inicial solo
    /// escondería que era absoluta. Se hace por patrón y no con
    /// <see cref="Path.IsPathRooted(string)"/> para que el resultado no dependa del
    /// sistema de ficheros del host.
    /// </summary>
    private static bool LooksAbsolute(string path)
    {
        var trimmed = path.Trim();
        return trimmed.StartsWith('/') ||
               trimmed.StartsWith('\\') ||
               (trimmed.Length >= 3 && char.IsLetter(trimmed[0]) && trimmed[1] == ':');
    }
}
