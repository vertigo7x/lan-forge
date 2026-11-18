using Avalonia.Threading;
using LanForge.Client;
using LanForge.UnitTests.Server;

namespace LanForge.UnitTests.Client;

/// <summary>
/// Arranca la aplicacion del cliente en modo headless para poder comprobar la UI
/// (tema, layout y binding) sin depender de una pantalla ni de Windows.
/// </summary>
public sealed class HeadlessClientFixture : IDisposable
{
    public HeadlessClientFixture()
    {
        // La MainWindow abre su SQLite en el directorio de datos real del usuario.
        // Se redirige a una carpeta temporal para que los tests no toquen el perfil.
        var dataRoot = Path.Combine(Path.GetTempPath(), "lanforge-ui-tests");
        Directory.CreateDirectory(dataRoot);
        Environment.SetEnvironmentVariable(ClientOptions.DataDirectoryVariable, dataRoot);

        // La plataforma headless es un singleton por proceso: cliente y servidor la
        // comparten, y con ella el hilo de UI.
        HeadlessAvalonia.EnsureStarted();

        // La sesion headless crea el hilo de UI y lo mantiene vivo: las ventanas se
        // construyen ahi, y no en el hilo del test.
        _ = HeadlessAvalonia.Session;
    }

    public void Dispose()
    {
        // El headless se inicializa una sola vez por proceso: no se cierra aqui.
    }

    /// <summary>
    /// Deja que termine el trabajo pendiente del hilo de UI (por ejemplo la carga
    /// de ajustes que rellena el campo de nombre al abrir la ventana).
    /// </summary>
    public static void DrainUiQueue() => HeadlessAvalonia.DrainUiQueue();
}

/// <summary>
/// Coleccion de tests de UI: no se ejecutan en paralelo entre si porque la
/// aplicacion Avalonia es un singleton por proceso. Incluye las pruebas de la UI del
/// servidor: las ventanas de Avalonia solo se pueden construir en el hilo de UI, y ese
/// hilo es compartido por toda la aplicacion.
/// </summary>
[CollectionDefinition("Cliente-UI", DisableParallelization = true)]
public sealed class ClienteUiCollection
    : ICollectionFixture<HeadlessClientFixture>, ICollectionFixture<HeadlessServerFixture>;
