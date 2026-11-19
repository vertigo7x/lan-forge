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
        : this(ResolveService(), ResolveCoverUploads(), ResolvePreviewLoader())
    {
    }

    public PackageCreateView(IPackageCreationService service)
        : this(service, coverUploads: null, previewLoader: null)
    {
    }

    public PackageCreateView(
        IPackageCreationService service,
        ICoverUploadService? coverUploads,
        ICoverPreviewLoader? previewLoader = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        InitializeComponent();
        ViewModel = new PackageCreateViewModel(
            service,
            coverUploads,
            previewLoader,
            new TopLevelPathPicker(this));
        DataContext = ViewModel;

        BrowseCoverButton.Click += async (_, _) => await BrowseCoverAsync();
        ClearCoverButton.Click += (_, _) => ViewModel.ClearCover();
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
    /// Abre el selector de ficheros para elegir la imagen de portada.
    ///
    /// Esta vista es un UserControl, no una ventana, asi que no tiene
    /// <c>StorageProvider</c> propio: se resuelve desde el nivel superior al que
    /// este control esta enganchado.
    /// </summary>
    public async Task BrowseCoverAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            ViewModel.SelectCover(null);
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Imagen") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] },
            ],
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            // El usuario ha cancelado: no se toca la seleccion anterior.
            return;
        }

        var (localPath, error, isTemporary) = await ResolveLocalPathAsync(file);
        if (localPath is null)
        {
            ViewModel.ReportCoverSelectionError(error ?? "No se ha podido leer el fichero de la imagen.");
            return;
        }

        if (isTemporary)
        {
            ViewModel.SelectTemporaryCover(localPath);
        }
        else
        {
            ViewModel.SelectCover(localPath);
        }
    }

    /// <summary>
    /// Devuelve la ruta local del fichero elegido. Normalmente la trae el propio
    /// selector; si no, se copia el contenido a un temporal para poder validarlo
    /// (tamano) y adjuntarlo.
    /// </summary>
    private static async Task<(string? Path, string? Error, bool IsTemporary)> ResolveLocalPathAsync(IStorageFile file)
    {
        var localPath = file.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            return (localPath, null, false);
        }

        // Sin ruta local: se vuelca el contenido en un temporal. El nombre conserva
        // la extension, que es lo que decide si la imagen es valida.
        try
        {
            await using var source = await file.OpenReadAsync();
            var (temporary, error) = await CoverTemporaryFile.CopyFromAsync(source, file.Name);
            return (temporary, error, temporary is not null);
        }
        catch (Exception)
        {
            return (null, "No se ha podido leer el fichero de la imagen.", false);
        }
    }

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

    private static ICoverUploadService? ResolveCoverUploads()
    {
        // El servicio de portadas es opcional: sin el, el formulario sigue
        // funcionando, pero avisa de que no puede adjuntar la imagen.
        return App.WebServices?.GetService<ICoverUploadService>();
    }

    private static ICoverPreviewLoader? ResolvePreviewLoader()
    {
        // El cargador de la previsualizacion tambien es opcional: sin el, el
        // formulario no muestra la imagen, pero el resto funciona igual.
        return App.WebServices?.GetService<ICoverPreviewLoader>();
    }
}

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
