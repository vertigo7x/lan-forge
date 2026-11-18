using System.Net;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using LanForge.Server.Application;
using LanForge.Server.ViewModels;
using LanForge.Server.Views;
using LanForge.Shared;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas end-to-end del caso de prevención de envío duplicado del formulario de
/// creación de paquetes.
///
/// El recorrido que se prueba es el completo: el usuario rellena el formulario →
/// hace doble click (o varios clicks seguidos) en el botón de envío → el formulario
/// envía UNA sola petición al endpoint de creación de paquetes.
///
/// La UI de LAN Forge es XAML de Avalonia, no HTML: no hay DOM sobre el que hacer
/// `page.route()` ni selectores `data-testid`. Por eso el caso se reparte entre
/// las dos superficies:
///
///  - aquí (xUnit + Avalonia headless) se comprueba la frontera de UI: el
///    formulario real, con el botón real, al que se le pulsan varias veces, y el
///    endpoint sustituido por un stub HTTP que cuenta las peticiones que llegan.
///    La cuenta de peticiones al endpoint es el equivalente del "count de
///    intercepts === 1" que pide el caso.
///  - en `e2e/duplicate-submission.spec.ts` (Playwright) se comprueba la misma
///    propiedad en la frontera HTTP real del servidor.
///
/// Las dos guardas que evitan el envío duplicado son independientes y las dos se
/// verifican aquí:
///
///  1. `DelegateCommand` marca el comando como ocupado mientras se ejecuta: un
///     segundo `Execute` mientras el primero sigue en vuelo no hace nada.
///  2. `PackageCreateViewModel.SubmitAsync` devuelve de inmediato si ya hay un
///     envío en vuelo (`IsSubmitting`), y el botón se deshabilita mientras tanto.
///
/// Además se comprueba el estado de carga: con la petición en vuelo el botón está
/// deshabilitado y el formulario no acepta un segundo envío.
///
/// Nota sobre el ciclo "deshabilitado → rehabilitado": en headless, la
/// continuation awaited desde el hilo de UI no se reanuda (el dispatcher de
/// headless no tiene bucle de ejecución), asi que ese ciclo no se puede
/// completar a través de la vista. Se cubre a nivel de view model en
/// `PackageCreateViewModelEndpointTests.Reactiva_el_envio_despues_de_terminar_la_peticion`.
/// </summary>
[Collection("Cliente-UI")]
public sealed class PackageCreateDuplicateSubmissionE2ETests
{
    /// <summary>
    /// Caso e2e principal: doble click rápido en el botón de envío. El endpoint
    /// tiene que recibir EXACTAMENTE UNA petición, no dos.
    /// </summary>
    [Fact]
    public async Task Un_doble_click_en_el_boton_de_envio_lanza_una_sola_peticion_al_endpoint()
    {
        var handler = new CountingPackageHandler();
        using var service = new HttpPackageCreationService("http://127.0.0.1:1", handler);
        var (view, window) = ShowForm(service);

        FillFormWithValidData(view);

        var button = FindPublishButton(window);

        // El doble click: dos pulsaciones del botón real, sin bombear la cola del
        // dispatcher entre medias. Es lo que hace un usuario que hace doble click.
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            button.Command!.Execute(button.CommandParameter);
            button.Command!.Execute(button.CommandParameter);
        });

        // (a) El endpoint ha recibido UNA sola petición.
        Assert.Equal(1, handler.RequestCount);

        // La petición es la correcta: POST al endpoint de paquetes con los datos
        // del formulario.
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/packages", request.RequestUri?.AbsolutePath);
        Assert.Equal("neon-drift", request.GetValue("gameId"));
        Assert.Equal("Neon Drift", request.GetValue("title"));
        Assert.Equal("1.2.3", request.GetValue("version"));
        Assert.Equal("/games/neon", request.GetValue("sourceDirectory"));
        Assert.Equal("game.exe", request.GetValue("entryPoint"));

        // (b) Estado de carga: con la petición en vuelo el botón está
        // deshabilitado, para que el usuario no pueda volver a pulsarlo.
        Assert.False(ReadOnUiThread(button, b => b.IsEnabled));
        Assert.False(view.ViewModel.CanSubmit);
        Assert.True(view.ViewModel.IsSubmitting);

        // El segundo click no ha colado ni una segunda petición.
        Assert.Equal(1, handler.RequestCount);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    /// <summary>
    /// Muchos clicks seguidos (no solo dos): el usuario que se impacienta y
    /// machaca el botón. El endpoint sigue recibiendo una sola petición.
    /// </summary>
    [Fact]
    public async Task Varios_clicks_seguidos_en_el_boton_de_envio_lanzan_una_sola_peticion()
    {
        var handler = new CountingPackageHandler();
        using var service = new HttpPackageCreationService("http://127.0.0.1:1", handler);
        var (view, window) = ShowForm(service);

        FillFormWithValidData(view);

        var button = FindPublishButton(window);

        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            for (var i = 0; i < 5; i++)
            {
                button.Command!.Execute(button.CommandParameter);
            }
        });

        Assert.Equal(1, handler.RequestCount);

        // Con la petición en vuelo el botón sigue deshabilitado: el usuario ve
        // que el envío está en curso y no puede repetirlo.
        Assert.False(ReadOnUiThread(button, b => b.IsEnabled));

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    /// <summary>
    /// Un click repetido DESPUÉS de que la primera petición ha terminado sí
    /// vuelve a enviar: la prevention es contra el solape, no una congelacion del
    /// formulario. Tras el primer envio el formulario se vacia, asi que se vuelve
    /// a rellenar con datos validos antes del segundo envio.
    ///
    /// El segundo envio se hace sobre el view model (creado fuera del hilo de UI)
    /// porque en headless la continuation awaited desde el hilo de UI no se
    /// reanuda: ver la nota de la clase.
    /// </summary>
    [Fact]
    public async Task Un_envio_posterior_a_que_termine_el_primero_si_se_puede_hacer()
    {
        var handler = new CountingPackageHandler();
        using var service = new HttpPackageCreationService("http://127.0.0.1:1", handler);
        var viewModel = new PackageCreateViewModel(service)
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "1.2.3",
            SourceDirectory = "/games/neon",
            EntryPoint = "game.exe",
            Arguments = "--fullscreen",
        };

        // Primer envio.
        var first = viewModel.SubmitAsync();
        Assert.Equal(1, handler.RequestCount);
        Assert.False(viewModel.CanSubmit);
        Assert.True(viewModel.IsSubmitting);

        handler.Complete();
        await first.WaitAsync(TimeSpan.FromSeconds(5));

        // El primer envio termino: el formulario vuelve a estar disponible.
        Assert.True(viewModel.CanSubmit);
        Assert.False(viewModel.IsSubmitting);

        // Segundo envio: es una peticion nueva, no una duplicada.
        viewModel.GameId = "neon-drift";
        viewModel.Title = "Neon Drift";
        viewModel.Version = "1.2.4";
        viewModel.SourceDirectory = "/games/neon";
        viewModel.EntryPoint = "game.exe";

        var second = viewModel.SubmitAsync();
        Assert.Equal(2, handler.RequestCount);

        handler.Complete();
        await second.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("1.2.4", handler.Requests[1].GetValue("version"));
    }

    /// <summary>
    /// Rellena el formulario con datos válidos.
    /// </summary>
    private static void FillFormWithValidData(PackageCreateView view)
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift",
            "Neon Drift",
            "1.2.3",
            "/games/neon",
            "game.exe",
            "--fullscreen"));
    }

    /// <summary>
    /// Localiza el botón de envío por su nombre en el árbol lógico.
    /// </summary>
    private static Button FindPublishButton(Window window)
    {
        Button? button = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            button = window.GetLogicalDescendants().OfType<Button>()
                .Single(candidate => candidate.Name == "PublishButton");
        });

        return button!;
    }

    /// <summary>
    /// Lee una propiedad del control en el hilo de UI: los controles de Avalonia
    /// pertenecen al hilo que los creó.
    /// </summary>
    private static T ReadOnUiThread<T>(Button button, Func<Button, T> read)
    {
        T value = default!;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => value = read(button));
        return value;
    }

    /// <summary>
    /// Monta el formulario real en una ventana. La vista y su view model nacen en
    /// el hilo de UI: los controles de Avalonia pertenecen al hilo que los crea.
    /// </summary>
    private static (PackageCreateView View, Window Window) ShowForm(IPackageCreationService service)
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

        HeadlessAvalonia.RunOnUiThreadAndWait(() => window!.UpdateLayout());
        return (view!, window!);
    }

    /// <summary>
    /// Stub del endpoint de creación de paquetes: cuenta las peticiones que
    /// llegan y las deja en vuelo hasta que el test las responde.
    ///
    /// Es el equivalente del contador de interceptos de Playwright: lo que se
    /// cuenta es las peticiones que llegan al endpoint, no las llamadas a la capa
    /// de servicio.
    /// </summary>
    private sealed class CountingPackageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _requestCount;

        /// <summary>Peticiones recibidas por el endpoint, en orden.</summary>
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>Número de peticiones recibidas por el endpoint.</summary>
        public int RequestCount => Volatile.Read(ref _requestCount);

        /// <summary>Responde a la petición en vuelo con un paquete creado.</summary>
        public void Complete()
        {
            var json = JsonSerializer.Serialize(
                new
                {
                    packageId = "neon-drift",
                    gameId = "neon-drift",
                    title = "Neon Drift",
                    version = "1.2.3",
                    fileCount = 1,
                    totalSizeBytes = 1,
                },
                SharedJson.Options);

            _completion.TrySetResult(new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, body));

            return await _completion.Task;
        }
    }

    /// <summary>Petición registrada por el stub.</summary>
    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string Body)
    {
        /// <summary>Valor de una propiedad del cuerpo JSON de la petición.</summary>
        public string? GetValue(string property)
        {
            using var document = JsonDocument.Parse(Body);
            return document.RootElement.TryGetProperty(property, out var value)
                ? value.GetString()
                : null;
        }
    }
}
