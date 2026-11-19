using LanForge.Server.Application;
using LanForge.Server.ViewModels;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas de la validacion de las rutas del formulario de publicacion.
///
/// El boton de publicar se deshabilita cuando la carpeta origen no existe, el
/// ejecutable no existe o el ejecutable no termina en «.exe» (sin distinguir
/// mayusculas). Cuando se deshabilita, el formulario tiene que decir por que.
///
/// La regla vive en <see cref="PackageSourcePaths.Validate"/>: estas pruebas
/// comprueban que el boton, el mensaje del campo y el del formulario usan la
/// misma regla y no pueden divergir.
/// </summary>
public sealed class PackageSourcePathsValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"lanforge-validation-{Guid.NewGuid():N}");

    private readonly string _sourceDirectory;
    private readonly string _executable;

    public PackageSourcePathsValidationTests()
    {
        _sourceDirectory = Path.Combine(_root, "game");
        Directory.CreateDirectory(_sourceDirectory);
        _executable = Path.Combine(_sourceDirectory, "game.exe");
        File.WriteAllText(_executable, "MZ");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ---------- La regla compartida ----------

    [Fact]
    public void La_regla_acepta_una_carpeta_y_un_ejecutable_que_existen()
    {
        Assert.Null(PackageSourcePaths.Validate(_sourceDirectory, _executable));
    }

    [Fact]
    public void La_regla_rechaza_una_carpeta_que_no_existe()
    {
        var error = PackageSourcePaths.Validate(Path.Combine(_root, "no-existe"), _executable);

        Assert.Equal(PackageSourcePaths.MissingSourceDirectoryMessage, error);
    }

    [Fact]
    public void La_regla_rechaza_un_ejecutable_que_no_existe()
    {
        var missing = Path.Combine(_sourceDirectory, "no-existe.exe");

        var error = PackageSourcePaths.Validate(_sourceDirectory, missing);

        Assert.Equal(PackageSourcePaths.MissingExecutableMessage, error);
    }

    [Fact]
    public void La_regla_rechaza_un_ejecutable_que_no_termina_en_exe()
    {
        var dll = Path.Combine(_sourceDirectory, "game.dll");
        File.WriteAllText(dll, "MZ");

        var error = PackageSourcePaths.Validate(_sourceDirectory, dll);

        Assert.Equal(PackageSourcePaths.NotExecutableMessage, error);
    }

    [Fact]
    public void La_regla_rechaza_un_ejecutable_que_no_esta_dentro_de_la_carpeta_origen()
    {
        // El ejecutable forma parte del paquete: si esta fuera de la carpeta que
        // se publica, el boton se habilitaria y el envio fallaria despues con un
        // error que el organizador no puede entender.
        var outside = Path.Combine(_root, "otro.exe");
        File.WriteAllText(outside, "MZ");

        var error = PackageSourcePaths.Validate(_sourceDirectory, outside);

        Assert.Equal(PackageSourcePaths.ExecutableOutsideSourceDirectoryMessage, error);
    }

    [Theory]
    [InlineData("game.exe")]
    [InlineData("GAME.EXE")]
    [InlineData("Game.Exe")]
    public void La_regla_acepta_la_extension_exe_sin_distinguir_mayusculas(string fileName)
    {
        // Windows no distingue mayusculas: un «GAME.EXE» es un ejecutable igual
        // que un «game.exe».
        var executable = Path.Combine(_sourceDirectory, fileName);
        File.WriteAllText(executable, "MZ");

        Assert.Null(PackageSourcePaths.Validate(_sourceDirectory, executable));
    }

    [Fact]
    public void La_regla_rechaza_una_carpeta_vacia()
    {
        var error = PackageSourcePaths.Validate(string.Empty, _executable);

        Assert.Equal(PackageSourcePaths.MissingSourceDirectoryMessage, error);
    }

    [Fact]
    public void La_regla_y_el_calculo_de_la_ruta_relativa_dicen_lo_mismo()
    {
        // El boton se deshabilita con un motivo y, si de todos modos se publica,
        // el calculo de la ruta relativa falla con el mismo texto: el
        // organizador ve el mismo motivo en los dos sitios.
        var outside = Path.Combine(_root, "otro.exe");
        File.WriteAllText(outside, "MZ");

        var error = PackageSourcePaths.Validate(_sourceDirectory, outside);
        var exception = Assert.Throws<ArgumentException>(() =>
            PackageSourcePaths.ToPackageRelativePath(_sourceDirectory, outside));

        // ArgumentException anade el nombre del parametro al mensaje: se
        // compara el texto del motivo, no el envoltorio de la excepcion.
        Assert.Equal(error, exception.Message.Split(" (Parameter")[0]);
        Assert.Equal(PackageSourcePaths.ExecutableOutsideSourceDirectoryMessage, error);
    }

    [Fact]
    public void La_extension_se_comprueba_sin_distinguir_mayusculas()
    {
        // La regla y el filtro del dialogo tienen que admitir las mismas
        // extensiones: si el filtro fuera «*.EXE» y la regla «*.exe», el dialogo
        // dejaria elegir archivos que el formulario despues rechaza.
        Assert.Equal(
            PackageSourcePaths.ExecutablePattern,
            $"*{PackageSourcePaths.ExecutableExtension}");
    }

    [Fact]
    public void La_regla_rechaza_un_ejecutable_vacio()
    {
        var error = PackageSourcePaths.Validate(_sourceDirectory, string.Empty);

        Assert.Equal(PackageSourcePaths.MissingExecutableMessage, error);
    }

    // ---------- El boton de publicar ----------

    [Fact]
    public void El_boton_se_habilita_con_una_carpeta_y_un_ejecutable_que_existen()
    {
        var viewModel = CreateViewModel();

        viewModel.SourceDirectory = _sourceDirectory;
        viewModel.EntryPoint = _executable;

        Assert.True(viewModel.CanPublish);
        Assert.Null(viewModel.SourcePathsError);
    }

    [Fact]
    public void El_boton_se_deshabilita_si_la_carpeta_origen_no_existe()
    {
        var viewModel = CreateViewModel();

        viewModel.SourceDirectory = Path.Combine(_root, "no-existe");
        viewModel.EntryPoint = _executable;

        Assert.False(viewModel.CanPublish);
        Assert.Equal(PackageSourcePaths.MissingSourceDirectoryMessage, viewModel.SourcePathsError);
    }

    [Fact]
    public void El_boton_se_deshabilita_si_el_ejecutable_no_existe()
    {
        var viewModel = CreateViewModel();

        viewModel.SourceDirectory = _sourceDirectory;
        viewModel.EntryPoint = Path.Combine(_sourceDirectory, "no-existe.exe");

        Assert.False(viewModel.CanPublish);
        Assert.Equal(PackageSourcePaths.MissingExecutableMessage, viewModel.SourcePathsError);
    }

    [Fact]
    public void El_boton_se_deshabilita_si_el_ejecutable_no_termina_en_exe()
    {
        var dll = Path.Combine(_sourceDirectory, "game.dll");
        File.WriteAllText(dll, "MZ");
        var viewModel = CreateViewModel();

        viewModel.SourceDirectory = _sourceDirectory;
        viewModel.EntryPoint = dll;

        Assert.False(viewModel.CanPublish);
        Assert.Equal(PackageSourcePaths.NotExecutableMessage, viewModel.SourcePathsError);
    }

    [Theory]
    [InlineData("game.exe")]
    [InlineData("GAME.EXE")]
    public void El_boton_se_habilita_con_la_extension_exe_en_mayusculas(string fileName)
    {
        // La comparacion de la extension no distingue mayusculas: el boton se
        // habilita igual con un «GAME.EXE».
        var executable = Path.Combine(_sourceDirectory, fileName);
        File.WriteAllText(executable, "MZ");
        var viewModel = CreateViewModel();

        viewModel.SourceDirectory = _sourceDirectory;
        viewModel.EntryPoint = executable;

        Assert.True(viewModel.CanPublish);
    }

    [Fact]
    public void El_boton_se_deshabilita_si_el_ejecutable_desaparece_del_disco()
    {
        // El organizador borra el ejecutable entre elegirlo y publicar.
        var viewModel = CreateViewModel();

        viewModel.SourceDirectory = _sourceDirectory;
        viewModel.EntryPoint = _executable;
        Assert.True(viewModel.CanPublish);

        File.Delete(_executable);

        Assert.False(viewModel.CanPublish);
        Assert.Equal(PackageSourcePaths.MissingExecutableMessage, viewModel.SourcePathsError);
    }

    [Fact]
    public void El_mensaje_que_explica_el_boton_deshabilitado_avisa_cuando_cambia()
    {
        // La vista esta ligada a este mensaje: si no se avisa del cambio, el
        // texto se queda con el motivo anterior.
        var viewModel = CreateViewModel();
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        viewModel.SourceDirectory = _sourceDirectory;
        viewModel.EntryPoint = _executable;

        Assert.Contains(nameof(PackageCreateViewModel.SourcePathsError), changes);
        Assert.Contains(nameof(PackageCreateViewModel.CanPublish), changes);
    }

    // ---------- Los dos selectores ----------

    [Fact]
    public async Task Elegir_un_ejecutable_que_no_existe_no_guarda_la_ruta_y_avisa()
    {
        // El dialogo del sistema operativo solo deja elegir archivos que
        // existen, pero un proveedor puede devolver una ruta que no esta en el
        // disco: esa ruta no se guarda en el campo y el organizador recibe un
        // mensaje claro.
        var missing = Path.Combine(_sourceDirectory, "no-existe.exe");
        var viewModel = CreateViewModel(executableToPick: missing);

        await viewModel.PickEntryPointAsync();

        Assert.Equal(string.Empty, viewModel.EntryPoint);
        Assert.True(viewModel.HasEntryPointError);
        Assert.Equal(PackageSourcePaths.MissingExecutableMessage, viewModel.EntryPointError);
        Assert.True(viewModel.StatusIsError);
        Assert.False(viewModel.CanPublish);
    }

    [Fact]
    public async Task Elegir_un_ejecutable_que_no_termina_en_exe_no_guarda_la_ruta_y_avisa()
    {
        var dll = Path.Combine(_sourceDirectory, "game.dll");
        File.WriteAllText(dll, "MZ");
        var viewModel = CreateViewModel(executableToPick: dll);

        await viewModel.PickEntryPointAsync();

        Assert.Equal(string.Empty, viewModel.EntryPoint);
        Assert.True(viewModel.HasEntryPointError);
        Assert.Equal(PackageSourcePaths.NotExecutableMessage, viewModel.EntryPointError);
        Assert.True(viewModel.StatusIsError);
        Assert.False(viewModel.CanPublish);
    }

    [Fact]
    public async Task Elegir_una_carpeta_que_no_existe_no_guarda_la_ruta_y_avisa()
    {
        var viewModel = CreateViewModel(folderToPick: Path.Combine(_root, "no-existe"));

        await viewModel.PickSourceDirectoryAsync();

        Assert.Equal(string.Empty, viewModel.SourceDirectory);
        Assert.True(viewModel.HasSourceDirectoryError);
        Assert.Equal(PackageSourcePaths.MissingSourceDirectoryMessage, viewModel.SourceDirectoryError);
        Assert.True(viewModel.StatusIsError);
        Assert.False(viewModel.CanPublish);
    }

    [Fact]
    public async Task Elegir_el_ejecutable_guarda_la_ruta_absoluta_y_habilita_el_boton()
    {
        var viewModel = CreateViewModel(folderToPick: _sourceDirectory, executableToPick: _executable);

        await viewModel.PickSourceDirectoryAsync();
        await viewModel.PickEntryPointAsync();

        // Las rutas absolutas elegidas se conservan tal cual en los campos.
        Assert.Equal(_sourceDirectory, viewModel.SourceDirectory);
        Assert.Equal(_executable, viewModel.EntryPoint);
        Assert.True(viewModel.CanPublish);
    }

    [Fact]
    public async Task Elegir_el_ejecutable_en_mayusculas_guarda_la_ruta_y_habilita_el_boton()
    {
        var executable = Path.Combine(_sourceDirectory, "GAME.EXE");
        File.WriteAllText(executable, "MZ");
        var viewModel = CreateViewModel(folderToPick: _sourceDirectory, executableToPick: executable);

        await viewModel.PickSourceDirectoryAsync();
        await viewModel.PickEntryPointAsync();

        Assert.Equal(executable, viewModel.EntryPoint);
        Assert.True(viewModel.CanPublish);
    }

    [Fact]
    public async Task Elegir_un_ejecutable_valido_limpia_el_error_anterior()
    {
        var picker = new FakePathPicker
        {
            ExecutableToPick = Path.Combine(_sourceDirectory, "no-existe.exe"),
        };
        var viewModel = new PackageCreateViewModel(new NoopPackageCreationService(), picker);

        await viewModel.PickEntryPointAsync();
        Assert.True(viewModel.HasEntryPointError);

        // El usuario elige un ejecutable que si existe: el error desaparece.
        picker.ExecutableToPick = _executable;
        await viewModel.PickEntryPointAsync();

        Assert.False(viewModel.HasEntryPointError);
        Assert.False(viewModel.StatusIsError);
    }

    // ---------- El payload no cambia ----------

    [Fact]
    public async Task El_envio_conserva_la_carpeta_absoluta_y_convierte_el_ejecutable_en_relativo()
    {
        var service = new RecordingPackageCreationService();
        var picker = new FakePathPicker
        {
            FolderToPick = _sourceDirectory,
            ExecutableToPick = _executable,
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

        // La carpeta origen se envia como la ruta absoluta elegida.
        Assert.Equal(_sourceDirectory, request.SourceDirectory);

        // El ejecutable se envia como la ruta relativa dentro de la carpeta,
        // que es lo que espera el endpoint.
        Assert.Equal("game.exe", request.EntryPoint);
    }

    private static PackageCreateViewModel CreateViewModel(
        string? folderToPick = null,
        string? executableToPick = null) =>
        new(
            new NoopPackageCreationService(),
            new FakePathPicker
            {
                FolderToPick = folderToPick,
                ExecutableToPick = executableToPick,
            });

    private sealed class FakePathPicker : IPathPicker
    {
        public string? FolderToPick { get; set; }

        public string? ExecutableToPick { get; set; }

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
