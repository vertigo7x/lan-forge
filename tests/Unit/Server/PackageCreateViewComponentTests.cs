using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using LanForge.Server.Application;
using LanForge.Server.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas de componente del formulario de creación de paquetes.
/// La capa de servicio se sustituye por un doble de prueba; no se usa ni el
/// servidor real ni el servicio simulado de producción.
///
/// El fixture arranca Avalonia en headless: sin él, las vistas no se cargan.
/// Comparte la colección "Cliente-UI" con las pruebas de la UI del cliente: las
/// ventanas de Avalonia solo se pueden construir en el hilo de UI, y ese hilo es
/// compartido por toda la aplicación.
/// </summary>
[Collection("Cliente-UI")]
public sealed class PackageCreateViewComponentTests(HeadlessServerFixture fixture) : IDisposable
{
    private readonly TemporaryGameFolder _game = new();

    public void Dispose() => _game.Dispose();

    [Fact]
    public void El_contenedor_resuelve_el_cliente_real_del_endpoint()
    {
        // El formulario no usa la simulacion: la vista recibe el cliente HTTP real
        // desde el contenedor de servicios del servidor.
        var service = fixture.Services.GetRequiredService<IPackageCreationService>();

        Assert.IsType<HttpPackageCreationService>(service);
    }

    [Fact]
    public void Renderiza_todos_los_campos_del_formulario()
    {
        // El fixture deja el contenedor de servicios del servidor disponible.
        Assert.NotNull(fixture.Services);

        var (_, window) = ShowView(new RecordingPackageCreationService());

        var textBoxes = Descendants<TextBox>(window);
        Assert.Equal(6, textBoxes.Length);
        Assert.Contains(textBoxes, box => box.Name == "GameIdBox");
        Assert.Contains(textBoxes, box => box.Name == "TitleBox");
        Assert.Contains(textBoxes, box => box.Name == "VersionBox");
        Assert.Contains(textBoxes, box => box.Name == "SourceDirectoryBox");
        Assert.Contains(textBoxes, box => box.Name == "EntryPointBox");
        Assert.Contains(textBoxes, box => box.Name == "ArgumentsBox");

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void Muestra_un_mensaje_de_error_en_cada_campo_requerido()
    {
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        Submit(view);
        HeadlessAvalonia.RunUiJobs();

        var errors = ErrorTexts(window);
        Assert.Equal("El id del juego es obligatorio.", errors["GameIdErrorText"]);
        Assert.Equal("El título es obligatorio.", errors["TitleErrorText"]);
        Assert.Equal("La versión es obligatoria.", errors["VersionErrorText"]);
        Assert.Equal("Indica la carpeta del juego portable.", errors["SourceDirectoryErrorText"]);
        Assert.Equal("Indica el ejecutable del juego.", errors["EntryPointErrorText"]);
        Assert.Empty(service.Requests);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void Muestra_el_error_de_formato_en_el_campo_correspondiente()
    {
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "id con espacios",
            "Neon Drift",
            "no-es-una-version",
            _game.SourceDirectory,
            _game.ExecutablePath,
            "--fullscreen"));
        Submit(view);
        HeadlessAvalonia.RunUiJobs();

        var errors = ErrorTexts(window);
        Assert.Contains("Id de juego inválido", errors["GameIdErrorText"], StringComparison.Ordinal);
        Assert.Contains("Versión inválida", errors["VersionErrorText"], StringComparison.Ordinal);
        Assert.Empty(service.Requests);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void Muestra_el_error_de_longitud_en_el_campo_correspondiente()
    {
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            new string('t', PackageCreationValidator.MaxTitleLength + 1),
            "1.2.3",
            _game.SourceDirectory,
            _game.ExecutablePath,
            "--fullscreen"));
        Submit(view);
        HeadlessAvalonia.RunUiJobs();

