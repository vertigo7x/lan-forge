using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Threading;
using LanForge.Server;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Arranca la aplicación del servidor en modo headless para poder comprobar el
/// formulario de creación de paquetes sin depender de una pantalla.
/// </summary>
public sealed class HeadlessServerFixture : IDisposable
{
    private static readonly object Gate = new();
    private static bool _started;

    public HeadlessServerFixture()
    {
        lock (Gate)
        {
            if (_started)
            {
                return;
            }

            try
            {
                AppBuilder.Configure<App>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Setup was already called", StringComparison.Ordinal))
            {
                // Otra colección de tests ya inicializó Avalonia en este proceso.
            }
            _started = true;
        }
    }

    public void Dispose()
    {
        // El headless se inicializa una sola vez por proceso: no se cierra aquí.
    }

    public static void DrainUiQueue()
    {
        // En los tests de UI el hilo de xUnit no es necesariamente el hilo del
        // dispatcher de Avalonia. Encargar el trabajo al dispatcher evita tocar
        // la composición visual desde otro hilo.
        Dispatcher.UIThread.Post(() => Dispatcher.UIThread.RunJobs());
    }

    /// <summary>
    /// Ejecuta una acción en el hilo de la interfaz de Avalonia. Si la aplicación
    /// cliente es la que inicializó Avalonia, el recurso de fuentes del cliente
    /// no está disponible: en ese caso la acción se ejecuta sin bindings de XAML.
    /// </summary>
    public static void RunOnUiThread(Action action)
    {
        Dispatcher.UIThread.Invoke(action);
    }

    /// <summary>Ejecuta las tareas pendientes del dispatcher de la interfaz.</summary>
    public static void RunUiJobs()
    {
        RunOnUiThread(() => Dispatcher.UIThread.RunJobs());
    }
}

/// <summary>Los tests de UI del servidor no se ejecutan en paralelo entre sí.</summary>
[CollectionDefinition("Servidor-UI", DisableParallelization = true)]
public sealed class ServidorUiCollection : ICollectionFixture<HeadlessServerFixture>;
