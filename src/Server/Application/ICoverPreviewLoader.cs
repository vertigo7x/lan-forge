using Avalonia.Media.Imaging;

namespace LanForge.Server.Application;

/// <summary>
/// Carga la previsualizacion de una imagen de portada.
///
/// La decodificacion depende de la plataforma de render (Avalonia necesita el
/// <c>IPlatformRenderInterface</c>), asi que va detras de un puerto: el view model
/// no depende de la plataforma y las pruebas pueden comprobar el estado del
/// formulario sin depender de que haya render.
/// </summary>
public interface ICoverPreviewLoader
{
    /// <summary>
    /// Devuelve la imagen decodificada, o null si el fichero no se puede
    /// decodificar. Un null no invalida la seleccion: la portada se puede
    /// adjuntar igual.
    /// </summary>
    Bitmap? Load(string imagePath);
}

/// <summary>
/// Previsualizacion con el decodificador de Avalonia. Un fichero que no se puede
/// decodificar no es un error: se devuelve null y la vista muestra solo el nombre.
/// </summary>
public sealed class AvaloniaCoverPreviewLoader : ICoverPreviewLoader
{
    public Bitmap? Load(string imagePath)
    {
        try
        {
            return new Bitmap(imagePath);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
