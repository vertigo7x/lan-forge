namespace LanForge.Server.Application;

/// <summary>
/// Adjunta una imagen de portada a un juego ya publicado.
///
/// El modelo de paquete no tiene campo de imagen (el manifiesto firmado no puede
/// admitirla sin romper la firma), asi que la portada viaja por su propio canal:
/// el archivo se guarda con <see cref="ICoverStore"/> y la fila del juego apunta
/// al archivo mediante <c>Game.CoverFile</c>.
///
/// La portada se puede asignar solo DESPUES de crear el paquete: el juego tiene que
/// existir ya en el catalogo.
/// </summary>
public interface ICoverUploadService
{
    /// <summary>
    /// Sube la imagen de portada del juego. Devuelve el nombre del archivo guardado.
    /// </summary>
    /// <param name="gameId">Juego al que se asigna la portada.</param>
    /// <param name="imagePath">Ruta local de la imagen a subir.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<string> UploadAsync(
        string gameId,
        string imagePath,
        CancellationToken cancellationToken = default);
}
