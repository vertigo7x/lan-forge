using LanForge.Server.Application;
using LanForge.Server.ViewModels;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas del flujo de escolha con los dialogos del sistema operativo: el
/// organizador rellena la carpeta origen y el ejecutable sin escribir rutas a mano.
/// </summary>
public sealed class PackageCreateViewModelPickerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"lanforge-picker-{Guid.NewGuid():N}");

    public PackageCreateViewModelPickerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "game"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Elegir_carpeta_deja_la_ruta_absoluta_en_el_campo()
    {
        var picker = new FakePathPicker
        {
            FolderToPick = Path.Combine(_root, "game"),
        };
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService(), picker);

        await viewModel.PickSourceDirectoryAsync();

        Assert.Equal(Path.Combine(_root, "game"), viewModel.SourceDirectory);
    }

    [Fact]
    public async Task Elegir_ejecutable_deja_la_ruta_absoluta_en_el_campo()
    {
        var executable = Path.Combine(_root, "game", "game.exe");
        File.WriteAllText(executable, "MZ");
        var picker = new FakePathPicker { ExecutableToPick = executable };
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService(), picker);

        await viewModel.PickEntryPointAsync();

        Assert.Equal(executable, viewModel.EntryPoint);
    }

    [Fact]
    public async Task Cancelar_el_dialogo_de_carpeta_no_cambia_el_campo()
    {
        var picker = new FakePathPicker();
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService(), picker)
        {
            SourceDirectory = "/ya/escrito",
        };

        await viewModel.PickSourceDirectoryAsync();

        Assert.Equal("/ya/escrito", viewModel.SourceDirectory);
    }

    [Fact]
    public async Task Elegir_una_carpeta_que_no_existe_no_guarda_la_ruta()
    {
        // El dialogo del sistema operativo solo deja elegir carpetas que
        // existen, pero un proveedor puede devolver una ruta que no esta en el
        // disco: esa ruta no se guarda en el campo.
        var picker = new FakePathPicker
        {
            FolderToPick = Path.Combine(_root, "no-existe"),
        };
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService(), picker);

        await viewModel.PickSourceDirectoryAsync();

        Assert.Equal(string.Empty, viewModel.SourceDirectory);
        Assert.True(viewModel.HasSourceDirectoryError);
        Assert.True(viewModel.StatusIsError);
        Assert.False(viewModel.CanPublish);
    }

    [Fact]
    public async Task Elegir_una_carpeta_valida_limpia_el_error_anterior()
    {
        var picker = new FakePathPicker
        {
            FolderToPick = Path.Combine(_root, "no-existe"),
        };
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService(), picker);

        await viewModel.PickSourceDirectoryAsync();
        Assert.True(viewModel.HasSourceDirectoryError);

        // El usuario elige una carpeta que si existe: el error desaparece.
        picker.FolderToPick = Path.Combine(_root, "game");
        await viewModel.PickSourceDirectoryAsync();

        Assert.Equal(Path.Combine(_root, "game"), viewModel.SourceDirectory);
        Assert.False(viewModel.HasSourceDirectoryError);
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public async Task Cancelar_el_dialogo_de_ejecutable_no_cambia_el_campo()
    {
        var picker = new FakePathPicker();
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService(), picker)
        {
            EntryPoint = "game.exe",
        };

        await viewModel.PickEntryPointAsync();

        Assert.Equal("game.exe", viewModel.EntryPoint);
    }

    [Fact]
    public async Task El_envio_convierte_el_ejecutable_absoluto_en_ruta_relativa_del_paquete()
    {
        var sourceDirectory = Path.Combine(_root, "game");
        var executable = Path.Combine(sourceDirectory, "game.exe");
        File.WriteAllText(executable, "MZ");
        var service = new RecordingPackageCreationService();
        var picker = new FakePathPicker
        {
            FolderToPick = sourceDirectory,
            ExecutableToPick = executable,
        };
        var viewModel = new PackageCreateViewModel(service, picker)
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "1.2.3",
        };

        await viewModel.PickSourceDirectoryAsync();
        await viewModel.PickEntryPointAsync();
        await viewModel.SubmitAsync();

        var request = Assert.Single(service.Requests);
        Assert.Equal(sourceDirectory, request.SourceDirectory);
        Assert.Equal("game.exe", request.EntryPoint);
    }

    [Fact]
    public async Task El_envio_no_se_envia_si_el_ejecutable_no_esta_dentro_de_la_carpeta_origen()
    {
        var sourceDirectory = Path.Combine(_root, "game");
        var executable = Path.Combine(_root, "otro.exe");
        File.WriteAllText(executable, "MZ");
        var service = new RecordingPackageCreationService();
        var picker = new FakePathPicker
        {
            FolderToPick = sourceDirectory,
            ExecutableToPick = executable,
        };
        var viewModel = new PackageCreateViewModel(service, picker)
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "1.2.3",
        };

        await viewModel.PickSourceDirectoryAsync();
        await viewModel.PickEntryPointAsync();
        await viewModel.SubmitAsync();

        Assert.Empty(service.Requests);
        Assert.True(viewModel.StatusIsError);
    }

    [Fact]
    public async Task El_boton_se_deshabilita_hasta_elegir_carpeta_y_ejecutable()
    {
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService());

        Assert.False(viewModel.CanPublish);

        var executable = Path.Combine(_root, "game", "game.exe");
        File.WriteAllText(executable, "MZ");
        var picker = new FakePathPicker
        {
            FolderToPick = Path.Combine(_root, "game"),
            ExecutableToPick = executable,
        };
        var conPicker = new PackageCreateViewModel(new NoopPackageCreationService(), picker);

        await conPicker.PickSourceDirectoryAsync();
        Assert.False(conPicker.CanPublish);

        await conPicker.PickEntryPointAsync();
        Assert.True(conPicker.CanPublish);
    }

    [Fact]
    public async Task El_boton_se_deshabilita_si_la_carpeta_elegida_desaparece()
    {
        var sourceDirectory = Path.Combine(_root, "game");
        var executable = Path.Combine(sourceDirectory, "game.exe");
        File.WriteAllText(executable, "MZ");
        var picker = new FakePathPicker
        {
            FolderToPick = sourceDirectory,
            ExecutableToPick = executable,
        };
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService(), picker);

        await viewModel.PickSourceDirectoryAsync();
        await viewModel.PickEntryPointAsync();
        Assert.True(viewModel.CanPublish);

        // El organizador borra la carpeta antes de publicar: el boton se bloquea.
        Directory.Delete(sourceDirectory, recursive: true);

        Assert.False(viewModel.CanPublish);
    }

    [Fact]
    public async Task El_boton_se_deshabilita_si_el_ejecutable_no_termina_en_exe()
    {
        var dll = Path.Combine(_root, "game", "game.dll");
        File.WriteAllText(dll, "MZ");
        var picker = new FakePathPicker { ExecutableToPick = dll };
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService(), picker);

        await viewModel.PickEntryPointAsync();

        Assert.False(viewModel.CanPublish);
        Assert.True(viewModel.HasEntryPointError);
    }

    private sealed class FakePathPicker : IPathPicker
    {
        public string? FolderToPick { get; set; }

        public string? ExecutableToPick { get; init; }

        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(FolderToPick);

        public Task<string?> PickExecutableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ExecutableToPick);
    }

    private sealed class NoopPackageCreationService : IPackageCreationService
    {
        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PackageCreationResult.Success(
                new PackageCreationSummary("neon-drift", "neon-drift", "Neon Drift", "1.2.3", 1, 1)));
    }

    private sealed class RecordingPackageCreationService : IPackageCreationService
    {
        public List<PackageCreationRequest> Requests { get; } = [];

        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(PackageCreationResult.Success(
                new PackageCreationSummary("neon-drift", "neon-drift", "Neon Drift", "1.2.3", 1, 1)));
        }
    }
}
