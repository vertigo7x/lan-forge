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
/// mejoras del servidor: el catálogo no incluye el formulario de publicación y la
/// confirmacion de borrado sobrevive al refresco periodico del listado.
///
/// Comparte la coleccion "Cliente-UI" con las pruebas de la UI del cliente: las
/// ventanas de Avalonia solo se pueden construir en el hilo de UI, y ese hilo es
/// compartido por toda la aplicacion.
/// </summary>
[Collection("Cliente-UI")]
public sealed class ServerUiTests
{
    public ServerUiTests(HeadlessServerFixture fixture)
    {
        _ = fixture;
    }

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
    public void ElCatalogoNoIncluyeElFormularioDePublicacion()
    {
        HeadlessAvalonia.RunOnUiThread(() =>
        {
            var window = CreateWindow();
            try
            {
                Assert.Null(window.FindControl<TextBox>("SourceDirBox"));
                Assert.Null(window.FindControl<Button>("BrowseButton"));
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

}
