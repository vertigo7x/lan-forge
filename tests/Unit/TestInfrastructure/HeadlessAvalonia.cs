using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

namespace LanForge.UnitTests;

/// <summary>
/// Arranque de Avalonia en headless. La aplicacion Avalonia es un singleton por
/// proceso: <c>SetupWithoutStarting</c> solo puede llamarse una vez y las ventanas
/// solo se pueden construir en el hilo de UI, asi que cliente y servidor comparten
/// este arranque en lugar de inicializar cada uno por su cuenta.
///
/// La aplicacion que se registra es la del cliente: su <c>Theme.axaml</c> trae los
/// recursos que usa la UI del cliente. La ventana del servidor no depende de ellos
/// (solo usa el tema Fluent).
/// </summary>
public static class HeadlessAvalonia
{
    private static readonly object Gate = new();
    private static HeadlessUnitTestSession? _session;

    /// <summary>
    /// Sesion headless de la prueba: mantiene vivo el hilo de UI de Avalonia y permite
    /// despachar el trabajo de la UI desde el hilo del test.
    /// </summary>
    public static HeadlessUnitTestSession Session
    {
        get
        {
            lock (Gate)
            {
                return _session ??= HeadlessUnitTestSession.GetOrStartForAssembly(
                    Assembly.GetExecutingAssembly());
            }
        }
    }

    /// <summary>
    /// Registra la plataforma headless con la aplicacion del cliente. Debe llamarse
    /// una sola vez por proceso: la segunda llamada no hace nada.
    /// </summary>
    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (Avalonia.Application.Current is not null)
            {
                return;
            }

            AppBuilder.Configure<LanForge.Client.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
        }
    }

    /// <summary>
    /// Deja que termine el trabajo pendiente del hilo de UI (por ejemplo la carga
    /// asincrona de los ajustes o del catalogo).
    /// </summary>
    public static void DrainUiQueue()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
    }

    /// <summary>
    /// Ejecuta una sola vez las tareas pendientes del hilo de UI, sin esperar. Se usa
    /// cuando el test solo necesita vaciar la cola del dispatcher, no esperar a que
    /// termine una carga asincrona.
    /// </summary>
    public static void RunUiJobs() => Dispatcher.UIThread.RunJobs();

    /// <summary>
    /// Ejecuta la accion en el hilo de UI de Avalonia. Las ventanas solo se pueden
    /// construir en ese hilo: un <see cref="Avalonia.Controls.Window"/> construido desde
    /// otro lanza "the calling thread cannot access this object".
    /// </summary>
    /// <remarks>
    /// <c>HeadlessUnitTestSession.Dispatch</c> se traga las excepciones de la accion:
    /// si una asercion falla dentro, el test pasaria sin comprobar nada. Por eso el
    /// fallo se captura aqui y se vuelve a lanzar en el hilo del test, donde xUnit si
    /// la ve.
    /// </remarks>
    public static void RunOnUiThread(Action action)
    {
        Exception? failure = null;
        Session.Dispatch(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }, CancellationToken.None);

        if (failure is not null)
        {
            throw new InvalidOperationException(
                "La prueba de UI fallo en el hilo de UI de Avalonia.", failure);
        }
    }

    /// <summary>
    /// Ejecuta la accion asincrona en el hilo de UI de Avalonia y propaga al hilo del
    /// test cualquier fallo.
    /// </summary>
    public static async Task RunOnUiThreadAsync(Func<Task> action)
    {
        Exception? failure = null;
        await Session.Dispatch(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }, CancellationToken.None);

        if (failure is not null)
        {
            throw new InvalidOperationException(
                "La prueba de UI fallo en el hilo de UI de Avalonia.", failure);
        }
    }
}
