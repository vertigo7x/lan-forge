using SkiaSharp;

namespace LanForge.Server.Application;

/// <summary>Reglas del logotipo global de la LAN party.</summary>
public static class LanPartyLogoRules
{
    public const int MaximumSidePixels = 215;

    public static string? ValidateFile(string filePath)
    {
        var coverError = CoverImageRules.ValidateFile(filePath);
        if (coverError is not null)
        {
            return coverError;
        }

        using var bitmap = SKBitmap.Decode(filePath);
        if (bitmap is null || bitmap.Width != bitmap.Height)
        {
            return "El logotipo debe ser una imagen cuadrada.";
        }

        return bitmap.Width > MaximumSidePixels
            ? $"El logotipo no puede superar {MaximumSidePixels} × {MaximumSidePixels} píxeles."
            : null;
    }
}
