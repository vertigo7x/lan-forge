using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LanForge.Client;

namespace LanForge.UnitTests.Client;

/// <summary>
/// Tests del comportamiento responsivo del formulario de ajustes.
///
/// Comprueban los puntos de ruptura del enunciado: a 375 px el formulario no
/// desborda y todo se apila; a 768 px se mantiene en una sola columna; a 1440 px
/// conserva las dos columnas del escritorio. Ademas comprueban que el ancho
/// util del formulario se mide sobre el area de contenido (sin el margen) y que
/// el formulario sigue funcionando (guardar y restablecer).
/// </summary>
[Collection("Cliente-UI")]
public sealed class SettingsResponsiveTests
{
    // Anchos a comprobar: movil, tablet y escritorio.
    private const double MobileWidth = 375;
    private const double TabletWidth = 768;
    private const double DesktopWidth = 1440;

    [Fact]
    public void El_formulario_de_ajustes_no_desborda_en_un_viewport_de_375_px()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var window = ShowSettings(MobileWidth);
            try
            {
                // La ventana tiene que poder llegar al ancho pedido: si su MinWidth
                // se queda en 1100 px, la ventana nunca baja de ahi y la comprobacion
                // de desbordamiento no diria nada (todo cabria en una ventana ancha).
                Assert.True(
                    window.Bounds.Width <= MobileWidth + 0.5,
                    $"La ventana no baja de {window.Bounds.Width:F1} px: " +
                    "su ancho minimo impide llegar al viewport de movil.");

                var report = MeasureOverflow(window);

                Assert.True(
                    report.Overflowing.Count == 0,
                    "El formulario de ajustes se sale del viewport de 375 px: " +
                    string.Join("; ", report.Overflowing));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void El_formulario_de_ajustes_no_desborda_en_768_ni_en_1440_px()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            foreach (var width in new[] { TabletWidth, DesktopWidth })
            {
                var window = ShowSettings(width);
                try
                {
                    var report = MeasureOverflow(window);

                    Assert.True(
                        report.Overflowing.Count == 0,
                        $"El formulario de ajustes se sale del viewport de {width} px: " +
                        string.Join("; ", report.Overflowing));
                }
                finally
                {
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public void En_movil_los_campos_y_las_tarjetas_se_apilan_en_una_sola_columna()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var window = ShowSettings(MobileWidth);
            try
            {
                var columns = window.FindControl<Grid>("SettingsColumns")!;
                var left = window.FindControl<StackPanel>("SettingsLeftColumn")!;
                var right = window.FindControl<StackPanel>("SettingsRightColumn")!;
                var footer = window.FindControl<Grid>("SettingsFooter")!;

                // Una sola columna: la derecha pasa debajo de la izquierda.
                Assert.Single(columns.ColumnDefinitions);
                Assert.Equal(0, Grid.GetColumn(right));
                Assert.Equal(1, Grid.GetRow(right));
                Assert.Equal(1, Grid.GetColumnSpan(footer));

                // Y no se solapan: la derecha empieza donde termina la izquierda.
                Assert.True(
                    right.Bounds.Y >= left.Bounds.Y + left.Bounds.Height - 0.5,
                    $"Las columnas se solapan: la derecha empieza en {right.Bounds.Y} y la " +
                    $"izquierda termina en {left.Bounds.Y + left.Bounds.Height}.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void En_movil_los_botones_de_conexion_se_envuelven_en_varias_lineas()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var window = ShowSettings(MobileWidth);
            try
            {
                var actionButtons = window.FindControl<WrapPanel>("SettingsActionButtons")!;
                var connect = window.FindControl<Button>("ConnectButton")!;
                var discover = window.FindControl<Button>("DiscoverButton")!;
                var test = window.FindControl<Button>("TestConnectionButton")!;

                // Los tres botones se apilan: no caben en una sola linea a 375 px.
                var rows = RowCount(actionButtons, connect, discover, test);
                Assert.True(rows > 1, $"Los botones de conexion siguen en una sola linea a {MobileWidth} px.");

                // Y ninguno se sale de la tarjeta.
                var card = connect.GetVisualAncestors().OfType<Border>()
                    .First(b => b.Bounds.Width > 0 && b.Padding.Left > 0);
                var innerRight = card.Bounds.Width - card.Padding.Right;
                foreach (var button in new[] { connect, discover, test })
                {
                    var origin = button.TranslatePoint(new Avalonia.Point(0, 0), card);
                    Assert.NotNull(origin);
                    Assert.True(
                        origin!.Value.X + button.Bounds.Width <= innerRight + 0.5,
                        $"El boton {button.Name} se sale de la tarjeta: " +
                        $"{origin.Value.X + button.Bounds.Width:F1} > {innerRight:F1}.");
                }

                Assert.True(actionButtons.Bounds.Width <= card.Bounds.Width);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void En_escritorio_el_formulario_conserva_las_dos_columnas_del_actual()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var window = ShowSettings(DesktopWidth);
            try
            {
                var columns = window.FindControl<Grid>("SettingsColumns")!;
                var right = window.FindControl<StackPanel>("SettingsRightColumn")!;
                var footer = window.FindControl<Grid>("SettingsFooter")!;

                Assert.Equal(2, columns.ColumnDefinitions.Count);
                Assert.Equal(1, Grid.GetColumn(right));
                Assert.Equal(0, Grid.GetRow(right));
                Assert.Equal(2, Grid.GetColumnSpan(footer));

                // La barra lateral sigue visible en escritorio.
                Assert.True(window.FindControl<Border>("SidebarBorder")!.IsVisible);

                // Los botones de conexion caben en una sola linea, como antes.
                var actionButtons = window.FindControl<WrapPanel>("SettingsActionButtons")!;
                var connect = window.FindControl<Button>("ConnectButton")!;
                var discover = window.FindControl<Button>("DiscoverButton")!;
                var test = window.FindControl<Button>("TestConnectionButton")!;

                var rows = RowCount(actionButtons, connect, discover, test);
                Assert.True(
                    rows == 1,
                    "Los botones de conexion se han partido en varias lineas en escritorio: " +
                    $"{rows} filas.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void En_escritorio_los_botones_de_conexion_no_se_salen_de_la_tarjeta()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var window = ShowSettings(DesktopWidth);
            try
            {
                var connect = window.FindControl<Button>("ConnectButton")!;
                var test = window.FindControl<Button>("TestConnectionButton")!;
                var card = connect.GetVisualAncestors().OfType<Border>()
                    .First(b => b.Bounds.Width > 0 && b.Padding.Left > 0);

                var innerRight = card.Bounds.Width - card.Padding.Right;
                var origin = test.TranslatePoint(new Avalonia.Point(0, 0), card);

                Assert.NotNull(origin);
                Assert.True(
                    origin!.Value.X + test.Bounds.Width <= innerRight + 0.5,
                    $"El boton de accion se sale de la tarjeta: " +
                    $"{origin.Value.X + test.Bounds.Width:F1} > {innerRight:F1}.");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void El_reparto_en_columnas_usa_el_ancho_util_sin_contar_el_margen()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            // Con el margen por defecto (28 px a cada lado) el ancho util es el de
            // la vista menos 56 px: si se midiera la vista entera, el reparto en
            // dos columnas se decidiria 56 px tarde.
            var window = ShowSettings(DesktopWidth);
            var narrow = ShowSettings(MobileWidth);
            try
            {
                var settings = window.FindControl<ScrollViewer>("SettingsView")!;
                var root = window.FindControl<StackPanel>("SettingsRoot")!;

                var viewWidth = settings.Bounds.Width;
                var margin = root.Margin.Left + root.Margin.Right;
                var usable = viewWidth - margin;

                Assert.True(usable > 0, $"El ancho util de ajustes es {usable:F1} px.");

                // Con el ancho util sale a dos columnas (hay sitio de sobra).
                Assert.True(usable >= 520 + 16 + 420);

                // El margen se estrecha en ventanas estrechas para no gastar el ancho
                // util en blanco.
                var narrowRoot = narrow.FindControl<StackPanel>("SettingsRoot")!;
                Assert.True(
                    narrowRoot.Margin.Left < root.Margin.Left,
                    "El margen de los ajustes no se ha reducido en una ventana estrecha.");
            }
            finally
            {
                window.Close();
                narrow.Close();
            }
        });
    }

    [Fact]
    public void La_barra_lateral_se_reduce_a_un_riel_de_iconos_en_ventanas_estrechas()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            // En una ventana de 375 px la barra lateral de 232 px se comia mas de
            // la mitad del ancho: se reduce a un riel de iconos para que el
            // formulario quepa y la navegacion siga siendo accesible.
            var window = ShowSettings(MobileWidth);
            try
            {
                var sidebar = window.FindControl<Border>("SidebarBorder")!;
                Assert.True(sidebar.IsVisible, "La barra lateral no deberia desaparecer: es la unica forma de cambiar de pantalla.");

                // Los rotulos se quitan, pero los botones siguen.
                Assert.False(window.FindControl<TextBlock>("SidebarMenuTitle")!.IsVisible);
                Assert.False(window.FindControl<StackPanel>("SidebarBottom")!.IsVisible);

                var navCatalog = window.FindControl<Button>("NavCatalog")!;
                var navSettings = window.FindControl<Button>("NavSettings")!;
                Assert.True(navCatalog.IsVisible);
                Assert.True(navSettings.IsVisible);

                // El riel es estrecho: los botones no ocupan los 232 px de antes.
                var root = window.FindControl<Grid>("RootGrid")!;
                Assert.True(
                    root.ColumnDefinitions[0].Width.Value <= 64,
                    $"La columna de la barra lateral sigue ocupando {root.ColumnDefinitions[0].Width.Value:F1} px.");

                // Y el boton de Ajustes se ve sin el texto: solo el icono.
                Assert.Equal(string.Empty, NavLabelOf(window, "NavSettingsText"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void En_escritorio_la_barra_lateral_sigue_con_su_texto()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var window = ShowSettings(DesktopWidth);
            try
            {
                var root = window.FindControl<Grid>("RootGrid")!;
                Assert.True(
                    Math.Abs(root.ColumnDefinitions[0].Width.Value - 232) < 0.5,
                    $"La barra lateral de escritorio mide {root.ColumnDefinitions[0].Width.Value:F1} px.");

                Assert.True(window.FindControl<TextBlock>("SidebarMenuTitle")!.IsVisible);
                Assert.True(window.FindControl<StackPanel>("SidebarBottom")!.IsVisible);

                var navText = NavLabelOf(window, "NavSettingsText");
                Assert.Equal("   Ajustes", navText);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Los_ajustes_se_pueden_guardar_y_restablecer_a_375_px()
    {
        HeadlessAvalonia.RunOnUiThreadAndWait(() =>
        {
            var window = ShowSettings(MobileWidth);
            try
            {
                var nameBox = window.FindControl<TextBox>("PlayerNameBox")!;
                var save = window.FindControl<Button>("SaveSettingsButton")!;
                var reset = window.FindControl<Button>("ResetSettingsButton")!;

                nameBox.Text = "Ana";
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();

                var hint = window.FindControl<TextBlock>("PlayerHintText")!;
                Assert.Equal("Cambios guardados.", hint.Text);

                reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();

                Assert.Equal("Valores por defecto restaurados.", hint.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ---------- ayudantes ----------

    /// <summary>
    /// Abre la ventana al ancho indicado, entra en Ajustes y la deja medida.
    /// </summary>
    private static MainWindow ShowSettings(double width)
    {
        var window = new MainWindow { Width = width, Height = 800 };
        window.Show();
        window.UpdateLayout();
        HeadlessClientFixture.DrainUiQueue();
        window.UpdateLayout();

        window.FindControl<Button>("NavSettings")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout();

        return window;
    }

    /// <summary>
    /// Devuelve el texto del rotulo de un boton de la barra lateral. El rotulo es
    /// un <see cref="Run"/> dentro del TextBlock del boton: no es un Control, asi
    /// que no se puede buscar con FindControl.
    /// </summary>
    private static string NavLabelOf(MainWindow window, string runName)
    {
        var label = window.FindControl<TextBlock>("NavSettingsLabel")!;
        var run = label.Inlines?.OfType<Run>().FirstOrDefault(r => r.Name == runName);
        Assert.NotNull(run);
        return run?.Text ?? string.Empty;
    }

    /// <summary>
    /// Numero de filas en las que se reparten unos controles dentro de un
    /// <see cref="WrapPanel"/>.
    ///
    /// No se comparan las Y de los botones: dentro de una fila el WrapPanel los
    /// centra en vertical y los botones tienen alturas distintas (44 el
    /// principal, 38 los secundarios), asi que sus Y no coinciden exactamente.
    /// Lo que dice si hay una o varias filas es la altura del panel: una fila
    /// ocupa lo que mide el boton mas alto, y varias filas lo suman.
    /// </summary>
    private static int RowCount(WrapPanel panel, params Control[] controls)
    {
        var tallest = controls.Max(c => c.Bounds.Height);
        if (tallest <= 0)
        {
            return 0;
        }

        return (int)Math.Round(panel.Bounds.Height / tallest);
    }

    /// <summary>
    /// Recorre el contenido del formulario y comprueba que nada se sale del
    /// area visible de la ventana.
    /// </summary>
    private static OverflowReport MeasureOverflow(MainWindow window)
    {
        var settings = window.FindControl<ScrollViewer>("SettingsView")!;
        var root = window.FindControl<StackPanel>("SettingsRoot")!;

        // El borde derecho visible de la vista de ajustes, en coordenadas de la
        // ventana: el visor ya empieza despues de la barra lateral, asi que su
        // posicion es la referencia y no hay que volver a restar la barra.
        var viewOrigin = settings.TranslatePoint(new Avalonia.Point(0, 0), window);
        Assert.NotNull(viewOrigin);
        var visibleRight = viewOrigin!.Value.X + settings.Bounds.Width - root.Margin.Right;

        var overflowing = new List<string>();

        // Se recorren los controles con nombre del formulario: los decorados
        // internos de los templates (PART_*, Root, VerticalRoot, TrackRect) son
        // de las barras de scroll y no dicen nada sobre el formulario.
        foreach (var visual in settings.GetVisualDescendants())
        {
            if (visual is not Control control) continue;
            if (control.Bounds.Width <= 0) continue;
            if (string.IsNullOrEmpty(control.Name)) continue;
            if (control.Name.StartsWith("PART_", StringComparison.Ordinal)) continue;
            if (IsInsideScrollBar(control)) continue;

            var origin = control.TranslatePoint(new Avalonia.Point(0, 0), window);
            if (!origin.HasValue) continue;

            var right = origin.Value.X + control.Bounds.Width;
            if (right > visibleRight + 0.5)
            {
                overflowing.Add(
                    $"{control.Name} (borde derecho {right:F1} > {visibleRight:F1})");
            }
        }

        return new OverflowReport(overflowing);
    }

    /// <summary>
    /// Indica si un control forma parte de una barra de scroll: la banda vertical
    /// que ocupa el borde derecho del visor y que no cuenta como desbordamiento
    /// del formulario.
    /// </summary>
    private static bool IsInsideScrollBar(Control control)
    {
        for (var parent = control.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is Avalonia.Controls.Primitives.ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record OverflowReport(List<string> Overflowing);
}
