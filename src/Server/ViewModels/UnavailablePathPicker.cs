namespace LanForge.Server.ViewModels;

/// <summary>
/// Selector de rutas sin dialogos del sistema operativo. Se usa cuando el view
/// model se crea sin un selector (por ejemplo, en pruebas que no necesitan
/// abrir un dialogo): las acciones de elegir carpeta o ejecutable avisan de que
/// no hay ningun dialogo disponible en lugar de fallar en silencio.
///
/// La aplicacion real inyecta <see cref="Views.TopLevelPathPicker"/>, que si usa
/// los dialogos nativos del sistema operativo.
/// </summary>
internal sealed class UnavailablePathPicker : Application.IPathPicker
{
    /// <summary>
    /// No hay ningun dialogo que abrir: el formulario tiene que avisar en vez de
    /// dejar al organizador pulsando «Examinar…» sin que pase nada.
    /// </summary>
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<string?> PickExecutableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
