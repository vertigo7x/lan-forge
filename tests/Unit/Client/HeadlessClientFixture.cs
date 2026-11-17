using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using LanForge.Client;

namespace LanForge.UnitTests.Client;

/// <summary>
/// Arranca la aplicacion del cliente en modo headless para poder comprobar la UI
/// (tema, layout y binding) sin depender de una pantalla ni de Windows.
/// </summary>
public sealed class HeadlessClientFixture : IDisposable
{
    private static readonly object Gate = new();
    private static bool _started;

    public HeadlessClientFixture()
    {
        lock (Gate)
        {
            if (_started)
            {
                return;
            }

            // La MainWindow abre su SQLite en el directorio de datos real del usuario.
            // Se redirige a una carpeta temporal para que los tests no toquen el perfil.
            var dataRoot = Path.Combine(Path.GetTempPath(), "lanforge-ui-tests");
            Directory.CreateDirectory(dataRoot);
            Environment.SetEnvironmentVariable(ClientOptions.DataDirectoryVariable, dataRoot);

            AppBuilder.Configure<LanForge.Client.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            _started = true;
        }
    }

    public void Dispose()
    {
        // El headless se inicializa una sola vez por proceso: no se cierra aqui.
    }

    /// <summary>
    /// Deja que termine el trabajo pendiente del hilo de UI (por ejemplo la carga
    /// de ajustes que rellena el campo de nombre al abrir la ventana).
    /// </summary>
    public static void DrainUiQueue()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }
}

/// <summary>
/// Coleccion de tests de UI: no se ejecutan en paralelo entre si porque la
/// aplicacion Avalonia es un singleton por proceso.
/// </summary>
[CollectionDefinition("Cliente-UI", DisableParallelization = true)]
public sealed class ClienteUiCollection : ICollectionFixture<HeadlessClientFixture>;
