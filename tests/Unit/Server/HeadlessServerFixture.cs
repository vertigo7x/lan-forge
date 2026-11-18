using LanForge.Server.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Monta el host web del servidor sobre un directorio temporal: las pruebas de la UI
/// del organizador necesitan el contenedor de servicios real (el mismo que usa la
/// aplicacion), pero sin tocar el perfil del usuario.
/// </summary>
public sealed class HeadlessServerFixture : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly WebApplication _app;

    public HeadlessServerFixture()
    {
        // Avalonia en headless es un singleton por proceso: sin el, las vistas no se
        // cargan (InitializeComponent deja el arbol visual vacio).
        HeadlessAvalonia.EnsureStarted();

        // La sesion headless crea el hilo de UI y lo mantiene vivo: las ventanas se
        // construyen ahi, y no en el hilo del test.
        _ = HeadlessAvalonia.Session;

        // Puerto 0: el sistema elige uno libre, asi que el test no depende del 8710.
        _app = ServerWebHost.CreateApp(
            new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1"));
        _app.StartAsync().GetAwaiter().GetResult();

        // La ventana del organizador lee sus servicios de aqui.
        LanForge.Server.App.WebServices = _app.Services;
    }

    public void Dispose()
    {
        try
        {
            _app.StopAsync().GetAwaiter().GetResult();
            _app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (ObjectDisposedException)
        {
            // El host ya estaba cerrado.
        }

        _temp.Dispose();
    }

    /// <summary>Directorio temporal donde vive el estado del host.</summary>
    public string TempPath => _temp.Path;

    /// <summary>Direccion base del host de pruebas.</summary>
    public string Address => _app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.First();

    /// <summary>Servicios del host: el mismo contenedor que usa la UI real.</summary>
    public IServiceProvider Services => _app.Services;
}
