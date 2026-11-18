using Avalonia.Controls;
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
    {
        ArgumentNullException.ThrowIfNull(service);
        InitializeComponent();
        ViewModel = new PackageCreateViewModel(service);
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

    private static IPackageCreationService ResolveService()
    {
        var services = App.WebServices ?? throw new InvalidOperationException("El host web no está disponible.");
        return services.GetRequiredService<IPackageCreationService>();
    }
}
