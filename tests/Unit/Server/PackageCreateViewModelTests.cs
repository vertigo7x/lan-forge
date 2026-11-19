using LanForge.Server.Application;
using LanForge.Server.ViewModels;

namespace LanForge.UnitTests.Server;

public sealed class PackageCreateViewModelTests : IDisposable
{
    private readonly TemporaryGameFolder _game = new();

    public void Dispose() => _game.Dispose();

    [Fact]
    public async Task SubmitAsync_invalida_en_cliente_y_no_llama_al_servicio()
    {
        var service = new RecordingPackageCreationService();
        var viewModel = new PackageCreateViewModel(service);

        await viewModel.SubmitAsync();

        Assert.Empty(service.Requests);
        Assert.True(viewModel.HasGameIdError);
        Assert.True(viewModel.HasTitleError);
        Assert.True(viewModel.HasVersionError);
        Assert.True(viewModel.HasSourceDirectoryError);
        Assert.True(viewModel.HasEntryPointError);
        Assert.False(viewModel.IsSubmitting);
        Assert.True(viewModel.StatusIsError);
    }

    [Fact]
    public async Task SubmitAsync_envia_el_formulario_al_servicio()
    {
        var service = new RecordingPackageCreationService();
        var viewModel = CreateValidViewModel(service);

        await viewModel.SubmitAsync();

        var request = Assert.Single(service.Requests);
        Assert.Equal("neon-drift", request.GameId);
        Assert.Equal("Neon Drift", request.Title);
        Assert.Equal("1.2.3", request.Version);
        Assert.Equal(_game.SourceDirectory, request.SourceDirectory);
        Assert.Equal("game.exe", request.EntryPoint);
        Assert.Equal("--fullscreen", request.Arguments);
    }

    [Fact]
    public async Task SubmitAsync_limpia_el_formulario_tras_crear_el_paquete()
    {
        var service = new RecordingPackageCreationService();
        var viewModel = CreateValidViewModel(service);

        await viewModel.SubmitAsync();

        Assert.Equal(string.Empty, viewModel.GameId);
        Assert.Equal(string.Empty, viewModel.Title);
        Assert.Equal(string.Empty, viewModel.Version);
        Assert.Equal(string.Empty, viewModel.SourceDirectory);
        Assert.Equal(string.Empty, viewModel.EntryPoint);
        Assert.Equal(string.Empty, viewModel.Arguments);
        Assert.False(viewModel.StatusIsError);
        Assert.NotNull(viewModel.StatusMessage);
    }

    [Fact]
    public async Task SubmitAsync_muestra_los_errores_por_campo_devueltos_por_el_servicio()
    {
        var service = new RecordingPackageCreationService
        {
            Result = PackageCreationResult.Invalid(new Dictionary<string, string>
            {
                [PackageCreationFields.Version] = "Esa versión ya existe.",
            })
        };
        var viewModel = CreateValidViewModel(service);

        await viewModel.SubmitAsync();

        Assert.Equal("Esa versión ya existe.", viewModel.VersionError);
        Assert.True(viewModel.HasVersionError);
        Assert.False(viewModel.HasGameIdError);
        Assert.True(viewModel.StatusIsError);
        Assert.False(viewModel.IsSubmitting);
    }

    [Fact]
    public async Task SubmitAsync_deshabilita_el_envio_mientras_espera_al_servicio()
    {
        var service = new BlockingPackageCreationService();
        var viewModel = CreateValidViewModel(service);

        var submit = viewModel.SubmitAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsSubmitting);
        Assert.False(viewModel.CanSubmit);

        service.Complete();
        await submit;

        Assert.False(viewModel.IsSubmitting);
        Assert.True(viewModel.CanSubmit);
    }

    private PackageCreateViewModel CreateValidViewModel(IPackageCreationService service)
    {
        var viewModel = new PackageCreateViewModel(service)
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "1.2.3",
            SourceDirectory = _game.SourceDirectory,
            EntryPoint = _game.ExecutablePath,
            Arguments = "--fullscreen",
        };
        return viewModel;
    }

    private sealed class RecordingPackageCreationService : IPackageCreationService
    {
        public List<PackageCreationRequest> Requests { get; } = [];

        public PackageCreationResult Result { get; set; } = PackageCreationResult.Success(
            new PackageCreationSummary("neon-drift", "neon-drift", "Neon Drift", "1.2.3", 3, 100));

        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class BlockingPackageCreationService : IPackageCreationService
    {
        private readonly TaskCompletionSource<PackageCreationResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Complete() => _completion.TrySetResult(
            PackageCreationResult.Success(
                new PackageCreationSummary("neon-drift", "neon-drift", "Neon Drift", "1.2.3", 3, 100)));
    }
}
