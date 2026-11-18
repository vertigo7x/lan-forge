using System.Net.Http.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using LanForge.Server.Application;
using LanForge.Server.Http;
using LanForge.Server.Views;
using LanForge.Shared;
using LanForge.Shared.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Prueba end-to-end del caso de exito del flujo de creacion de paquetes.
///
/// A diferencia de <see cref="PackageCreateViewComponentTests"/> (que sustituye la
/// capa de servicio por un doble de prueba), aqui no hay dobles: la vista real de
/// Avalonia, con sus controles reales, envia el formulario contra el endpoint real
/// (<c>POST /api/v1/packages</c>) de un Kestrel de verdad.
///
/// El recorrido es el del usuario, de principio a fin:
/// <list type="number">
/// <item>Se rellenan TODOS los campos del formulario con datos validos.</item>
/// <item>Se pulsa el boton real de crear paquete.</item>
/// <item>Se comprueba que la UI muestra la confirmacion esperada.</item>
/// <item>Se comprueba que la peticion que llego al endpoint fue la correcta.</item>
/// <item>Se comprueba que el paquete quedo publicado de verdad en el servidor.</item>
/// </list>
///
/// La verificacion de la red se hace en el equivalente de <c>page.route()</c> para
/// esta arquitectura: <see cref="SynchronousHttpHandler"/> observa la peticion HTTP
/// real que sale del cliente, la anota y la deja pasar hacia el servidor real. No
/// se sustituye la red por un doble: la peticion llega al endpoint de verdad y es
/// el servidor real el que responde.
///
/// La UI es XAML de Avalonia y no tiene DOM, asi que los "selectores estables" son
/// los <c>x:Name</c> de los controles (el equivalente de <c>data-testid</c>), y la
/// espera se hace sobre el elemento (el mensaje de confirmacion), nunca con una
/// duracion fija.
///
/// Nota sobre la coleccion: la UI de Avalonia es un singleton por proceso y su
/// hilo lo comparten el cliente y el servidor. La coleccion "Cliente-UI" es la que
/// declara las dos fixtures (la del cliente y la del servidor), asi que es la que
/// debe usarse para las pruebas que tocan la UI: si dos colecciones arrancan la
/// sesion a la vez, gana la que llegue primero y la otra acaba construyendo sus
/// ventanas en el hilo equivocado.
///
/// Nota sobre las esperas: la sesion headless de Avalonia instala su
/// <c>SynchronizationContext</c> en el hilo del test, y ahi las continuaciones
/// nunca se ejecutan (el dispatcher headless no las bombea). Por eso la prueba no
/// hace <c>await</c>: las llamadas HTTP se lanzan desde un hilo del pool y se
/// esperan de forma sincrona.
/// </summary>
[Collection("Cliente-UI")]
public sealed class PackageCreateSuccessE2ETests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly WebApplication _app;
    private readonly SynchronousHttpHandler _handler = new();
    private readonly HttpPackageCreationService _service;
    private Window? _window;
    private PackageCreateView? _view;

    public PackageCreateSuccessE2ETests()
    {
        // El servidor real, con puerto 0: el sistema elige uno libre. Reservar un
        // puerto y soltarlo antes de que Kestrel lo ocupe deja una carrera: otra
        // prueba puede cogerlo entre medias. Con el puerto 0 no hay carrera, y la
        // direccion real se lee despues del arranque.
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        _app = ServerWebHost.CreateApp(options);
        _app.StartAsync().GetAwaiter().GetResult();

        // Avalonia en headless es un singleton por proceso: sin el, las vistas no se
        // cargan (InitializeComponent deja el arbol visual vacio).
        HeadlessAvalonia.EnsureStarted();
        _ = HeadlessAvalonia.Session;

        // El cliente HTTP que usa la UI en produccion, con el manejador que observa
        // la peticion y la deja llegar al servidor.
        _service = new HttpPackageCreationService(ServerAddress, _handler);
    }

    public void Dispose()
    {
        try
        {
            HeadlessAvalonia.RunOnUiThreadAndWait(() => _window?.Close());
        }
        catch (Exception)
        {
            // La ventana ya estaba cerrada.
        }

        _service.Dispose();
        _handler.Dispose();

        try
        {
            // La parada se lanza desde un hilo del pool: el hilo del test tiene el
            // SynchronizationContext de Avalonia y ahi las continuaciones no se
            // ejecutan, con lo que la parada se quedaria esperando para siempre.
            Task.Run(() => _app.StopAsync()).GetAwaiter().GetResult();
            Task.Run(() => _app.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
        catch (ObjectDisposedException)
        {
            // El host ya estaba cerrado.
        }

        _temp.Dispose();
    }

    [Fact]
    public void Crear_el_paquete_desde_el_formulario_publica_el_paquete_y_muestra_la_confirmacion()
    {
        // Carpeta origen real con el ejecutable que declara el formulario: el
        // servidor valida el contenido, lo hashea y lo importa de verdad.
        var sourceDirectory = _temp.CreateSourceDirectory(
            ("neon-drift.exe", "contenido-e2e"),
            ("data/pak0.bin", "datos-e2e"));
        const string gameId = "neon-drift";

        var view = ShowView(_service);

        // ---- 1. Rellenar TODOS los campos del formulario ----
        FillForm(view, gameId, "Neon Drift", "1.2.3", sourceDirectory, "neon-drift.exe", "--fullscreen");

        // El formulario esta completo: el boton tiene que estar habilitado.
        Assert.True(PublishButtonEnabled());

        // ---- 2. Enviar el formulario pulsando el boton real ----
        ClickPublish();

        // ---- 3. La UI muestra la confirmacion esperada ----
        var status = WaitForStatusMessage();
        Assert.Equal("Paquete creado: Neon Drift v1.2.3 (2 archivos).", status);
        Assert.False(view.ViewModel.StatusIsError);
        Assert.True(view.ViewModel.HasStatusMessage);

        // Tras el exito el formulario se vacia para poder meter el siguiente juego.
        Assert.Equal(string.Empty, TextBoxText("GameIdBox"));
        Assert.Equal(string.Empty, TextBoxText("TitleBox"));
        Assert.Equal(string.Empty, TextBoxText("VersionBox"));
        Assert.Equal(string.Empty, TextBoxText("SourceDirectoryBox"));
        Assert.Equal(string.Empty, TextBoxText("EntryPointBox"));
        Assert.Equal(string.Empty, TextBoxText("ArgumentsBox"));

        // ---- 4. La peticion que llego al endpoint fue la correcta ----
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/packages", request.Path);

        // La UI identifica al cliente con su cabecera de correlacion.
        Assert.Equal(HttpPackageCreationService.ClientId, request.ClientId);

        // El cuerpo lleva los seis campos del formulario, sin recortes de espacio.
        var body = request.BodyJson;
        Assert.NotNull(body);
        Assert.Equal(gameId, body.Value.GetProperty("gameId").GetString());
        Assert.Equal("Neon Drift", body.Value.GetProperty("title").GetString());
        Assert.Equal("1.2.3", body.Value.GetProperty("version").GetString());
        Assert.Equal(sourceDirectory, body.Value.GetProperty("sourceDirectory").GetString());
        Assert.Equal("neon-drift.exe", body.Value.GetProperty("entryPoint").GetString());
        Assert.Equal("--fullscreen", body.Value.GetProperty("arguments").GetString());

        // ---- 5. El servidor publico el paquete de verdad ----
        var catalog = CatalogAsync().GetAwaiter().GetResult();
        var entry = Assert.Single(catalog.Where(c => c.GameId == gameId));
        Assert.Equal("Neon Drift", entry.Title);
        Assert.Equal("1.2.3", entry.Version);

        // Y el manifiesto firmado se sirve con el ejecutable que declaro el formulario.
        var manifestBody = FetchManifest(gameId).GetAwaiter().GetResult();
        Assert.Contains("\"entryPoint\":\"neon-drift.exe\"", manifestBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rellena los seis campos del formulario escribiendo en los <c>TextBox</c>
    /// reales. Se escribe en el control, no en el view model, para que el binding
    /// bidireccional sea el que propaga el valor: es el camino que recorre el
    /// usuario al teclear.
    ///
    /// Todos los controles se tocan dentro de una sola accion de UI: anidar un
    /// despacho dentro de otra accion de UI se bloquea, porque la sesion headless
    /// encola la accion interna detras de la que ya esta ejecutandose.
    /// </summary>
    private void FillForm(
        PackageCreateView view,
        string gameId,
        string title,
        string version,
        string sourceDirectory,
        string entryPoint,
        string arguments)
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            TextBox("GameIdBox").Text = gameId;
            TextBox("TitleBox").Text = title;
            TextBox("VersionBox").Text = version;
            TextBox("SourceDirectoryBox").Text = sourceDirectory;
            TextBox("EntryPointBox").Text = entryPoint;
            TextBox("ArgumentsBox").Text = arguments;

            // Los bindings se propagan en el hilo de UI: hay que vaciar la cola
            // del dispatcher para que el view model vea los valores.
            HeadlessAvalonia.RunUiJobs();
        });

        // El view model tiene que ver los valores: si el binding no llegara, el
        // formulario se enviaria vacio y la prueba no significaria nada.
        Assert.Equal(gameId, view.ViewModel.GameId);
        Assert.Equal(title, view.ViewModel.Title);
        Assert.Equal(version, view.ViewModel.Version);
        Assert.Equal(sourceDirectory, view.ViewModel.SourceDirectory);
        Assert.Equal(entryPoint, view.ViewModel.EntryPoint);
        Assert.Equal(arguments, view.ViewModel.Arguments);
    }

    /// <summary>Pulsa el boton real de crear paquete.</summary>
    private void ClickPublish()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            PublishButton().Command?.Execute(null);

            // El comando se ejecuta de forma asincrona: hay que vaciar la cola del
            // dispatcher para que el envio llegue a completarse.
            HeadlessAvalonia.RunUiJobs();
        });
    }

    /// <summary>
    /// Espera a que la UI muestre un mensaje de estado. La espera es sobre el
    /// elemento (el texto de la confirmacion), no sobre una duracion fija: si la
    /// peticion al servidor tarda mas, la prueba sigue siendo correcta.
    /// </summary>
    private string WaitForStatusMessage()
    {
        var limit = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < limit)
        {
            var message = StatusMessageText();
            if (!string.IsNullOrEmpty(message))
            {
                return message;
            }

            // La continuacion del envio vuelve al hilo de UI: hay que bombear la
            // cola del dispatcher para que se ejecute.
            HeadlessAvalonia.RunOnUiThreadAndWait(() => HeadlessAvalonia.RunUiJobs());
            Thread.Sleep(10);
        }

        throw new TimeoutException(
            "El formulario no mostro ningun mensaje de estado tras enviarse.");
    }

    /// <summary>Texto del mensaje de estado que muestra la UI.</summary>
    private string StatusMessageText()
    {
        var text = string.Empty;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => text = StatusMessageOnUiThread());
        return text;
    }

    /// <summary>
    /// Localiza un control por su nombre estable (<c>x:Name</c> en el XAML, el
    /// equivalente de <c>data-testid</c>). Solo se puede llamar desde el hilo de
    /// UI: por eso no hace falta despachar aqui.
    /// </summary>
    private TextBox TextBox(string name) =>
        _window!.GetLogicalDescendants().OfType<TextBox>()
            .Single(candidate => candidate.Name == name);

    /// <summary>El boton de crear paquete. Solo desde el hilo de UI.</summary>
    private Button PublishButton() =>
        _window!.GetLogicalDescendants().OfType<Button>()
            .Single(candidate => candidate.Name == "PublishButton");

    /// <summary>
    /// Texto del mensaje de estado. Solo desde el hilo de UI.
    ///
    /// El mensaje de estado es el <c>TextBlock</c> cuyo texto coincide con
    /// <see cref="ViewModels.PackageCreateViewModel.StatusMessage"/>. Se localiza por
    /// su contenido enlazado y no por la posicion en el arbol ni por clases de
    /// estilo, que en XAML no existen: los textos de la cabecera del formulario
    /// ("Crear paquete", ...) tambien son <c>TextBlock</c> sin nombre.
    /// </summary>
    private string StatusMessageOnUiThread()
    {
        var status = _view!.ViewModel.StatusMessage;
        if (string.IsNullOrEmpty(status))
        {
            return string.Empty;
        }

        var shown = _window!.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Where(block => string.IsNullOrEmpty(block.Name))
            .Select(block => block.Text)
            .FirstOrDefault(text => text == status);

        return shown ?? string.Empty;
    }

    /// <summary>Texto de uno de los campos del formulario, por su nombre estable.</summary>
    private string TextBoxText(string name)
    {
        var text = string.Empty;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => text = TextBox(name).Text);
        return text;
    }

    /// <summary>Si el boton de enviar esta habilitado.</summary>
    private bool PublishButtonEnabled()
    {
        var enabled = false;
        HeadlessAvalonia.RunOnUiThreadAndWait(() => enabled = PublishButton().IsEnabled);
        return enabled;
    }

    /// <summary>
    /// Monta la vista real del formulario en una ventana. La vista se construye en
    /// el hilo de UI: es su dueno y no se puede adjuntar a una ventana desde otro.
    /// </summary>
    private PackageCreateView ShowView(IPackageCreationService service)
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

            // Deja que termine el trabajo pendiente del hilo de UI.
            HeadlessAvalonia.DrainUiQueue();
            window.UpdateLayout();
        });

        _window = window;
        _view = view;
        return view!;
    }

    /// <summary>
    /// Direccion base del servidor real de la prueba, ya con el puerto que el
    /// sistema eligio al arrancar.
    /// </summary>
    private string ServerAddress
    {
        get
        {
            var addresses = _app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>();
            var address = addresses?.Addresses.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "El servidor de la prueba no tiene direccion de escucha.");
            return address;
        }
    }

    /// <summary>
    /// El catalogo que devuelve el servidor real.
    ///
    /// La peticion sale de un hilo del pool: el hilo del test tiene el
    /// <c>SynchronizationContext</c> de Avalonia y ahi las continuaciones no se
    /// ejecutan, asi que una peticion lanzada desde el no llegaria a completarse.
    /// </summary>
    private Task<IReadOnlyList<CatalogEntry>> CatalogAsync() =>
        Task.Run<IReadOnlyList<CatalogEntry>>(async () =>
        {
            using var client = new HttpClient { BaseAddress = new Uri(ServerAddress) };

            // La conexion se cierra al terminar: si se queda abierta, el servidor
            // sigue con ella viva y al parar el host espera a que expire.
            client.DefaultRequestHeaders.ConnectionClose = true;

            var response = await client.GetAsync("/api/v1/catalog").ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var entries = await response.Content
                .ReadFromJsonAsync<List<CatalogEntry>>(SharedJson.Options)
                .ConfigureAwait(false);
            return entries ?? [];
        });

    /// <summary>
    /// El manifiesto firmado de un paquete, tal y como lo sirve el servidor.
    ///
    /// Devuelve el cuerpo ya leido: la respuesta se cierra dentro, asi que el
    /// texto tiene que salir de aqui.
    /// </summary>
    private Task<string> FetchManifest(string gameId) =>
        Task.Run(async () =>
        {
            using var client = new HttpClient { BaseAddress = new Uri(ServerAddress) };
            client.DefaultRequestHeaders.ConnectionClose = true;

            using var response = await client
                .GetAsync($"/api/v1/manifest/{gameId}")
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        });
}
