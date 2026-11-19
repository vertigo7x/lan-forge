using LanForge.Server.Application;
using LanForge.Server.ViewModels;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas del view model del formulario de creación de paquetes enfocadas en la
/// integracion con el endpoint real: envio unico, errores por campo del servidor y
/// conservacion del mensaje del servidor al editar el campo.
/// </summary>
public sealed class PackageCreateViewModelEndpointTests : IDisposable
{
    private readonly TemporaryGameFolder _game = new();
    private readonly TemporaryGameFolder _otherGame = new();

    public void Dispose()
    {
        _game.Dispose();
        _otherGame.Dispose();
    }

    [Fact]
    public async Task Ignora_un_segundo_envio_mientras_hay_una_peticion_en_vuelo()
    {
        var service = new BlockingPackageCreationService();
        var viewModel = CreateValidViewModel(service);

        var first = viewModel.SubmitAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Segundo envio mientras el primero sigue en vuelo: no debe pasar nada.
        await viewModel.SubmitAsync();

        Assert.Equal(1, service.CallCount);

        service.Complete();
        await first;

        Assert.Equal(1, service.CallCount);
        Assert.True(viewModel.CanSubmit);
        Assert.False(viewModel.IsSubmitting);
    }

    [Fact]
    public async Task Reactiva_el_envio_despues_de_terminar_la_peticion()
    {
        var service = new BlockingPackageCreationService();
        var viewModel = CreateValidViewModel(service);

        var submit = viewModel.SubmitAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.CanSubmit);

        service.Complete();
        await submit;

        Assert.True(viewModel.CanSubmit);
        Assert.False(viewModel.IsSubmitting);
    }

    [Fact]
    public async Task Muestra_el_error_del_servidor_junto_al_campo_afectado()
    {
        var service = new RecordingPackageCreationService
        {
            Result = PackageCreationResult.Invalid(
                new Dictionary<string, string>
                {
                    [PackageCreationFields.SourceDirectory] = "La carpeta '/games/no-existe' no existe en el servidor.",
                    [PackageCreationFields.EntryPoint] = "El ejecutable 'game.exe' no esta en el paquete.",
                },
                "La solicitud no cumple las validaciones del paquete."),
        };
        var viewModel = CreateValidViewModel(service);

        await viewModel.SubmitAsync();

        Assert.Equal("La carpeta '/games/no-existe' no existe en el servidor.", viewModel.SourceDirectoryError);
        Assert.Equal("El ejecutable 'game.exe' no esta en el paquete.", viewModel.EntryPointError);
        Assert.False(viewModel.HasGameIdError);
        Assert.True(viewModel.StatusIsError);
        Assert.Equal("La solicitud no cumple las validaciones del paquete.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Conserva_el_error_del_servidor_al_editar_el_campo_afectado()
    {
        var service = new RecordingPackageCreationService
        {
            Result = PackageCreationResult.Invalid(
                new Dictionary<string, string>
                {
                    [PackageCreationFields.SourceDirectory] = "La carpeta '/games/no-existe' no existe en el servidor.",
                }),
        };
        var viewModel = CreateValidViewModel(service);

        await viewModel.SubmitAsync();
        Assert.Equal("La carpeta '/games/no-existe' no existe en el servidor.", viewModel.SourceDirectoryError);

        // El usuario elige otra carpeta que si existe: el cliente ya no se queja,
        // pero el mensaje del servidor sigue siendo el dato valido hasta el
        // siguiente envio.
        viewModel.SourceDirectory = _otherGame.SourceDirectory;

        Assert.Equal("La carpeta '/games/no-existe' no existe en el servidor.", viewModel.SourceDirectoryError);
    }

    [Fact]
    public async Task El_error_de_cliente_toma_el_lugar_al_mensaje_del_servidor_al_editar()
    {
        var service = new RecordingPackageCreationService
        {
            Result = PackageCreationResult.Invalid(
                new Dictionary<string, string>
                {
                    [PackageCreationFields.Version] = "Esa version ya existe.",
                }),
        };
        var viewModel = CreateValidViewModel(service);

        await viewModel.SubmitAsync();
        Assert.Equal("Esa version ya existe.", viewModel.VersionError);

        // El usuario escribe una version invalida: el mensaje del cliente manda,
        // porque describe el valor actual del campo.
        viewModel.Version = "no-es-una-version";

        Assert.NotEqual("Esa version ya existe.", viewModel.VersionError);
        Assert.Contains("Versión inválida", viewModel.VersionError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Limpia_el_error_del_servidor_al_enviar_de_nuevo()
    {
        var service = new RecordingPackageCreationService
        {
            Result = PackageCreationResult.Invalid(
                new Dictionary<string, string>
                {
                    [PackageCreationFields.SourceDirectory] = "La carpeta no existe.",
                }),
        };
        var viewModel = CreateValidViewModel(service);

        await viewModel.SubmitAsync();
        Assert.True(viewModel.HasSourceDirectoryError);

        // Segundo envio: el servicio ahora responde con exito, asi que los errores
        // del envio anterior desaparecen.
        service.Result = PackageCreationResult.Success(
            new PackageCreationSummary("neon-drift", "neon-drift", "Neon Drift", "1.2.3", 3, 100));
        await viewModel.SubmitAsync();

        Assert.False(viewModel.HasSourceDirectoryError);
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public async Task Muestra_el_error_general_cuando_el_servicio_no_puede_contactar()
    {
        var service = new RecordingPackageCreationService
        {
            Result = PackageCreationResult.Failure("No se ha podido contactar con el servidor: error de red."),
        };
        var viewModel = CreateValidViewModel(service);

        await viewModel.SubmitAsync();

        Assert.True(viewModel.StatusIsError);
        Assert.Equal("No se ha podido contactar con el servidor: error de red.", viewModel.StatusMessage);
        Assert.False(viewModel.HasGameIdError);
        Assert.False(viewModel.HasSourceDirectoryError);
        Assert.True(viewModel.CanSubmit);
    }

    private PackageCreateViewModel CreateValidViewModel(IPackageCreationService service)
    {
        return new PackageCreateViewModel(service)
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "1.2.3",
            SourceDirectory = _game.SourceDirectory,
            EntryPoint = _game.ExecutablePath,
            Arguments = "--fullscreen",
        };
    }

    private sealed class RecordingPackageCreationService : IPackageCreationService
    {
        public List<PackageCreationRequest> Requests { get; } = [];

        public int CallCount => Requests.Count;

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

        private int _callCount;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => _callCount;

        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            Started.TrySetResult();
            return _completion.Task;
        }

        public void Complete() => _completion.TrySetResult(
            PackageCreationResult.Success(
                new PackageCreationSummary("neon-drift", "neon-drift", "Neon Drift", "1.2.3", 3, 100)));
    }
}
