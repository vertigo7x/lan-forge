using Avalonia;
using LanForge.Server.Application;
using LanForge.Server.Http;
using LanForge.Server.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.Server;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Servicio HTTP en segundo plano + anuncio mDNS; la UI consume estos datos.
        var options = ServerOptions.Default();
        var webApp = ServerWebHost.CreateApp(options);
        webApp.StartAsync().GetAwaiter().GetResult();

        using var advertiser = new MdnsAdvertiser(
            webApp.Services.GetRequiredService<IServerIdentity>(),
            options.HttpPort);

        App.WebServices = webApp.Services;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
