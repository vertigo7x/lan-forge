using LanForge.Server.Application;
using LanForge.Server.Http;
using LanForge.Server.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>
/// Capa de servicio del formulario de creación de paquetes: debe poder resolverse
/// desde el contenedor de servicios (cualquier componente la usa) y admitir una
/// implementación alternativa sin tocar al consumidor.
///
/// El contenedor resuelve el servicio local: el formulario publica mediante el
/// mismo caso de uso que el catálogo.
/// </summary>
public sealed class PackageCreationServiceLayerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task El_servicio_se_resuelve_desde_el_contenedor()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();

        var service = app.Services.GetRequiredService<IPackageCreationService>();

        Assert.NotNull(service);
        Assert.IsType<CatalogPackageCreationService>(service);
    }

    [Fact]
    public async Task El_formulario_crea_el_paquete_a_traves_del_servicio_del_contenedor()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();

        var service = app.Services.GetRequiredService<IPackageCreationService>();
        var source = _temp.CreateSourceDirectory(
            ("run.exe", "fake-binary"),
            ("content/pak0.bin", "pak-data"));

        // La vista y el view model solo dependen de la interfaz: se puede
        // construir el formulario con el servicio resuelto del contenedor.
        //
        // El ejecutable se indica con su ruta absoluta, como lo deja el dialogo
        // de archivos del sistema operativo: el formulario lo convierte a la ruta
        // relativa que espera el endpoint.
        var viewModel = new PackageCreateViewModel(service)
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "1.0.0",
            SourceDirectory = source,
            EntryPoint = Path.Combine(source, "run.exe"),
        };

        await viewModel.SubmitAsync();

        Assert.False(viewModel.StatusIsError);
        Assert.True(viewModel.HasStatusMessage);
        Assert.Contains("Neon Drift", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_implementacion_alternativa_sustituye_al_cliente_real_sin_tocar_al_consumidor()
    {
        // Se registra otra implementación del mismo contrato; el consumidor
        // (PackageCreateViewModel) no cambia ni una linea.
        var services = new ServiceCollection();
        services.AddSingleton<IPackageCreationService>(
            new MockPackageCreationService(
                TimeSpan.Zero,
                _ => PackageCreationResult.Failure("Fallo del servidor real.")));
        using var provider = services.BuildServiceProvider();

        var viewModel = new PackageCreateViewModel(provider.GetRequiredService<IPackageCreationService>());
        var source = _temp.CreateSourceDirectory(("juego.exe", "fake-binary"));
        viewModel.GameId = "neon-drift";
        viewModel.Title = "Neon Drift";
        viewModel.Version = "1.0.0";
        viewModel.SourceDirectory = source;
        viewModel.EntryPoint = Path.Combine(source, "juego.exe");

        await viewModel.SubmitAsync();

        Assert.True(viewModel.StatusIsError);
        Assert.Contains("Fallo del servidor real.", viewModel.StatusMessage);
    }
}
