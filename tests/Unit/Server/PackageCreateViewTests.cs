using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using LanForge.Server.Application;
using LanForge.Server.Views;

namespace LanForge.UnitTests.Server;

[Collection("Servidor-UI")]
public sealed class PackageCreateViewTests
{
    [Fact]
    public void El_formulario_muestra_todos_los_campos_del_paquete()
    {
        var view = CreateView();
        var window = Show(view);

        TextBox[] textBoxes = [];
        HeadlessAvalonia.RunOnUiThread(() =>
            textBoxes = window.GetVisualDescendants().OfType<TextBox>().ToArray());
        Assert.Equal(6, textBoxes.Length);
        Assert.Contains(textBoxes, box => box.Name == "GameIdBox");
        Assert.Contains(textBoxes, box => box.Name == "TitleBox");
        Assert.Contains(textBoxes, box => box.Name == "VersionBox");
        Assert.Contains(textBoxes, box => box.Name == "SourceDirectoryBox");
        Assert.Contains(textBoxes, box => box.Name == "EntryPointBox");
        Assert.Contains(textBoxes, box => box.Name == "ArgumentsBox");

        HeadlessAvalonia.RunOnUiThread(window.Close);
    }

    [Fact]
    public void El_boton_de_envio_se_deshabilita_durante_el_envio()
    {
        var service = new BlockingPackageCreationService();
        var view = CreateView(service);
        var window = Show(view);
        Button? button = null;
        HeadlessAvalonia.RunOnUiThread(() =>
        {
            button = window.GetVisualDescendants().OfType<Button>()
                .Single(candidate => candidate.Name == "PublishButton");
        });

        HeadlessAvalonia.RunOnUiThread(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            "/games/neon",
            "game.exe",
            "--fullscreen"));
        var submit = view.SubmitAsync();

        HeadlessAvalonia.DrainUiQueue();
        var disabled = false;
        HeadlessAvalonia.RunOnUiThread(() => disabled = button!.IsEnabled);
        Assert.False(disabled);

#pragma warning disable xUnit1031
        service.Complete();
        submit.GetAwaiter().GetResult();
        HeadlessAvalonia.RunUiJobs();
#pragma warning restore xUnit1031

        var enabled = false;
        HeadlessAvalonia.RunOnUiThread(() => enabled = button!.IsEnabled);
        Assert.True(enabled);
        HeadlessAvalonia.RunOnUiThread(window.Close);
    }

    private static PackageCreateView CreateView(IPackageCreationService? service = null)
    {
        return new PackageCreateView(service ?? new SuccessfulPackageCreationService());
    }

    private static Window Show(Control content)
    {
        Window? window = null;
        HeadlessAvalonia.RunOnUiThread(() =>
        {
            window = new Window
            {
                Width = 640,
                Height = 700,
                Content = content,
            };
            window.Show();
        });
        HeadlessAvalonia.DrainUiQueue();
        return window!;
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
