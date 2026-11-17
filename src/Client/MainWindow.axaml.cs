using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using LanForge.Client.Application;
using LanForge.Client.Infrastructure;
using LanForge.Client.ViewModels;
using LanForge.Shared.Models;

namespace LanForge.Client;

public partial class MainWindow : Window
{
    private static readonly string[] CoverAssets =
    [
        "avares://LanForge.Client/Assets/cover-neon-drift.png",
        "avares://LanForge.Client/Assets/cover-starfall-arena.png",
        "avares://LanForge.Client/Assets/cover-dungeon-raid.png",
        "avares://LanForge.Client/Assets/cover-mecha-clash.png",
        "avares://LanForge.Client/Assets/cover-pixel-pirates.png",
        "avares://LanForge.Client/Assets/cover-zombie-protocol.png",
        "avares://LanForge.Client/Assets/cover-bumper-bots.png",
        "avares://LanForge.Client/Assets/cover-gravity-league.png",
    ];

    private const string SettingPlayerName = "player_name";
    private const string SettingConfirmUninstall = "confirm_uninstall";
    private const string SettingAutoConnect = "auto_connect";

    private readonly ClientOptions _options = ClientOptions.Default();
    private readonly ObservableCollection<GameCardItem> _cards = [];
    private ClientSession? _session;
    private IChatService? _chat;
    private string _currentChannel = LanForge.Shared.Chat.ChatHubContract.GeneralChannel;
    private readonly ObservableCollection<LanForge.Shared.Chat.ChatMessage> _chatMessages = [];
    private readonly ObservableCollection<ChatChannelItem> _chatChannels = [];
    private readonly ObservableCollection<string> _chatMembers = [];
    private List<CatalogEntry> _catalog = [];
    private List<Domain.InstalledGame> _installed = [];
    private string _filter = "all";
    private string _search = string.Empty;
    private CatalogSortOrder _sortOrder = CatalogSortOrder.NameAscending;
    private Button? _activeNav;
    private string? _connectedAddress;
    private readonly Button? _startViewButton;

    public MainWindow()
    {
        InitializeComponent();
        GamesGrid.ItemsSource = _cards;

        _ = LoadSettingsAsync();

        UpdateStorageCard();
        SetActiveNav(NavCatalog);
        ChipAll.Classes.Add("active");

        ChannelsListBox.ItemsSource = _chatChannels;
        MembersListBox.ItemsSource = _chatMembers;
        MessagesList.ItemsSource = _chatMessages;
        ChannelsListBox.SelectionChanged += OnChannelSelected;

        // Vista inicial alternativa para pruebas/desarrollo: LANFORGE_START_VIEW=settings|downloads|chat.
        _startViewButton = Environment.GetEnvironmentVariable("LANFORGE_START_VIEW") switch
        {
            "settings" => NavSettings,
            "downloads" => NavDownloads,
            "chat" => NavChat,
            _ => null,
        };
        if (_startViewButton is not null)
        {
            OnNavClick(_startViewButton, new Avalonia.Interactivity.RoutedEventArgs());
        }

        Loaded += async (_, _) =>
        {
            var trust = await ReadLastTrustAsync();
            if (trust is not null)
            {
                AddressBox.Text = trust.BaseUrl.Replace("http://", string.Empty);
                StatusText.Text = "Hay un servidor de confianza guardado. Pulsa Conectar.";
                if (await GetSettingBoolAsync(SettingAutoConnect, defaultValue: true))
                {
                    await ConnectAsync();
                }
            }
        };
    }

    // ---------- Ajustes ----------

    private async Task LoadSettingsAsync()
    {
        using var store = OpenStore();
        var playerName = await store.GetSettingAsync(SettingPlayerName);
        var name = string.IsNullOrWhiteSpace(playerName) ? Environment.MachineName : playerName;
        PlayerNameBox.Text = name;
        SetUserCard(name);
        ToggleConfirmUninstall.IsChecked = await GetSettingBoolAsync(SettingConfirmUninstall, defaultValue: true);
        ToggleAutoConnect.IsChecked = await GetSettingBoolAsync(SettingAutoConnect, defaultValue: true);
    }

    private async Task<bool> GetSettingBoolAsync(string key, bool defaultValue)
    {
        using var store = OpenStore();
        var value = await store.GetSettingAsync(key);
        return value is null ? defaultValue : string.Equals(value, "true", StringComparison.Ordinal);
    }

