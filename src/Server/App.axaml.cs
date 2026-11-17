using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LanForge.Server.Http;

namespace LanForge.Server;

public partial class App : Avalonia.Application
{
    /// <summary>Servicios del host web, disponibles para la UI (Program los asigna antes de arrancar Avalonia).</summary>
    public static IServiceProvider? WebServices { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
