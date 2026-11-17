using System.Net;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LanForge.Server.Application;
using LanForge.Server.ViewModels;
using LanForge.Shared.Models;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.Server;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();

        var services = App.WebServices ?? throw new InvalidOperationException("El host web no está disponible.");
        var identity = services.GetRequiredService<IServerIdentity>();
        var options = Server.Http.ServerOptions.Default();

        ServerNameText.Text = identity.ServerName;
        AddressText.Text = $"{GetLocalIpAddress()}:{options.HttpPort}";
        FingerprintText.Text = identity.Fingerprint.ToString();

        BrowseButton.Click += async (_, _) => await BrowseSourceAsync();
        BrowseCoverButton.Click += async (_, _) => await BrowseCoverAsync();
        PublishButton.Click += async (_, _) => await PublishAsync();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();

        Loaded += async (_, _) => await RefreshAsync();
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

    private async Task BrowseSourceAsync()
    {
        var folders = await this.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        var folder = folders.FirstOrDefault();
        if (folder is not null)
        {
            SourceDirBox.Text = folder.Path.LocalPath;
        }
    }

    private async Task BrowseCoverAsync()
    {
        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Imagen") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp"] },
            ],
        });
        var file = files.FirstOrDefault();
        if (file is not null)
        {
            CoverBox.Text = file.Path.LocalPath;
        }
    }

    private async Task PublishAsync()
    {
        var services = App.WebServices!;
        var catalog = services.GetRequiredService<CatalogService>();
        try
        {
            var entry = await catalog.PublishPackageAsync(
                GameIdBox.Text?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(TitleBox.Text) ? GameIdBox.Text?.Trim() ?? "Juego" : TitleBox.Text.Trim(),
                VersionBox.Text?.Trim() ?? string.Empty,
                SourceDirBox.Text ?? string.Empty,
                EntryPointBox.Text?.Trim() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(CoverBox.Text))
            {
                await catalog.SetCoverAsync(
                    entry.GameId,
                    CoverBox.Text,
                    services.GetRequiredService<ICoverStore>());
            }

            StatusText.Text = $"Publicado: {entry.Title} v{entry.Version} ({entry.FileCount} archivos, {entry.TotalSizeBytes} bytes).";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error al publicar: {ex.Message}";
        }
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
        CatalogList.ItemsSource = packages
            .Select(p =>
            {
                var title = games.FirstOrDefault(g => g.Id == p.GameId)?.Title ?? p.GameId;
                return new CatalogRow(p.GameId, title, p.Version, p.FileCount, p.TotalSizeBytes);
            })
            .ToArray();

        var clients = await store.ListSeenClientsAsync();
        ClientsList.ItemsSource = clients
            .Select(c => $"{c.ClientId}  —  visto {c.LastSeenUtc.LocalDateTime:g}  —  {c.IpAddress ?? "?"}")
            .ToArray();

        var rooms = services.GetRequiredService<LanForge.Server.Http.ChatRoomRegistry>();
        ChannelsList.ItemsSource = rooms
            .List()
            .Select(c => $"{c.Name}  —  {c.Members} miembro(s)")
            .ToArray();

        var operations = await store.ListOperationsAsync();
        OperationsList.ItemsSource = operations
            .TakeLast(100)
            .Select(o => $"{o.StartedUtc.LocalDateTime:g}  [{o.Status}]  {o.Type}  {o.Detail ?? string.Empty}  ({o.ClientId})")
            .ToArray();
    }
}
