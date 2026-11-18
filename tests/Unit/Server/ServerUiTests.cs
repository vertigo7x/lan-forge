using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using LanForge.Server;
using LanForge.Server.ViewModels;
using LanForge.UnitTests.Client;
using ServerDomain = LanForge.Server.Domain;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Tests de la UI del organizador en headless. Comprueban la parte visible de las
/// mejoras del servidor: durante la publicacion el boton queda deshabilitado (para que
/// el organizador no lance dos publicaciones a la vez y vea el estado) y la
/// confirmacion de borrado sobrevive al refresco periodico del listado.
///
/// Comparte la coleccion "Cliente-UI" con las pruebas de la UI del cliente: las
/// ventanas de Avalonia solo se pueden construir en el hilo de UI, y ese hilo es
/// compartido por toda la aplicacion.
/// </summary>
[Collection("Cliente-UI")]
public sealed class ServerUiTests(HeadlessServerFixture fixture)
{
    private static MainWindow CreateWindow()
    {
        var window = new MainWindow();
        window.Show();
        window.UpdateLayout();
        HeadlessClientFixture.DrainUiQueue();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    public void LosBotonesDePublicacionEstanHabilitadosAlAbrir()
    {
        HeadlessAvalonia.RunOnUiThread(() =>
        {
            var window = CreateWindow();
            try
            {
                Assert.True(window.FindControl<Button>("PublishButton")!.IsEnabled);
                Assert.True(window.FindControl<Button>("BrowseButton")!.IsEnabled);
                Assert.True(window.FindControl<Button>("BrowseCoverButton")!.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task DuranteLaPublicacionElBotonQuedaDeshabilitado()
    {
        // Publicar comprime y hashea un juego: son segundos o minutos. Sin deshabilitar
        // el boton, el organizador creeria que no pasa nada y podria publicar dos veces.
        var source = Path.Combine(fixture.TempPath, "juego");
        Directory.CreateDirectory(source);

        // Un archivo grande: con uno pequeno la publicacion termina antes de que el test
        // pueda mirar, y el boton ya habria vuelto a habilitarse.
        var payload = new byte[8 * 1024 * 1024];
        Random.Shared.NextBytes(payload);
        await File.WriteAllBytesAsync(Path.Combine(source, "run.exe"), payload);

        await HeadlessAvalonia.RunOnUiThreadAsync(async () =>
        {
            var window = CreateWindow();
            try
            {
                window.FindControl<TextBox>("GameIdBox")!.Text = "quake3";
                window.FindControl<TextBox>("TitleBox")!.Text = "Quake III Arena";
                window.FindControl<TextBox>("VersionBox")!.Text = "1.32";
                window.FindControl<TextBox>("SourceDirBox")!.Text = source;
                window.FindControl<TextBox>("EntryPointBox")!.Text = "run.exe";

                var publish = window.FindControl<Button>("PublishButton")!;
                publish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                // La publicacion arranca en segundo plano: en cuanto se ha lanzado, el boton
                // tiene que estar deshabilitado mientras dura.
                HeadlessClientFixture.DrainUiQueue();
                Assert.False(publish.IsEnabled);
                Assert.False(window.FindControl<Button>("BrowseButton")!.IsEnabled);
                Assert.False(window.FindControl<Button>("BrowseCoverButton")!.IsEnabled);

                // Y el estado explica que esta en curso.
                var status = window.FindControl<TextBlock>("StatusText")!;
                Assert.Contains("Publicando", status.Text!);

                // Al terminar, el boton vuelve a estar disponible.
                await WaitUntilAsync(() => publish.IsEnabled);
                Assert.True(publish.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void LaConfirmacionDeBorradoSobreviveAlRefrescoDelListado()
    {
        // El listado se refresca cada 2 segundos. Si en cada refresco se crearan filas
        // nuevas, el boton de borrar perderia su confirmacion y el organizador tendria
        // que pulsar dos veces en menos de 2 segundos.
        HeadlessAvalonia.RunOnUiThread(() =>
        {
            var window = CreateWindow();
            try
            {
                var row = new CatalogRow("quake3", "Quake III Arena", "1.32", 1, 10);
                window.FindControl<ListBox>("CatalogList")!.ItemsSource = new[] { row };

                var delete = new Button { DataContext = row };
                delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal("¿Seguro?", row.ConfirmText);

                // Llega el refresco: la fila se reutiliza y mantiene su confirmacion.
                var cache = new CatalogRowCache();
                var reused = cache.Build(
                    [new ServerDomain.Package
                    {
                        PackageId = "quake3",
                        GameId = "quake3",
                        Version = "1.32",
                        EntryPoint = "run.exe",
                        Arguments = null,
                        TotalSizeBytes = 10,
                        FileCount = 1,
                        CreatedUtc = DateTimeOffset.UtcNow,
                        Manifest = null!,
                    }],
                    [new ServerDomain.Game("quake3", "Quake III Arena")]);

                Assert.Same(row, reused[0]);
                Assert.Equal("¿Seguro?", reused[0].ConfirmText);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>Espera a que la condicion se cumpla (el trabajo de UI es asincrono).</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            HeadlessClientFixture.DrainUiQueue();
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }
    }
}
