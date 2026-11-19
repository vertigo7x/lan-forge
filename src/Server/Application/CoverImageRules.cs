using SkiaSharp;

namespace LanForge.Server.Application;

/// <summary>
/// Reglas de las imágenes de portada. Las comparte el selector del formulario
/// (que valida antes de dejar elegir el fichero) y el almacenamiento de portadas
/// (que valida de nuevo al guardar), de modo que la lista de formatos admitidos y
/// el limite de tamaño no puedan divergir entre las dos capas.
/// </summary>
public static class CoverImageRules
{
    /// <summary>Extensiones de imagen admitidas, con el punto inicial.</summary>
    public static readonly IReadOnlyList<string> AllowedExtensions =
    [
        ".png", ".jpg", ".jpeg", ".webp",
    ];

    /// <summary>Tipos MIME de imagen admitidos, en minúsculas.</summary>
    public static readonly IReadOnlyList<string> AllowedContentTypes =
    [
        "image/png", "image/jpeg", "image/jpg", "image/webp",
    ];

    /// <summary>
    /// Tamaño máximo de la portada en bytes (5 MB). Guarda del lado del cliente: el
    /// servidor comprueba el mismo limite al recibir la imagen.
    /// </summary>
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    /// <summary>True si la extension del fichero es una de las admitidas.</summary>
    public static bool IsAllowedExtension(string? extension) =>
        extension is not null &&
        AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True si el tipo MIME declarado es uno de los de imagen admitidos. Un tipo
    /// vacio o desconocido se acepta: algunos sistemas de ficheros no lo informan,
    /// y la extension sigue siendo obligatoria.
    /// </summary>
    public static bool IsAllowedContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ||
        AllowedContentTypes.Contains(contentType.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Valida una imagen y devuelve el motivo del rechazo, o null si es valida.
    /// </summary>
    /// <param name="fileName">Nombre del fichero: se usa para la extension.</param>
    /// <param name="contentType">Tipo MIME declarado por el sistema de ficheros.</param>
    /// <param name="sizeBytes">Tamaño del fichero en bytes.</param>
    public static string? Validate(string? fileName, string? contentType, long sizeBytes)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (!IsAllowedExtension(extension))
        {
            return "Solo se admiten imagenes PNG, JPG, JPEG o WEBP.";
        }

        if (!IsAllowedContentType(contentType))
        {
            return "El fichero seleccionado no es una imagen.";
        }

        if (sizeBytes > MaxSizeBytes)
        {
            return $"La imagen no puede pasar de {MaxSizeBytes / (1024 * 1024)} MB.";
        }

        if (sizeBytes == 0)
        {
            return "El fichero de imagen esta vacio.";
        }

        return null;
    }

    /// <summary>
    /// Valida los metadatos y decodifica una imagen local completa. El tipo MIME y
    /// la extensión son datos declarados por el proveedor del fichero; la
    /// decodificación comprueba los datos reales y rechaza contenido truncado o
    /// forjado sin requerir la plataforma de render de Avalonia.
    /// </summary>
    public static string? ValidateFile(string filePath, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                return "El fichero de la imagen no existe.";
            }

            var metadataError = Validate(info.Name, contentType, info.Length);
            if (metadataError is not null)
            {
                return metadataError;
            }

            using var codec = SKCodec.Create(filePath);
            if (codec is null || !MatchesExtension(Path.GetExtension(info.Name), codec.EncodedFormat))
            {
                return "El contenido del fichero no es una imagen valida del formato indicado.";
            }

            using var bitmap = SKBitmap.Decode(filePath);
            if (bitmap is null)
            {
                return "El contenido del fichero no es una imagen valida del formato indicado.";
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "No se ha podido leer el fichero de la imagen.";
        }
    }

    private static bool MatchesExtension(string extension, SKEncodedImageFormat decodedFormat) =>
        extension.ToLowerInvariant() switch
        {
            ".png" => decodedFormat == SKEncodedImageFormat.Png,
            ".jpg" or ".jpeg" => decodedFormat == SKEncodedImageFormat.Jpeg,
            ".webp" => decodedFormat == SKEncodedImageFormat.Webp,
            _ => false,
        };

}
