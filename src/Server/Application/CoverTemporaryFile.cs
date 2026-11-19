namespace LanForge.Server.Application;

/// <summary>Materializa de forma acotada un fichero de un proveedor sin ruta local.</summary>
public static class CoverTemporaryFile
{
    public static async Task<(string? Path, string? Error)> CopyFromAsync(
        Stream source,
        string sourceFileName,
        CancellationToken cancellationToken = default,
        string? temporaryDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var extension = Path.GetExtension(sourceFileName);
        if (!CoverImageRules.IsAllowedExtension(extension))
        {
            return (null, "Solo se admiten imagenes PNG, JPG, JPEG o WEBP.");
        }

        var temporary = Path.Combine(temporaryDirectory ?? Path.GetTempPath(), $"lanforge-cover-{Guid.NewGuid():n}{extension}");
        try
        {
            var exceededMaximumSize = false;
            await using (var target = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    copied += read;
                    if (copied > CoverImageRules.MaxSizeBytes)
                    {
                        exceededMaximumSize = true;
                        break;
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            if (exceededMaximumSize)
            {
                TryDelete(temporary);
                return (null, $"La imagen no puede pasar de {CoverImageRules.MaxSizeBytes / (1024 * 1024)} MB.");
            }

            return (temporary, null);
        }
        catch (Exception) when (File.Exists(temporary))
        {
            TryDelete(temporary);
            return (null, "No se ha podido leer el fichero de la imagen.");
        }
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}