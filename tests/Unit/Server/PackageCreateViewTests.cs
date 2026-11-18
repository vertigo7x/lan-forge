using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using LanForge.Server.Application;
using LanForge.Server.Views;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas de la vista del formulario de creación de paquetes.
/// El fixture arranca Avalonia en headless: sin él, las vistas no se cargan.
/// </summary>
[Collection("Cliente-UI")]
public sealed class PackageCreateViewTests(HeadlessServerFixture fixture)
{
    [Fact]
    public void El_formulario_muestra_todos_los_campos_del_paquete()
    {
        var (_, window) = ShowView(new SuccessfulPackageCreationService());

        TextBox[] textBoxes = [];
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
            textBoxes = window.GetLogicalDescendants().OfType<TextBox>().ToArray());
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
    public void El_boton_de_envio_se_deshabilita_durante_el_envio()
    {
        var service = new BlockingPackageCreationService();
        var (view, window) = ShowView(service);
        Button? button = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            button = window.GetLogicalDescendants().OfType<Button>()
                .Single(candidate => candidate.Name == "PublishButton");
        });

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            "/games/neon",
            "game.exe",
            "--fullscreen"));

        // El envio se hace en el hilo de UI: al cambiar el estado del view model se
        // disparan los bindings, que tocan controles del hilo de UI. El servicio
        // bloquea, asi que el envio se queda esperando la respuesta: el boton tiene
        // que estar deshabilitado mientras tanto.
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var submit = view.SubmitAsync();
            Assert.False(submit.IsCompleted);
        });

        HeadlessAvalonia.DrainUiQueue();
        var disabled = false;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => disabled = button!.IsEnabled);
        Assert.False(disabled);

        // El servicio responde: el envio termina. La continuacion del envio vuelve
        // al hilo de la interfaz, que en headless no se puede bombear desde el hilo
        // de prueba; el ciclo deshabilitado->habilitado lo cubre la prueba del view
        // model (SubmitAsync_deshabilita_el_envio_mientras_espera_al_servicio).
        service.Complete();
        HeadlessAvalonia.DrainUiQueue();
        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
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
        });
        HeadlessAvalonia.DrainUiQueue();
        HeadlessAvalonia.RunOnUiThreadAndWait(() => window!.UpdateLayout());
        return (view!, window!);
    }

    private sealed class SuccessfulPackageCreationService : IPackageCreationService
    {
        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PackageCreationResult.Success(
                new PackageCreationSummary(request.GameId, request.GameId, request.Title, request.Version, 3, 100)));
        }
    }

    private sealed class BlockingPackageCreationService : IPackageCreationService
    {
        private readonly TaskCompletionSource<PackageCreationResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Complete() => _completion.TrySetResult(PackageCreationResult.Success(
            new PackageCreationSummary("neon-drift", "neon-drift", "Neon Drift", "1.2.3", 3, 100)));

        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            return _completion.Task;
        }
    }
}
