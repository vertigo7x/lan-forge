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
    /// Indica si hay un dialogo del sistema operativo disponible para elegir una
    /// ruta.
    ///
    /// Distingue dos situaciones que las dos devuelven null pero no son la misma:
    /// el usuario cancela el dialogo (silencioso, es lo normal) y no hay ningun
    /// dialogo que abrir (un fallo que el organizador tiene que conocer, porque
    /// pulsar «Examinar…» no hace nada y no hay ninguna pista de por que).
    ///
    /// Por defecto se considera que el dialogo esta disponible: solo los
    /// selectores que saben que no pueden abrirlo lo declaran.
    /// </summary>
    bool IsAvailable => true;

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
