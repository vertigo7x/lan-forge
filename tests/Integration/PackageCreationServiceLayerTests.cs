using LanForge.Server.Application;
using LanForge.Server.Http;
using LanForge.Server.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>
/// Capa de servicio del formulario de creación de paquetes: debe poder resolverse
/// desde el contenedor de servicios (cualquier componente la usa) y admitir una
/// implementación alternativa sin tocar al consumidor.
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

        var service = app.Services.GetRequiredService<IPackageCreationService>();

        Assert.NotNull(service);
        Assert.IsType<MockPackageCreationService>(service);

        // La vista y el view model solo dependen de la interfaz: se puede
        // construir el formulario con el servicio resuelto del contenedor.
        var viewModel = new PackageCreateViewModel(service);
        viewModel.GameId = "neon-drift";
        viewModel.Title = "Neon Drift";
        viewModel.Version = "1.0.0";
        viewModel.SourceDirectory = "/srv/juegos/neon-drift";
        viewModel.EntryPoint = "juego.exe";

        await viewModel.SubmitAsync();

        Assert.False(viewModel.StatusIsError);
        Assert.True(viewModel.HasStatusMessage);
    }

    [Fact]
    public async Task Una_implementacion_alternativa_sustituye_al_mock_sin_tocar_al_consumidor()
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
        viewModel.GameId = "neon-drift";
        viewModel.Title = "Neon Drift";
        viewModel.Version = "1.0.0";
        viewModel.SourceDirectory = "/srv/juegos/neon-drift";
        viewModel.EntryPoint = "juego.exe";

        await viewModel.SubmitAsync();

        Assert.True(viewModel.StatusIsError);
        Assert.Contains("Fallo del servidor real.", viewModel.StatusMessage);
    }
}
