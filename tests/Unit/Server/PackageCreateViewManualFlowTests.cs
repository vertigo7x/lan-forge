using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.Views;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Verificacion del flujo completo del formulario de publicacion.
///
/// A diferencia del resto de pruebas, esta no sustituye el selector de rutas:
/// usa el selector real de la vista, que abre los dialogos del sistema
/// operativo a traves del proveedor de almacenamiento de Avalonia. En headless
/// los dialogos no se pueden abrir, asi que lo que se comprueba aqui es que la
/// vista esta preparada para usarlos y que el formulario acepta las rutas que
/// el dialogo devolveria.
///
/// El envio si es de verdad: la peticion sale por HTTP contra el endpoint real
/// del servidor y el paquete se crea en el contenido del servidor.
///
/// En headless el hilo de UI no reanuda las continuaciones pendiente, asi que
/// la llamada HTTP se hace en un hilo de fondo y se entrega ya resuelta al
/// hilo de UI: el <c>await</c> del view model continua de forma sincrona y el
/// formulario refleja el resultado sin colgarse.
/// </summary>
[Collection("Cliente-UI")]
public sealed class PackageCreateViewManualFlowTests(HeadlessServerFixture fixture) : IDisposable
{
    private readonly TemporaryGameFolder _game = new();

    public void Dispose() => _game.Dispose();

