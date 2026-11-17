using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using LanForge.Client;
using LanForge.Client.Controls;
using LanForge.Client.ViewModels;

namespace LanForge.UnitTests.Client;

/// <summary>
/// Tests de la UI del cliente en headless. Comprueban los arreglos de la Fase 7:
/// la portada se pinta (7.1), el area de chat es responsive y el boton de enviar
/// queda dentro (7.3), el nombre de usuario se puede escribir (7.4) y los botones
/// de ajustes tienen padding (7.5).
/// </summary>
[Collection("Cliente-UI")]
public sealed class ClientUiTests
{
    private static MainWindow CreateWindow()
    {
        var window = new MainWindow();
        window.Show();
        window.UpdateLayout();
        // La carga de ajustes es asincrona y rellena el campo de nombre al abrir.
        HeadlessClientFixture.DrainUiQueue();
        window.UpdateLayout();
        return window;
    }

    // ---------- 7.1: las portadas se pintan ----------

    [Fact]
    public void La_portada_de_la_game_card_se_convierte_en_una_imagen()
    {
        var card = new GameCard();
        var window = new Window { Width = 400, Height = 300, Content = card };
        window.Show();

        card.DataContext = new GameCardItem(
            new Shared.Models.CatalogEntry("neon-drift", "Neon Drift", "neon-drift", "1.0.0", 1024, 12),
            installedVersion: null,
            install: _ => Task.CompletedTask,
            play: _ => Task.CompletedTask)
        {
            CoverAsset = "avares://LanForge.Client/Assets/cover-neon-drift.png",
        };
        window.UpdateLayout();

        var image = card.GetVisualDescendants().OfType<Image>().FirstOrDefault();
        Assert.NotNull(image);
        // Image.Source es IImage: si se enlaza un string, la imagen no se pinta.
        Assert.IsAssignableFrom<IImage>(image!.Source);
    }

    [Fact]
    public void El_conversor_de_portadas_devuelve_null_para_un_valor_invalido()
    {
        var converter = new CoverImageConverter();

        Assert.Null(converter.Convert(null, typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert(string.Empty, typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Null(converter.Convert(42, typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void El_conversor_de_portadas_carga_un_asset_del_cliente()
    {
        var converter = new CoverImageConverter();

        var image = converter.Convert(
            "avares://LanForge.Client/Assets/cover-neon-drift.png",
            typeof(IImage), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.IsAssignableFrom<IImage>(image);
    }

    // ---------- 7.3: el area de chat es responsive y el boton de enviar queda dentro ----------

    [Fact]
    public void El_boton_de_enviar_queda_dentro_del_area_de_chat()
    {
        var window = CreateWindow();
        try
        {
            window.FindControl<ScrollViewer>("ChatView")!.IsVisible = true;
            window.FindControl<ScrollViewer>("CatalogView")!.IsVisible = false;
            window.UpdateLayout();

            var input = window.FindControl<TextBox>("ChatInput")!;
            var send = window.FindControl<Button>("SendButton")!;
            var card = input.GetVisualAncestors().OfType<Border>().First(b => b.Padding.Left > 0);

            var sendRight = send.Bounds.X + send.Bounds.Width;
            Assert.True(
                sendRight <= card.Bounds.Width,
                $"El boton de enviar se sale del area de chat (derecha {sendRight} > {card.Bounds.Width}).");

            // El campo de entrada comparte la fila (columna "*") con el boton.
            Assert.Same(input.GetVisualParent(), send.GetVisualParent());
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void El_area_de_chat_crece_con_el_ancho_de_la_ventana()
    {
        // En headless la ventana ya mostrada no se vuelve a medir al cambiar Width,
        // asi que se mide con una ventana nueva por cada ancho.
        var narrow = MeasureChatCardWidth(1280);
        var wide = MeasureChatCardWidth(1800);

        Assert.True(
            wide > narrow,
            $"El area de chat no se adapta al ancho de la ventana ({wide} <= {narrow}).");
    }

    private static double MeasureChatCardWidth(double width)
    {
        var window = new MainWindow();
        try
        {
            window.FindControl<ScrollViewer>("ChatView")!.IsVisible = true;
            window.FindControl<ScrollViewer>("CatalogView")!.IsVisible = false;
            window.Width = width;
            window.Height = 800;
            window.Show();
            window.UpdateLayout();

            var input = window.FindControl<TextBox>("ChatInput")!;
            var card = input.GetVisualAncestors().OfType<Border>().First(b => b.Padding.Left > 0);
            return card.Bounds.Width;
        }
        finally
        {
            window.Close();
        }
    }

    // ---------- 7.4: el nombre de usuario se puede escribir ----------

    [Fact]
    public void El_nombre_de_usuario_se_puede_escribir_y_tiene_placeholder()
    {
        var window = CreateWindow();
        try
        {
            // El campo vive en la vista de ajustes, que arranca oculta: sin mostrarla
            // el control no puede recibir foco ni tecleado.
            window.FindControl<ScrollViewer>("SettingsView")!.IsVisible = true;
            window.FindControl<ScrollViewer>("CatalogView")!.IsVisible = false;
            window.UpdateLayout();
            HeadlessClientFixture.DrainUiQueue();

            var nameBox = window.FindControl<TextBox>("PlayerNameBox")!;

            Assert.Equal("Escribe tu nombre de jugador", nameBox.PlaceholderText);
            Assert.False(nameBox.IsReadOnly);

            // Al abrir, el campo viene relleno con el nombre de la maquina: se vacia
            // para comprobar que el texto tecleado llega al control.
            nameBox.Text = string.Empty;
            window.Activate();
            nameBox.Focus();
            HeadlessClientFixture.DrainUiQueue();
            window.KeyTextInput("Ana");
            window.UpdateLayout();

            Assert.Equal("Ana", nameBox.Text);
        }
        finally
        {
            window.Close();
        }
    }

    // ---------- 7.5: los botones de ajustes tienen padding ----------

    [Theory]
    [InlineData("SaveSettingsButton")]
    [InlineData("ResetSettingsButton")]
    public void Los_botones_de_ajustes_tienen_padding_horizontal(string name)
    {
        var window = CreateWindow();
        try
        {
            var button = window.FindControl<Button>(name)!;

            Assert.True(button.Padding.Left >= 12, $"El boton {name} no tiene padding horizontal ({button.Padding}).");
            Assert.True(button.Padding.Right >= 12, $"El boton {name} no tiene padding horizontal ({button.Padding}).");
        }
        finally
        {
            window.Close();
        }
    }

    // ---------- 7.7: ordenar por nombre es un boton funcional ----------

    [Fact]
    public void El_boton_de_ordenar_alterna_el_sentido_del_orden()
    {
        var window = CreateWindow();
        try
        {
            var button = window.FindControl<Button>("SortButton")!;
            var text = window.FindControl<TextBlock>("SortText")!;

            Assert.Contains("A-Z", text.Text, StringComparison.Ordinal);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("Z-A", text.Text, StringComparison.Ordinal);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("A-Z", text.Text, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    // ---------- 7.6: el boton de refrescar existe y esta habilitado ----------

    [Fact]
    public void El_boton_de_actualizar_catalogo_existe()
    {
        var window = CreateWindow();
        try
        {
            var refresh = window.FindControl<Button>("RefreshCatalogButton")!;

            Assert.NotNull(refresh);
            Assert.True(refresh.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }
}
