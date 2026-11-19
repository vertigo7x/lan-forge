using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LanForge.Server.Application;
using LanForge.Server.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.Server.Views;

/// <summary>
/// Formulario independiente de creación de paquetes. Solo depende del contrato
/// <see cref="IPackageCreationService"/>, por lo que se puede construir con un
/// servicio simulado en pruebas o resolver el servicio real desde DI.
/// </summary>
public partial class PackageCreateView : UserControl
{
    public PackageCreateView()
        : this(ResolveService())
    {
    }

    public PackageCreateView(IPackageCreationService service)
        : this(service, null)
    {
    }

    public PackageCreateView(IPackageCreationService service, IPathPicker? pathPicker)
    {
        ArgumentNullException.ThrowIfNull(service);
        InitializeComponent();

        // El selector de rutas real se construye cuando la vista ya esta pegada a
        // una ventana: el dialogo del sistema operativo necesita el proveedor de
        // almacenamiento de la aplicacion, que solo existe a partir del nivel
        // superior. Las pruebas pueden inyectar su propio selector.
        ViewModel = new PackageCreateViewModel(service, pathPicker ?? new TopLevelPathPicker(this));
        DataContext = ViewModel;
    }

    public PackageCreateViewModel ViewModel { get; }

    /// <summary>Permite a las pruebas rellenar el formulario sin acceder a los controles.</summary>
    public void SetFormValues(
        string gameId,
        string title,
        string version,
        string sourceDirectory,
        string entryPoint,
        string arguments)
    {
        ViewModel.GameId = gameId;
        ViewModel.Title = title;
        ViewModel.Version = version;
        ViewModel.SourceDirectory = sourceDirectory;
        ViewModel.EntryPoint = entryPoint;
        ViewModel.Arguments = arguments;
    }

    /// <summary>Valida y envía el formulario usando el servicio inyectado.</summary>
    public Task SubmitAsync() => ViewModel.SubmitAsync();

    /// <summary>
    /// Opciones del diálogo nativo de archivos del ejecutable.
    ///
    /// El filtro es solo <c>*.exe</c>: el diálogo del sistema operativo deja
    /// elegir únicamente ejecutables de Windows. La comprobación de la
    /// extensión se repite después en el view model, porque el filtro es una
    /// comodidad del diálogo y no una garantía.
    ///
    /// Vive en un método aparte para que las pruebas puedan comprobar el
    /// filtro sin abrir un diálogo.
    /// </summary>
    public static FilePickerOpenOptions ExecutablePickerOptions() => new()
    {
        AllowMultiple = false,
        Title = "Elige el ejecutable del juego",
        FileTypeFilter =
        [
            new FilePickerFileType("Ejecutable de Windows")
            {
                Patterns = [PackageSourcePaths.ExecutablePattern],
            },
        ],
    };

    /// <summary>
    /// Opciones del diálogo nativo de carpetas de la carpeta origen.
    ///
    /// El diálogo elige una sola carpeta (la del juego portable) y no filtra por
    /// extensión: lo que se comprueba despues es que la carpeta exista, en
    /// <see cref="PackageSourcePaths.Validate"/>.
    ///
    /// Vive en un método aparte para que las pruebas puedan comprobar las
    /// opciones sin abrir un diálogo.
    /// </summary>
    public static FolderPickerOpenOptions SourceDirectoryPickerOptions() => new()
    {
        AllowMultiple = false,
        Title = "Elige la carpeta del juego portable",
    };

    private static IPackageCreationService ResolveService()
    {
        var services = App.WebServices ?? throw new InvalidOperationException("El host web no está disponible.");
        return services.GetRequiredService<IPackageCreationService>();
    }
}

/// <summary>
/// Selector de rutas que busca el proveedor de almacenamiento de la ventana
/// donde esta la vista. Se resuelve en el momento de abrir el dialogo, no al
/// construir la vista: la vista se crea antes de que exista la ventana.
///
/// Si la vista no esta pegada a ninguna ventana no hay dialogo que abrir, asi
/// que se devuelve null y el campo se queda como estaba.
/// </summary>
internal sealed class TopLevelPathPicker : IPathPicker
{
    private readonly Control _owner;

    public TopLevelPathPicker(Control owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var storage = Storage();
        if (storage is null)
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(
            PackageCreateView.SourceDirectoryPickerOptions());

        cancellationToken.ThrowIfCancellationRequested();
        return folders.Count > 0 ? LocalPath(folders[0]) : null;
    }

    /// <inheritdoc />
    public async Task<string?> PickExecutableAsync(CancellationToken cancellationToken = default)
    {
        var storage = Storage();
        if (storage is null)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(PackageCreateView.ExecutablePickerOptions());

        cancellationToken.ThrowIfCancellationRequested();
        return files.Count > 0 ? LocalPath(files[0]) : null;
    }

    private IStorageProvider? Storage() => TopLevel.GetTopLevel(_owner)?.StorageProvider;

    /// <summary>
    /// Devuelve la ruta local del elemento elegido. Los dialogos pueden devolver
    /// una URI sin ruta local (un proveedor remoto): en ese caso no hay una ruta
    /// que enviar al servidor, asi que se devuelve null.
    ///
    /// El elemento y su ruta pueden ser null: el proveedor puede devolver una
    /// entrada sin ruta local (MEDIDO: al pedir un archivo por una URI que no
    /// es del sistema de ficheros, el proveedor devuelve null). Un null ahi
    /// haria caer el formulario al elegir, asi que se traduce a null, que es lo
    /// que el formulario entiende como "no se ha elegido nada".
    ///
    /// Es interno (no privado) para que las pruebas puedan comprobarlo: es la
    /// traduccion del elemento del dialogo a la ruta que entiende el servidor,
    /// y en headless el dialogo no devuelve ningun elemento con el que
    /// probarlo de otra manera.
    /// </summary>
    internal static string? LocalPath(IStorageItem? item)
    {
        var localPath = item?.Path?.LocalPath;
        return string.IsNullOrWhiteSpace(localPath) ? null : localPath;
    }
}