    private SqliteClientStore OpenStore() =>
        new(Path.Combine(_options.DataDirectory, "lanforge-client.db"));

    private void SetUserCard(string name)
    {
        UserNameText.Text = name;
        UserInitials.Text = name.Length >= 2 ? name[..2].ToUpperInvariant() : "PC";
    }

    private async void OnSaveSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SaveSettingsCoreAsync();
        PlayerHintText.Text = "Cambios guardados.";
        StatusText.Text = "Ajustes guardados.";
    }

    private async Task SaveSettingsCoreAsync()
    {
        var name = string.IsNullOrWhiteSpace(PlayerNameBox.Text) ? Environment.MachineName : PlayerNameBox.Text.Trim();
        using var store = OpenStore();
        await store.SaveSettingAsync(SettingPlayerName, name);
        await store.SaveSettingAsync(SettingConfirmUninstall, ToggleConfirmUninstall.IsChecked == true ? "true" : "false");
        await store.SaveSettingAsync(SettingAutoConnect, ToggleAutoConnect.IsChecked == true ? "true" : "false");
        SetUserCard(name);
    }

    private async void OnResetSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        PlayerNameBox.Text = Environment.MachineName;
        ToggleConfirmUninstall.IsChecked = true;
        ToggleAutoConnect.IsChecked = true;
        await SaveSettingsCoreAsync();
        PlayerHintText.Text = "Valores por defecto restaurados.";
        StatusText.Text = "Ajustes restablecidos.";
    }

    private async void OnTestConnection(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var session = _session ?? await ClientHost.CreateAsync(_options, AddressBox.Text ?? string.Empty);
            var result = await session.SyncAsync(confirmNewServer: false);
            StatusText.Text = $"Conexión correcta con {result.ServerName} ({result.EntryCount} juegos en el catálogo).";
        }
        catch (TrustRequiredException)
        {
            StatusText.Text = "El servidor responde, pero aún no es de confianza. Pulsa Conectar para confirmar su fingerprint.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Sin conexión: {ex.Message}";
        }
    }

    // ---------- Navegación ----------

    private void OnNavClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        SetActiveNav(button);
        CatalogView.IsVisible = button == NavCatalog;
        DownloadsView.IsVisible = button == NavDownloads;
        SettingsView.IsVisible = button == NavSettings;
        ChatView.IsVisible = button == NavChat;
        if (button == NavDownloads)
        {
            _ = RefreshOperationsAsync();
        }
        else if (button == NavChat)
        {
            _ = RefreshChatAsync();
        }
    }

    private void SetActiveNav(Button button)
    {
        // Estado por clase (ver ^.active en el ControlTheme NavItem): los valores locales
        // sobre controles con Theme rompen el re-render del contenido.
        _activeNav?.Classes.Remove("active");
        _activeNav = button;
        button.Classes.Add("active");
    }

    private IBrush Brush(string key) => (IBrush)this.Resources[key]!;

    // ---------- Sidebar ----------

    private void UpdateStorageCard()
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_options.InstallRoot)) ?? "C:\\";
            var drive = new DriveInfo(root);
            var usedPercent = (int)((drive.TotalSize - drive.AvailableFreeSpace) * 100 / drive.TotalSize);
            StorageFill.Width = Math.Max(2, 200 * usedPercent / 100.0);
            StorageText.Text = $"{GameCardItem.FormatBytes(drive.AvailableFreeSpace)} libres de {GameCardItem.FormatBytes(drive.TotalSize)}";
        }
        catch (Exception)
        {
            StorageText.Text = "No se pudo leer el espacio en disco.";
        }
    }

    // ---------- Conexión ----------

    private async Task<ServerTrustInfo?> ReadLastTrustAsync()
    {
        try
        {
            using var store = new SqliteClientStore(Path.Combine(_options.DataDirectory, "lanforge-client.db"));
            return await store.GetServerTrustAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async void OnConnect(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await ConnectAsync();

    private async Task ConnectAsync()
    {
        try
        {
            StatusText.Text = "Conectando…";
            _session = await ClientHost.CreateAsync(_options, AddressBox.Text ?? string.Empty);
            _connectedAddress = AddressBox.Text?.Trim();
            await SyncAsync(confirm: false);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error de conexión: {ex.Message}";
        }
    }

    private async Task SyncAsync(bool confirm)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            var result = await _session.SyncAsync(confirm);
            UpdateServerPill(connected: true, result.ServerName, _connectedAddress ?? string.Empty, $"{result.EntryCount} juegos");
            UpdateServerCard(connected: true, result.ServerName, _connectedAddress ?? string.Empty, result.ServerFingerprint.ToString(), $"{result.EntryCount} juegos");
            StatusText.Text = string.Empty;
            var targetNav = _startViewButton ?? NavCatalog;
            SetActiveNav(targetNav);
            OnNavClick(targetNav, new Avalonia.Interactivity.RoutedEventArgs());
            await RefreshCatalogAsync();
            await ConnectChatAsync();
        }
        catch (TrustRequiredException ex)
        {
            var trusted = await TrustDialog.ConfirmAsync(this, ex.ServerName, ex.BaseUrl, ex.PresentedFingerprint);
            if (trusted)
            {
                await SyncAsync(confirm: true);
            }
            else
            {
                StatusText.Text = "Servidor rechazado. Pide el fingerprint correcto al organizador.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void UpdateServerPill(bool connected, string name, string address, string state)
    {
        ServerDot.Fill = Brush(connected ? "SuccessBrush" : "TextMutedBrush");
        ServerNameText.Text = connected ? name : "Sin conexión";
        ServerAddressText.Text = connected ? address : string.Empty;
        ServerStateText.Text = connected ? state : string.Empty;
    }

    private void UpdateServerCard(bool connected, string name, string address, string fingerprint, string games)
    {
        ServerCardDot.Background = Brush(connected ? "SuccessBrush" : "TextMutedBrush");
        ServerCardStatus.Text = connected ? "Conectado" : "Sin conexión";
        ServerCardStatus.Foreground = Brush(connected ? "SuccessBrush" : "TextMutedBrush");
        ServerCardGames.Text = connected ? games : string.Empty;
        SettingsServerName.Text = connected ? name : "—";
        SettingsServerAddress.Text = connected ? address : "—";
        SettingsFingerprint.Text = connected ? fingerprint : string.Empty;
    }

    private async void OnDiscover(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        StatusText.Text = "Buscando servidores por mDNS…";
        DiscoveredList.IsVisible = true;
        var discovered = await ServerDiscovery.DiscoverAsync(TimeSpan.FromSeconds(3));
        if (discovered.Count == 0)
        {
            StatusText.Text = "No se encontró ningún servidor. Introdúcelo manualmente (IP:puerto).";
            DiscoveredList.IsVisible = false;
            return;
        }

        DiscoveredList.ItemsSource = discovered
            .Select(d => $"{d.Name} — {d.IpAddress}:{d.Port}")
            .ToArray();
        DiscoveredList.Tag = discovered;
        DiscoveredList.SelectionChanged += (_, _) =>
        {
            if (DiscoveredList.SelectedIndex >= 0 && DiscoveredList.Tag is IReadOnlyList<DiscoveredServer> servers)
            {
                var selected = servers[DiscoveredList.SelectedIndex];
                AddressBox.Text = $"{selected.IpAddress}:{selected.Port}";
            }
        };
        StatusText.Text = $"Encontrados {discovered.Count} servidor(es). Selecciona uno y pulsa Conectar.";
    }

    private async void OnForgetServer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_session is null)
        {
            return;
        }

        if (_chat is not null)
        {
            await _chat.DisconnectAsync();
            _chat = null;
        }

        _chatMessages.Clear();
        _chatChannels.Clear();
        _chatMembers.Clear();

        using var store = OpenStore();
        await store.ClearServerTrustAsync();
        UpdateServerCard(connected: false, string.Empty, string.Empty, string.Empty, string.Empty);
        StatusText.Text = "Servidor olvidado. El próximo contacto pedirá confirmar el fingerprint.";
    }

    // ---------- Chat (Jugadores) ----------

    private async Task ConnectChatAsync()
    {
        try
        {
            _chat ??= new SignalRChatService();
            if (_chat.IsConnected || string.IsNullOrWhiteSpace(_connectedAddress))
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(PlayerNameBox.Text) ? Environment.MachineName : PlayerNameBox.Text.Trim();
            _chat.MessageReceived += OnChatMessage;
            _chat.ChannelsChanged += OnChannelsChanged;
            _chat.MembersChanged += OnMembersChanged;
            await _chat.ConnectAsync($"http://{_connectedAddress}", name);
            await _chat.JoinAsync(LanForge.Shared.Chat.ChatHubContract.GeneralChannel);
            await RefreshChatAsync();
        }
        catch (Exception)
        {
            // El chat es opcional: no rompe la conexión principal.
        }
    }

    private async Task RefreshChatAsync()
    {
        if (_chat is null || !_chat.IsConnected)
        {
            _chatChannels.Clear();
            _chatMembers.Clear();
            return;
        }

        var channels = await _chat.ListChannelsAsync();
        _chatChannels.Clear();
        foreach (var channel in channels)
        {
            _chatChannels.Add(ChatChannelItem.From(channel, _catalog));
        }

        ChannelCountText.Text = channels.Count.ToString();
        GameForRoomBox.ItemsSource = _catalog
            .Where(e => !string.Equals(e.GameId, _currentChannel, StringComparison.Ordinal))
            .Select(e => e.Title)
            .ToArray();

        await SwitchChannelViewAsync(_currentChannel);
    }

    private async Task SwitchChannelViewAsync(string channel)
    {
        _currentChannel = channel;
        _chatMessages.Clear();

        var members = _chat is { IsConnected: true } ? await _chat.ListMembersAsync(channel) : [];
        _chatMembers.Clear();
        var you = string.IsNullOrWhiteSpace(PlayerNameBox.Text) ? Environment.MachineName : PlayerNameBox.Text.Trim();
        foreach (var member in members)
        {
            _chatMembers.Add(member == you ? $"{member} (tú)" : member);
        }

        MembersCountText.Text = members.Count.ToString();
        ChatChannelTitle.Text = ChannelDisplayName(channel);
        ChatChannelSub.Text = $"{members.Count} miembro(s) — chat efímero de la sala";
        ChatSubtitle.Text = $"{members.Count} conectado(s) en {ChannelDisplayName(channel)}";
    }

    private string ChannelDisplayName(string channel) => ChatChannelItem.DisplayNameFor(channel, _catalog);

    private void OnChatMessage(LanForge.Shared.Chat.ChatMessage message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!string.Equals(message.Channel, _currentChannel, StringComparison.Ordinal))
            {
                return;
            }

            _chatMessages.Add(message);
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => MessagesScroll.ScrollToEnd(),
                Avalonia.Threading.DispatcherPriority.Background);
        });
    }

    private void OnChannelsChanged(IReadOnlyList<LanForge.Shared.Chat.ChatChannelInfo> channels)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _chatChannels.Clear();
            foreach (var channel in channels)
            {
                _chatChannels.Add(ChatChannelItem.From(channel, _catalog));
            }

            ChannelCountText.Text = channels.Count.ToString();
        });
    }

    private void OnMembersChanged(string channel, IReadOnlyList<string> members)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (!string.Equals(channel, _currentChannel, StringComparison.Ordinal))
            {
                return;
            }

            await SwitchChannelViewAsync(channel);
        });
    }

    private async void OnChannelSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ChannelsListBox.SelectedItem is not ChatChannelItem channel || _chat is null)
        {
            return;
        }

        // Solo un canal activo: las salas efímeras se abandonan al cambiar (el servidor las borra al vaciarse).
        if (!string.Equals(_currentChannel, channel.Name, StringComparison.Ordinal) &&
            !string.Equals(channel.Name, LanForge.Shared.Chat.ChatHubContract.GeneralChannel, StringComparison.Ordinal))
        {
            if (!string.Equals(_currentChannel, LanForge.Shared.Chat.ChatHubContract.GeneralChannel, StringComparison.Ordinal))
            {
                await _chat.LeaveAsync(_currentChannel);
            }

            await _chat.JoinAsync(channel.Name);
        }

        await SwitchChannelViewAsync(channel.Name);
    }

    private async void OnCreateRoom(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_chat is null || GameForRoomBox.SelectedItem is not string title)
        {
            return;
        }

        var game = _catalog.FirstOrDefault(g => g.Title == title);
        if (game is null)
        {
            return;
        }

        await _chat.JoinAsync(game.GameId);
        await SwitchChannelViewAsync(game.GameId);
    }

    private async void OnSendMessage(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => await SendChatMessageAsync();

    private void OnChatInputKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            _ = SendChatMessageAsync();
        }
    }

    private async Task SendChatMessageAsync()
    {
        if (_chat is null || string.IsNullOrWhiteSpace(ChatInput.Text))
        {
            return;
        }

        try
        {
            await _chat.SendAsync(_currentChannel, ChatInput.Text);
            ChatInput.Text = string.Empty;
        }
        catch (Exception)
        {
            StatusText.Text = "No se pudo enviar el mensaje.";
        }
    }

    // ---------- Catálogo ----------

    private async Task RefreshCatalogAsync()
    {
        if (_session is null)
        {
            return;
        }

        _catalog = (await _session.GetLocalCatalogAsync()).ToList();
        _installed = (await _session.GetInstalledAsync()).ToList();
        ApplyCatalogView();
    }

    private void ApplyCatalogView()
    {
        var installedByGame = _installed.ToDictionary(g => g.GameId, g => g.Version);
        var query = _catalog.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(_search))
        {
            query = query.Where(e => e.Title.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                                     e.GameId.Contains(_search, StringComparison.OrdinalIgnoreCase));
        }

        query = _filter switch
        {
            "installed" => query.Where(e => installedByGame.ContainsKey(e.GameId)),
            "pending" => query.Where(e => !installedByGame.ContainsKey(e.GameId)),
            _ => query,
        };

        var items = CatalogSort.Apply(query, _sortOrder);
        _cards.Clear();
        foreach (var entry in items)
        {
            installedByGame.TryGetValue(entry.GameId, out var installedVersion);
            _cards.Add(new GameCardItem(entry, installedVersion, InstallAsync, PlayAsync, UninstallAsync)
            {
                CoverAsset = CoverFor(entry),
            });
        }

        CatalogSubtitle.Text = $"{_catalog.Count} títulos disponibles en el servidor";
        UpdateBanner(items);
        _ = EnsureServerCoversAsync(items.ToList());
    }

    private string CoverFor(CatalogEntry entry) =>
        CoverResolver.CachedCoverUri(_options.DataDirectory, entry.CoverPath)
        ?? CoverResolver.FallbackAsset(entry.GameId, CoverAssets);

    private string? GetCachedCoverPath(string? coverFileName) =>
        CoverResolver.CachedCoverUri(_options.DataDirectory, coverFileName);

    private async Task EnsureServerCoversAsync(List<CatalogEntry> entries)
    {
        if (_session is null)
        {
            return;
        }

        var pending = entries.Where(e => e.CoverPath is not null && GetCachedCoverPath(e.CoverPath) is null).ToList();
        foreach (var entry in pending)
        {
            try
            {
                var bytes = await _session.DownloadCoverAsync(entry.CoverPath!);
                if (bytes is null)
                {
                    continue;
                }

                var path = CoverResolver.CoverTargetPath(_options.DataDirectory, entry.CoverPath)
                    ?? throw new InvalidOperationException($"Nombre de portada no valido: '{entry.CoverPath}'.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes);
                var card = _cards.FirstOrDefault(c => c.Entry.GameId == entry.GameId);
                if (card is not null)
                {
                    card.CoverAsset = new Uri(path).AbsoluteUri;
                }
            }
            catch (Exception)
            {
                // La portada es opcional: se reintenta en el próximo refresco.
            }
        }
    }

    private GameCardItem? _bannerGame;

    private void UpdateBanner(IReadOnlyList<CatalogEntry> visible)
    {
        var featured = visible.FirstOrDefault() ?? _catalog.FirstOrDefault();
        if (featured is null)
        {
            BannerTitle.Text = "Sin juegos publicados";
            BannerMeta.Text = "El organizador aún no ha publicado ningún paquete.";
            BannerAction.IsVisible = false;
            BannerHint.Text = string.Empty;
            _bannerGame = null;
            return;
        }

        var card = _cards.FirstOrDefault(c => c.Entry.GameId == featured.GameId);
        _bannerGame = card;
        BannerTitle.Text = featured.Title;
        BannerMeta.Text = $"Disponible en LAN — {GameCardItem.FormatBytes(featured.TotalSizeBytes)}";
        BannerAction.IsVisible = card is not null && !card.IsInstalledState;
        BannerAction.Content = card?.State == GameCardState.UpdateAvailable ? "Actualizar ahora" : "Instalar ahora";
        BannerHint.Text = card is { IsInstalledState: true } ? "Ya está instalado y listo para jugar" : string.Empty;
    }

    private void OnBannerAction(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_bannerGame is not null)
        {
            _bannerGame.InstallCommand.Execute(null);
        }
    }

    private async void OnRefreshCatalog(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_session is null)
        {
            StatusText.Text = "Conéctate a un servidor para actualizar el catálogo.";
            return;
        }

        try
        {
            StatusText.Text = "Actualizando catálogo…";
            await RefreshCatalogAsync();
            StatusText.Text = $"Catálogo actualizado: {_catalog.Count} títulos.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"No se pudo actualizar el catálogo: {ex.Message}";
        }
    }

    private void OnSortClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _sortOrder = _sortOrder == CatalogSortOrder.NameAscending
            ? CatalogSortOrder.NameDescending
            : CatalogSortOrder.NameAscending;
        SortText.Text = _sortOrder == CatalogSortOrder.NameAscending
            ? "Ordenar por nombre (A-Z)"
            : "Ordenar por nombre (Z-A)";
        ApplyCatalogView();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text ?? string.Empty;
        ApplyCatalogView();
    }

    private void OnChipClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button chip)
        {
            return;
        }

        StyleChip(ChipAll, active: chip == ChipAll);
        StyleChip(ChipInstalled, active: chip == ChipInstalled);
        StyleChip(ChipPending, active: chip == ChipPending);
        _filter = chip == ChipInstalled ? "installed" : chip == ChipPending ? "pending" : "all";
        ApplyCatalogView();
    }

    private static void StyleChip(Button chip, bool active)
    {
        if (active)
        {
            chip.Classes.Add("active");
        }
        else
        {
            chip.Classes.Remove("active");
        }
    }

    // ---------- Acciones de juegos ----------

    private async Task InstallAsync(GameCardItem card)
    {
        if (_session is null)
        {
            return;
        }

        card.SetDownloading(true);
        try
        {
            var progress = new Progress<DownloadProgress>(p =>
                card.ProgressPercent = p.TotalBytes > 0 ? (int)(p.CompletedBytes * 100 / p.TotalBytes) : 0);
            await _session.InstallAsync(card.Entry.GameId, progress);
            card.SetInstalled();
            await RefreshCatalogAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error de instalación: {ex.Message}";
            SetActiveNav(NavSettings);
            OnNavClick(NavSettings, new Avalonia.Interactivity.RoutedEventArgs());
        }
    }

    private async Task PlayAsync(GameCardItem card)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            await _session.PlayAsync(card.Entry.GameId);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error al lanzar: {ex.Message}";
        }
    }

    private async Task UninstallAsync(GameCardItem card)
    {
        if (_session is null)
        {
            return;
        }

        if (ToggleConfirmUninstall.IsChecked == true)
        {
            var confirmed = await ConfirmDialog.ShowAsync(
                this,
                "Desinstalar juego",
                $"Se borrará '{card.Entry.Title}' de este equipo ({GameCardItem.FormatBytes(card.Entry.TotalSizeBytes)}). Podrás reinstalarlo del servidor cuando quieras.",
                "Desinstalar");
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            await _session.UninstallAsync(card.Entry.GameId);
            card.SetUninstalled();
            await RefreshCatalogAsync();
            StatusText.Text = $"{card.Entry.Title} desinstalado.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error al desinstalar: {ex.Message}";
        }
    }

    // ---------- Descargas ----------

    private async Task RefreshOperationsAsync()
    {
        if (_session is null)
        {
            OperationsList.ItemsSource = null;
            return;
        }

        using var store = new SqliteClientStore(Path.Combine(_options.DataDirectory, "lanforge-client.db"));
        var operations = await store.ListOperationsAsync();
        OperationsList.ItemsSource = operations
            .Select(o => $"{o.StartedUtc.LocalDateTime:g}   [{o.Status}]   {o.Type}   {o.Detail ?? string.Empty}")
            .ToArray();
    }
}
