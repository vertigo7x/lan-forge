using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.ViewModels;
using LanForge.Shared.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.Server;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;

    /// <summary>
    /// Filas del catalogo que ya existen, por id de juego. Se reutilizan en cada refresco
    /// para que la confirmacion de borrado sobreviva al refresco periodico.
    /// </summary>
    private readonly CatalogRowCache _catalogRows = new();

    public MainWindow()
    {
        InitializeComponent();

        var services = App.WebServices ?? throw new InvalidOperationException("El host web no está disponible.");
        var identity = services.GetRequiredService<IServerIdentity>();
        var options = Server.Http.ServerOptions.Default();

        ServerNameText.Text = identity.ServerName;
        AddressText.Text = $"{GetLocalIpAddress()}:{options.HttpPort}";
        FingerprintText.Text = identity.Fingerprint.ToString();
        ChooseLanPartyLogoButton.Click += async (_, _) => await ChooseLanPartyLogoAsync();
        ClearLanPartyLogoButton.Click += async (_, _) => await ClearLanPartyLogoAsync();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        Loaded += async (_, _) =>
        {
            await RefreshAsync();
            await RefreshLanPartyLogoPreviewAsync();
        };
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var address = Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return address?.ToString() ?? "127.0.0.1";
        }
        catch (SocketException)
        {
            return "127.0.0.1";
        }
    }

    private async Task ChooseLanPartyLogoAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Elige el logotipo de la LAN party",
            FileTypeFilter =
            [
                new FilePickerFileType("Imagen") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] },
            ],
        });
        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        try
        {
            var branding = App.WebServices!.GetRequiredService<ILanPartyBrandingStore>();
            await branding.SaveLogoAsync(file.Path.LocalPath);
            LanPartyLogoStatusText.Text = "Logotipo guardado. Se mostrará al sincronizar los clientes.";
            await RefreshLanPartyLogoPreviewAsync();
        }
        catch (Exception ex)
        {
            LanPartyLogoStatusText.Text = $"No se pudo guardar el logotipo: {ex.Message}";
        }
    }

    private async Task ClearLanPartyLogoAsync()
    {
        var branding = App.WebServices!.GetRequiredService<ILanPartyBrandingStore>();
        await branding.ClearLogoAsync();
        LanPartyLogoPreview.Source = null;
        LanPartyLogoStatusText.Text = "Logotipo eliminado.";
    }

    private async Task RefreshLanPartyLogoPreviewAsync()
    {
        var branding = App.WebServices!.GetRequiredService<ILanPartyBrandingStore>();
        if (string.IsNullOrWhiteSpace(branding.LogoFileName))
        {
            LanPartyLogoPreview.Source = null;
            return;
        }

        await using var stream = await branding.OpenLogoAsync(branding.LogoFileName);
        LanPartyLogoPreview.Source = stream is null ? null : new Bitmap(stream);
    }

    /// <summary>Borra un paquete del catalogo. La primera pulsacion pide confirmacion.</summary>
    private async void OnDeleteGame(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CatalogRow row })
        {
            return;
        }

        if (row.ConfirmText != "Borrar")
        {
            // Segunda pulsacion: el jugador ya ha visto el aviso, se borra de verdad.
            row.ConfirmText = "Borrar";
            await DeleteGameAsync(row.GameId);
            return;
        }

        row.ConfirmText = "¿Seguro?";
        StatusText.Text = $"Vuelve a pulsar para borrar '{row.Title}' ({row.GameId}) del catalogo.";
    }

    private async Task DeleteGameAsync(string gameId)
    {
        var services = App.WebServices!;
        var catalog = services.GetRequiredService<CatalogService>();
        try
        {
            var deleted = await catalog.DeleteGameAsync(gameId, services.GetRequiredService<ICoverStore>());
            if (deleted)
            {
                // La fila se descarta ya: si el juego volviera a publicarse, debe
                // aparecer con su confirmacion en estado normal.
                _catalogRows.Remove(gameId);
            }

            StatusText.Text = deleted
                ? $"Juego eliminado del catalogo: {gameId}."
                : $"No habia ningun juego con el id '{gameId}'.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error al borrar '{gameId}': {ex.Message}";
        }
    }

    /// <summary>
    /// Reconstruye el listado del catalogo reutilizando las filas que ya existen. Antes se
    /// creaban filas nuevas en cada refresco (cada 2 segundos), y como el boton de borrar
    /// guarda su confirmacion en la propia fila, el organizador tenia que pulsar dos veces
    /// en menos de 2 segundos o el borrado nunca se ejecutaba. Al reutilizar la fila, la
    /// confirmacion sobrevive al refresco.
    /// </summary>
    private CatalogRow[] BuildCatalogRows(IReadOnlyList<Package> packages, IReadOnlyList<Game> games) =>
        _catalogRows.Build(packages, games);

    private async Task RefreshAsync()
    {
        var services = App.WebServices;
        if (services is null)
        {
            return;
        }

        var store = services.GetRequiredService<ICatalogStore>();
        var packages = await store.ListLatestPackagesAsync();
        var games = await store.ListGamesAsync();
        CatalogList.ItemsSource = BuildCatalogRows(packages, games);

        var clients = await store.ListSeenClientsAsync();
        ClientsList.ItemsSource = clients
            .Select(c => $"{c.ClientId}  —  visto {c.LastSeenUtc.LocalDateTime:g}  —  {c.IpAddress ?? "?"}")
            .ToArray();

        var rooms = services.GetRequiredService<LanForge.Server.Http.ChatRoomRegistry>();
        ChannelsList.ItemsSource = rooms
            .List()
            .Select(c => $"{c.Name}  —  {c.Members} miembro(s)")
            .ToArray();

        // La lectura viene acotada por el store (las mas recientes primero) y el listado
        // de la UI no necesita mas de 100: antes se cargaba la tabla entera en memoria
        // cada 2 segundos solo para quedarse con las 100 ultimas.
        var operations = await store.ListOperationsAsync(100);
        OperationsList.ItemsSource = operations
            .Select(o => $"{o.StartedUtc.LocalDateTime:g}  [{o.Status}]  {o.Type}  {o.Detail ?? string.Empty}  ({o.ClientId})")
            .ToArray();
    }
}