        var errors = ErrorTexts(window);
        Assert.Contains(
            $"{PackageCreationValidator.MaxTitleLength} caracteres",
            errors["TitleErrorText"],
            StringComparison.Ordinal);
        Assert.Empty(service.Requests);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void Envia_los_datos_al_servicio_sustituido()
    {
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            _game.SourceDirectory,
            _game.ExecutablePath,
            "--fullscreen"));
        Submit(view);
        HeadlessAvalonia.RunUiJobs();

        var request = Assert.Single(service.Requests);
        Assert.Equal("neon-drift", request.GameId);
        Assert.Equal("Neon Drift", request.Title);
        Assert.Equal("1.2.3", request.Version);
        Assert.Equal(_game.SourceDirectory, request.SourceDirectory);
        Assert.Equal("game.exe", request.EntryPoint);
        Assert.Equal("--fullscreen", request.Arguments);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void Limpia_el_formulario_tras_crear_el_paquete()
    {
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            _game.SourceDirectory,
            _game.ExecutablePath,
            "--fullscreen"));
        Submit(view);
        HeadlessAvalonia.RunUiJobs();

        var textBoxes = Descendants<TextBox>(window);
        var texts = new string[textBoxes.Length];
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            for (var i = 0; i < textBoxes.Length; i++)
            {
                texts[i] = textBoxes[i].Text ?? string.Empty;
            }
        });
        Assert.All(texts, text => Assert.Equal(string.Empty, text));

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    /// <summary>
    /// Envia el formulario y espera a que termine.
    ///
    /// Todo ocurre en el hilo de UI: al cambiar el estado del view model se disparan
    /// los bindings, que tocan controles del hilo de UI, y RunJobs solo se puede
    /// ejecutar desde ese hilo. El servicio de estas pruebas responde de inmediato,
    /// asi que el envio no se suspende y termina en el mismo hilo.
    /// </summary>
    private static void Submit(PackageCreateView view)
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view.SubmitAsync().GetAwaiter().GetResult();
            HeadlessAvalonia.DrainUiQueue();
        });
    }

    /// <summary>
    /// Crea la vista y la muestra en una ventana. La vista se construye en el hilo
    /// de UI: es su dueño y no se puede adjuntar a una ventana desde otro hilo.
    ///
    /// En headless no hay renderizador, asi que el arbol visual no se realiza: las
    /// pruebas usan el arbol logico, donde los controles y sus bindings si existen.
    /// </summary>
    private static (PackageCreateView View, Window Window) ShowView(IPackageCreationService service)
    {
        PackageCreateView? view = null;
        Window? window = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view = new PackageCreateView(service);
            window = new Window
            {
                Width = 640,
                Height = 700,
                Content = view,
            };
            window.Show();
            window.UpdateLayout();
            HeadlessAvalonia.DrainUiQueue();
            window.UpdateLayout();
        });
        return (view!, window!);
    }

    [Fact]
    public void Muestra_los_botones_de_examinar_del_sistema_operativo()
    {
        var service = new RecordingPackageCreationService();
        var (_, window) = ShowView(service);

        var buttons = Descendants<Button>(window);
        Assert.Contains(buttons, button => button.Name == "PickSourceDirectoryButton");
        Assert.Contains(buttons, button => button.Name == "PickEntryPointButton");

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void El_boton_de_restablecer_esta_presente_y_vinculado_a_su_comando()
    {
        // El boton «Restablecer» vacia el formulario. Existia en la vista pero
        // no tenia ninguna prueba que atara su presencia ni su vinculo con el
        // comando del view model.
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        var reset = Descendants<Button>(window).Single(candidate => candidate.Name == "ResetButton");

        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            Assert.NotNull(reset.Command);
            Assert.Same(view.ViewModel.ResetCommand, reset.Command);
        });

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void El_boton_de_restablecer_vacia_el_formulario()
    {
        // El efecto del boton en la vista real: al pulsarlo, los campos se
        // vacian y el boton de publicar vuelve a quedar deshabilitado.
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            _game.SourceDirectory,
            _game.ExecutablePath,
            "--fullscreen"));

        var reset = Descendants<Button>(window).Single(candidate => candidate.Name == "ResetButton");
        HeadlessAvalonia.RunOnUiThreadAndWait(() => reset.Command?.Execute(null));
        HeadlessAvalonia.RunUiJobs();

        var textBoxes = Descendants<TextBox>(window);
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            Assert.All(textBoxes, box => Assert.Equal(string.Empty, box.Text ?? string.Empty));
        });

        var publish = Descendants<Button>(window).Single(candidate => candidate.Name == "PublishButton");
        var enabled = true;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => enabled = publish.IsEnabled);
        Assert.False(enabled);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void La_carpeta_y_el_ejecutable_no_se_pueden_escribir_a_mano()
    {
        var service = new RecordingPackageCreationService();
        var (_, window) = ShowView(service);

        // Las dos rutas se eligen con los dialogos del sistema operativo: los
        // campos solo muestran lo elegido y no se pueden editar.
        var textBoxes = Descendants<TextBox>(window);
        var sourceDirectory = textBoxes.Single(box => box.Name == "SourceDirectoryBox");
        var entryPoint = textBoxes.Single(box => box.Name == "EntryPointBox");

        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            Assert.True(sourceDirectory.IsReadOnly);
            Assert.True(entryPoint.IsReadOnly);
        });

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void El_boton_de_publicar_esta_deshabilitado_sin_carpeta_ni_ejecutable()
    {
        var service = new RecordingPackageCreationService();
        var (_, window) = ShowView(service);

        var button = Descendants<Button>(window).Single(candidate => candidate.Name == "PublishButton");

        var enabled = true;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => enabled = button.IsEnabled);

        Assert.False(enabled);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void El_boton_de_publicar_esta_deshabilitado_si_la_carpeta_no_existe()
    {
        // El boton se deshabilita y el formulario dice por que: el organizador
        // no puede adivinar que la carpeta que eligio ya no esta en el disco.
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            Path.Combine(_game.SourceDirectory, "no-existe"),
            _game.ExecutablePath,
            "--fullscreen"));

        var button = Descendants<Button>(window).Single(candidate => candidate.Name == "PublishButton");
        var enabled = true;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => enabled = button.IsEnabled);

        Assert.False(enabled);

        var errors = ErrorTexts(window);
        Assert.Equal(
            PackageSourcePaths.MissingSourceDirectoryMessage,
            errors["SourcePathsErrorText"]);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void El_boton_de_publicar_esta_deshabilitado_si_el_ejecutable_no_existe()
    {
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            _game.SourceDirectory,
            Path.Combine(_game.SourceDirectory, "no-existe.exe"),
            "--fullscreen"));

        var button = Descendants<Button>(window).Single(candidate => candidate.Name == "PublishButton");
        var enabled = true;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => enabled = button.IsEnabled);

        Assert.False(enabled);

        var errors = ErrorTexts(window);
        Assert.Equal(
            PackageSourcePaths.MissingExecutableMessage,
            errors["SourcePathsErrorText"]);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void El_boton_de_publicar_esta_deshabilitado_si_el_ejecutable_no_termina_en_exe()
    {
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        var dll = Path.Combine(_game.SourceDirectory, "game.dll");
        File.WriteAllText(dll, "MZ");

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            _game.SourceDirectory,
            dll,
            "--fullscreen"));

        var button = Descendants<Button>(window).Single(candidate => candidate.Name == "PublishButton");
        var enabled = true;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => enabled = button.IsEnabled);

        Assert.False(enabled);

        var errors = ErrorTexts(window);
        Assert.Equal(
            PackageSourcePaths.NotExecutableMessage,
            errors["SourcePathsErrorText"]);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void El_boton_de_publicar_se_habilita_con_una_carpeta_y_un_ejecutable_validos()
    {
        var service = new RecordingPackageCreationService();
        var (view, window) = ShowView(service);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            _game.SourceDirectory,
            _game.ExecutablePath,
            "--fullscreen"));

        var button = Descendants<Button>(window).Single(candidate => candidate.Name == "PublishButton");
        var enabled = false;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => enabled = button.IsEnabled);

        Assert.True(enabled);

        // Con las rutas validas no hay ningun motivo por el que no se pueda
        // publicar: el mensaje queda vacio.
        var errors = ErrorTexts(window);
        Assert.True(string.IsNullOrEmpty(errors["SourcePathsErrorText"]));

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    private static T[] Descendants<T>(Window window) where T : Control
    {
        T[] controls = [];
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
            controls = window.GetLogicalDescendants().OfType<T>().ToArray());
        return controls;
    }

    private static Dictionary<string, string?> ErrorTexts(Window window)
    {
        var errors = new Dictionary<string, string?>();
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            foreach (var text in window.GetLogicalDescendants().OfType<TextBlock>())
            {
                if (!string.IsNullOrEmpty(text.Name))
                {
                    errors[text.Name] = text.Text;
                }
            }
        });
        return errors;
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
                new PackageCreationSummary(
                    request.GameId,
                    request.GameId,
                    request.Title,
                    request.Version,
                    3,
                    100)));
        }
    }
}
