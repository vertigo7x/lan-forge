using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using LanForge.Server.Application;
using LanForge.Server.Views;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas de componente del formulario de creación de paquetes.
/// La capa de servicio se sustituye por un doble de prueba; no se usa ni el
/// servidor real ni el servicio simulado de producción.
///
/// El fixture arranca Avalonia en headless y monta el host web del servidor:
/// sin él, las vistas no se cargan.
/// </summary>
[Collection("Cliente-UI")]
public sealed class PackageCreateViewComponentTests(HeadlessServerFixture fixture)
{
    [Fact]
    public void Renderiza_todos_los_campos_del_formulario()
    {
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
            "/games/neon",
            "game.exe",
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
            "/games/neon",
            "game.exe",
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
            "/games/neon",
            "game.exe",
            "--fullscreen"));
        Submit(view);
        HeadlessAvalonia.RunUiJobs();

        var request = Assert.Single(service.Requests);
        Assert.Equal("neon-drift", request.GameId);
        Assert.Equal("Neon Drift", request.Title);
        Assert.Equal("1.2.3", request.Version);
        Assert.Equal("/games/neon", request.SourceDirectory);
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
            "/games/neon",
            "game.exe",
            "--fullscreen"));
        Submit(view);
        HeadlessAvalonia.RunUiJobs();

        var textBoxes = Descendants<TextBox>(window);
        var texts = new string[textBoxes.Length];
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            for (var i = 0; i < textBoxes.Length; i++)
            {
                texts[i] = textBoxes[i].Text;
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
