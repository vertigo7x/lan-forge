namespace LanForge.Server.Application;

/// <summary>
/// Seleccion de rutas con los dialogos nativos del sistema operativo. La interfaz
/// existe para que el view model no dependa de Avalonia: asi se puede probar el
/// flujo de elegir carpeta y ejecutable sin abrir una ventana.
///
/// Las implementaciones devuelven la ruta local absoluta de lo elegido, o null si
/// el usuario cancela el dialogo.
/// </summary>
public interface IPathPicker
{
    /// <summary>
    /// Abre el dialogo de carpetas del sistema operativo y devuelve la ruta
    /// absoluta de la carpeta elegida, o null si se cancela.
    /// </summary>
    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Abre el dialogo de archivos del sistema operativo filtrado a ejecutables
    /// (<c>*.exe</c>) y devuelve la ruta absoluta del archivo elegido, o null si se
    /// cancela.
    /// </summary>
    Task<string?> PickExecutableAsync(CancellationToken cancellationToken = default);
}
