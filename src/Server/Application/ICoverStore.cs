namespace LanForge.Server.Application;

/// <summary>Puerto de almacenamiento de portadas de juegos (imágenes servidas por HTTP).</summary>
public interface ICoverStore
{
    /// <summary>Copia la imagen fuente (png/jpg/jpeg/webp) como portada del juego. Devuelve el nombre de archivo.</summary>
    Task<string> SaveAsync(string gameId, string sourceImagePath, CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string fileName, CancellationToken cancellationToken = default);
}