    [Fact]
    public void La_vista_real_tiene_los_dos_dialogos_del_sistema_operativo()
    {
        var (_, window) = ShowView(fixture);

        var buttons = Descendants<Button>(window);
        Assert.Contains(buttons, button => button.Name == "PickSourceDirectoryButton");
        Assert.Contains(buttons, button => button.Name == "PickEntryPointButton");

        // Los dos botones ejecutan los comandos que abren los dialogos.
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var pickFolder = buttons.Single(b => b.Name == "PickSourceDirectoryButton");
            var pickExecutable = buttons.Single(b => b.Name == "PickEntryPointButton");
            Assert.NotNull(pickFolder.Command);
            Assert.NotNull(pickExecutable.Command);
        });

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void Publicar_con_las_rutas_elegidas_crea_el_paquete_en_el_servidor()
    {
        // El flujo real: el organizador elige la carpeta y el ejecutable y
        // publica. El endpoint real recibe la peticion y crea el paquete.
        var (view, window) = ShowView(fixture);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift-manual",
            "Neon Drift Manual",
            "1.0.0",
            _game.SourceDirectory,
            _game.ExecutablePath,
            "--fullscreen"));

        // El boton de publicar solo se habilita con las rutas elegidas.
        var enabled = false;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var button = window.GetLogicalDescendants().OfType<Button>()
                .Single(candidate => candidate.Name == "PublishButton");
            enabled = button.IsEnabled;
        });
        Assert.True(enabled);

        Submit(view);

        Assert.False(view.ViewModel.StatusIsError);
        Assert.Contains("Neon Drift Manual", view.ViewModel.StatusMessage!, StringComparison.Ordinal);

        // El paquete se creo de verdad en el servidor: el store lo tiene.
        var store = fixture.Services.GetRequiredService<ICatalogStore>();
        var package = ReadPackage(store, "neon-drift-manual");
        Assert.NotNull(package);
        Assert.Equal("1.0.0", package!.Version);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    [Fact]
    public void El_manifiesto_guarda_el_ejecutable_como_ruta_relativa()
    {
        // El endpoint exige que el punto de entrada sea una ruta relativa dentro
        // de la carpeta: es lo que el servidor firma y lo que el cliente
        // resuelve contra el directorio de instalacion del juego.
        var (view, window) = ShowView(fixture);

        HeadlessAvalonia.RunOnUiThreadAndWait(() => view.SetFormValues(
            "neon-drift-relativo",
            "Neon Drift Relativo",
            "1.0.0",
            _game.SourceDirectory,
            _game.ExecutablePath,
            string.Empty));

        Submit(view);

        Assert.False(view.ViewModel.StatusIsError);

        var store = fixture.Services.GetRequiredService<ICatalogStore>();
        var package = ReadPackage(store, "neon-drift-relativo");
        Assert.NotNull(package);
        Assert.Equal("game.exe", package!.Manifest.EntryPoint);

        HeadlessAvalonia.RunOnUiThreadAndWait(window.Close);
    }

    /// <summary>
    /// Envia el formulario y espera a que termine.
    ///
    /// El servicio real habla por HTTP, asi que la peticion sale del hilo de UI
    /// en un hilo de fondo y vuelve ya resuelta: el <c>await</c> del view model
    /// continua en el hilo de UI y el boton de publicar se rehabilita.
    /// </summary>
    private static void Submit(PackageCreateView view)
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view.SubmitAsync().GetAwaiter().GetResult();
            HeadlessAvalonia.DrainUiQueue();
        });
    }

    private static (PackageCreateView View, Window Window) ShowView(HeadlessServerFixture fixture)
    {
        PackageCreateView? view = null;
        Window? window = null;
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            view = new PackageCreateView(new BlockingHttpPackageCreationService(fixture));
            window = new Window
            {
                Width = 640,
                Height = 700,
                Content = view,
            };
            window.Show();
            window.UpdateLayout();
            HeadlessAvalonia.DrainUiQueue();
            window.UpdateLayout();
        });
        return (view!, window!);
    }

    /// <summary>
    /// Cliente HTTP real del endpoint con la llamada resuelta de forma sincrona.
    ///
    /// El hilo de UI de Avalonia en headless no reanuda las continuaciones
    /// pendiente, asi que una peticion HTTP awaited desde ese hilo se queda
    /// colgada. El manejador hace la peticion de verdad en un hilo de fondo y
    /// devuelve la respuesta ya resuelta: la peticion sale por la red contra el
    /// endpoint real y el <c>await</c> del view model continua en el hilo de UI.
    /// </summary>
    private sealed class BlockingHttpPackageCreationService : IPackageCreationService
    {
        private readonly HttpPackageCreationService _inner;

        public BlockingHttpPackageCreationService(HeadlessServerFixture fixture)
        {
            _inner = new HttpPackageCreationService(
                () => fixture.Address,
                new BlockingHandler());
        }

        public Task<PackageCreationResult> CreateAsync(
            PackageCreationRequest request,
            CancellationToken cancellationToken = default) =>
            _inner.CreateAsync(request, cancellationToken);

        private sealed class BlockingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                // La peticion real sale en un hilo de fondo y se espera aqui: el
                // hilo de UI de Avalonia en headless no reanuda las continuaciones
                // pendiente, asi que la respuesta tiene que estar lista antes de
                // devolver.
                var response = Task.Run(
                    async () =>
                    {
                        // La peticion se copia: el mensaje original lo envia el
                        // HttpClient de este manejador y no se puede reutilizar.
                        using var forwarded = await CloneRequestAsync(request, cancellationToken);
                        using var result = await new HttpClient()
                            .SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                        // El cuerpo se copia a memoria antes de devolver la
                        // respuesta: el cliente la vuelve a leer y la peticion
                        // original ya no es valida.
                        var body = await result.Content.ReadAsByteArrayAsync(cancellationToken);
                        var buffered = new HttpResponseMessage(result.StatusCode)
                        {
                            Content = new ByteArrayContent(body),
                        };
                        foreach (var header in result.Headers)
                        {
                            buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                        return buffered;
                    },
                    cancellationToken).GetAwaiter().GetResult();

                return Task.FromResult(response);
            }

            /// <summary>
            /// Copia la peticion: un <see cref="HttpRequestMessage"/> no se puede
            /// enviar dos veces, y aqui lo envian el cliente del view model y el
            /// manejador.
            /// </summary>
            private static async Task<HttpRequestMessage> CloneRequestAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                ByteArrayContent? content = null;
                if (request.Content is not null)
                {
                    content = new ByteArrayContent(
                        await request.Content.ReadAsByteArrayAsync(cancellationToken));

                    // Las cabeceras del contenido (por ejemplo Content-Type)
                    // viven en el contenido, no en la peticion: sin ellas el
                    // servidor no recibe JSON.
                    foreach (var header in request.Content.Headers)
                    {
                        content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                var clone = new HttpRequestMessage(request.Method, request.RequestUri)
                {
                    Content = content,
                    Version = request.Version,
                };

                foreach (var header in request.Headers)
                {
                    clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                return clone;
            }
        }
    }

    /// <summary>
    /// Lee un paquete del store del servidor.
    ///
    /// La lectura sale en un hilo de fondo y se espera aqui: en headless el
    /// hilo de la prueba tiene el <c>AvaloniaSynchronizationContext</c>, asi que
    /// un <c>await</c> en ese hilo no llega a completarse nunca.
    /// </summary>
    private static Package? ReadPackage(ICatalogStore store, string packageId) =>
        Task.Run(() => store.GetPackageAsync(packageId))
            .GetAwaiter()
            .GetResult();

    private static T[] Descendants<T>(Window window) where T : Control
    {
        T[] controls = [];
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
            controls = window.GetLogicalDescendants().OfType<T>().ToArray());
        return controls;
    }
}
