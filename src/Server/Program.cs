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
        // Modo headless: solo el host HTTP, sin UI de Avalonia ni anuncio mDNS.
        // Es el modo que usa la suite e2e de Playwright para levantar el servidor real
        // en un puerto y un directorio de datos propios (variables de entorno).
        if (IsHeadless(args))
        {
            RunHeadlessAsync().GetAwaiter().GetResult();
            return;
        }

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

    /// <summary>
    /// Arranca solo el host HTTP y espera hasta que el proceso recibe una senal de
    /// terminacion. No abre la ventana de Avalonia ni anuncia mDNS: la suite e2e solo
    /// necesita la superficie HTTP del servidor real.
    /// </summary>
    private static async Task RunHeadlessAsync()
    {
        var options = ServerOptions.FromEnvironment();
        var webApp = ServerWebHost.CreateApp(options);

        await webApp.StartAsync();

        // La linea de arranque la lee el gestor de la suite e2e para saber que el
        // servidor ya escucha y en que direccion.
        Console.WriteLine($"LanForge server listening on {options.ListenAddress}:{options.HttpPort}");

        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.TrySetResult();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.TrySetResult();

        await shutdown.Task;
        await webApp.StopAsync();
        await webApp.DisposeAsync();
    }

    /// <summary>Indica si se pidio el modo headless por linea de comandos.</summary>
    private static bool IsHeadless(string[] args) =>
        Array.Exists(args, arg =>
            string.Equals(arg, "--headless", StringComparison.OrdinalIgnoreCase));

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
