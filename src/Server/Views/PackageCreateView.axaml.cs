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
        ViewModel = new PackageCreateViewModel(service, coverUploads, previewLoader);
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

        var localPath = await ResolveLocalPathAsync(file);
        if (localPath is null)
        {
            ViewModel.SelectCover(null);
            return;
        }

        ViewModel.SelectCover(localPath);
    }

    /// <summary>
    /// Devuelve la ruta local del fichero elegido. Normalmente la trae el propio
    /// selector; si no, se copia el contenido a un temporal para poder validarlo
    /// (tamano) y adjuntarlo.
    /// </summary>
    private static async Task<string?> ResolveLocalPathAsync(IStorageFile file)
    {
        var localPath = file.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            return localPath;
        }

        // Sin ruta local: se vuelca el contenido en un temporal. El nombre conserva
        // la extension, que es lo que decide si la imagen es valida.
        var extension = Path.GetExtension(file.Name);
        var temporary = Path.Combine(Path.GetTempPath(), $"lanforge-cover-{Guid.NewGuid():n}{extension}");
        try
        {
            await using var source = await file.OpenReadAsync();
            await using var target = File.Create(temporary);
            await source.CopyToAsync(target);
            return temporary;
        }
        catch (Exception)
        {
            TryDelete(temporary);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // El temporal se limpia al reiniciar el sistema: no es grave.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem: el temporal se queda, pero no rompe el formulario.
        }
    }

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
