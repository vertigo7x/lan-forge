using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using LanForge.Server.Application;
using LanForge.Server.Views;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas de componente del formulario de creación de paquetes.
/// La capa de servicio se sustituye por un doble de prueba; no se usa ni el
/// servidor real ni el servicio simulado de producción.
/// </summary>
[Collection("Servidor-UI")]
public sealed class PackageCreateViewComponentTests
{
    [Fact]
    public void Renderiza_todos_los_campos_del_formulario()
    {
        var window = Show(CreateView(new RecordingPackageCreationService()));

        var textBoxes = Descendants<TextBox>(window);
        Assert.Equal(6, textBoxes.Length);
        Assert.Contains(textBoxes, box => box.Name == "GameIdBox");
        Assert.Contains(textBoxes, box => box.Name == "TitleBox");
        Assert.Contains(textBoxes, box => box.Name == "VersionBox");
        Assert.Contains(textBoxes, box => box.Name == "SourceDirectoryBox");
        Assert.Contains(textBoxes, box => box.Name == "EntryPointBox");
        Assert.Contains(textBoxes, box => box.Name == "ArgumentsBox");

        HeadlessServerFixture.RunOnUiThread(window.Close);
    }

    [Fact]
    public async Task Muestra_un_mensaje_de_error_en_cada_campo_requerido()
    {
        var service = new RecordingPackageCreationService();
        var view = CreateView(service);
        var window = Show(view);

        await view.SubmitAsync();
        HeadlessServerFixture.RunUiJobs();

        var errors = ErrorTexts(window);
        Assert.Equal("El id del juego es obligatorio.", errors["GameIdErrorText"]);
        Assert.Equal("El título es obligatorio.", errors["TitleErrorText"]);
        Assert.Equal("La versión es obligatoria.", errors["VersionErrorText"]);
        Assert.Equal("Indica la carpeta del juego portable.", errors["SourceDirectoryErrorText"]);
        Assert.Equal("Indica el ejecutable del juego.", errors["EntryPointErrorText"]);
        Assert.Empty(service.Requests);

        HeadlessServerFixture.RunOnUiThread(window.Close);
    }

    [Fact]
    public async Task Muestra_el_error_de_formato_en_el_campo_correspondiente()
    {
        var service = new RecordingPackageCreationService();
        var view = CreateView(service);
        var window = Show(view);

        HeadlessServerFixture.RunOnUiThread(() => view.SetFormValues(
            "id con espacios",
            "Neon Drift",
            "no-es-una-version",
            "/games/neon",
            "game.exe",
            "--fullscreen"));
        await view.SubmitAsync();
        HeadlessServerFixture.RunUiJobs();

        var errors = ErrorTexts(window);
        Assert.Contains("Id de juego inválido", errors["GameIdErrorText"], StringComparison.Ordinal);
        Assert.Contains("Versión inválida", errors["VersionErrorText"], StringComparison.Ordinal);
        Assert.Empty(service.Requests);

        HeadlessServerFixture.RunOnUiThread(window.Close);
    }

    [Fact]
    public async Task Muestra_el_error_de_longitud_en_el_campo_correspondiente()
    {
        var service = new RecordingPackageCreationService();
        var view = CreateView(service);
        var window = Show(view);

        HeadlessServerFixture.RunOnUiThread(() => view.SetFormValues(
            "neon-drift",
            new string('t', PackageCreationValidator.MaxTitleLength + 1),
            "1.2.3",
            "/games/neon",
            "game.exe",
            "--fullscreen"));
        await view.SubmitAsync();
        HeadlessServerFixture.RunUiJobs();

        var errors = ErrorTexts(window);
        Assert.Contains(
            $"{PackageCreationValidator.MaxTitleLength} caracteres",
            errors["TitleErrorText"],
            StringComparison.Ordinal);
        Assert.Empty(service.Requests);

        HeadlessServerFixture.RunOnUiThread(window.Close);
    }

    [Fact]
    public async Task Envia_los_datos_al_servicio_sustituido()
    {
        var service = new RecordingPackageCreationService();
        var view = CreateView(service);
        var window = Show(view);

        HeadlessServerFixture.RunOnUiThread(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            "/games/neon",
            "game.exe",
            "--fullscreen"));
        await view.SubmitAsync();
        HeadlessServerFixture.RunUiJobs();

        var request = Assert.Single(service.Requests);
        Assert.Equal("neon-drift", request.GameId);
        Assert.Equal("Neon Drift", request.Title);
        Assert.Equal("1.2.3", request.Version);
        Assert.Equal("/games/neon", request.SourceDirectory);
        Assert.Equal("game.exe", request.EntryPoint);
        Assert.Equal("--fullscreen", request.Arguments);

        HeadlessServerFixture.RunOnUiThread(window.Close);
    }

    [Fact]
    public async Task Limpia_el_formulario_tras_crear_el_paquete()
    {
        var service = new RecordingPackageCreationService();
        var view = CreateView(service);
        var window = Show(view);

        HeadlessServerFixture.RunOnUiThread(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            "/games/neon",
            "game.exe",
            "--fullscreen"));
        await view.SubmitAsync();
        HeadlessServerFixture.RunUiJobs();

        var textBoxes = Descendants<TextBox>(window);
        Assert.All(textBoxes, box => Assert.Equal(string.Empty, box.Text));

        HeadlessServerFixture.RunOnUiThread(window.Close);
    }

    private static PackageCreateView CreateView(IPackageCreationService service) => new(service);

    private static Window Show(Control content)
    {
        Window? window = null;
        HeadlessServerFixture.RunOnUiThread(() =>
        {
            window = new Window
            {
                Width = 640,
                Height = 700,
                Content = content,
            };
            window.Show();
        });
        HeadlessServerFixture.DrainUiQueue();
        return window!;
    }

    private static T[] Descendants<T>(Window window) where T : Control
    {
        T[] controls = [];
        HeadlessServerFixture.RunOnUiThread(() =>
            controls = window.GetVisualDescendants().OfType<T>().ToArray());
        return controls;
    }

    private static Dictionary<string, string?> ErrorTexts(Window window)
    {
        var errors = new Dictionary<string, string?>();
        HeadlessServerFixture.RunOnUiThread(() =>
        {
            foreach (var text in window.GetVisualDescendants().OfType<TextBlock>())
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
