using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace LanForge.Client.Controls;

/// <summary>
/// Convierte la ruta o URI de una portada en la imagen que pinta el control Image.
/// Image.Source es un IImage: enlazarle un string no dibuja nada, por eso el conversor.
/// </summary>
public sealed class CoverImageConverter : IValueConverter
{
    public static CoverImageConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string source || string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            if (source.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = AssetLoader.Open(new Uri(source));
                return new Bitmap(stream);
            }

            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                return new Bitmap(uri.LocalPath);
            }

            return new Bitmap(source);
        }
        catch (Exception)
        {
            // Una portada rota no puede tumbar el catalogo: se muestra el hueco vacio.
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("La portada no se edita desde la vista.");
}
