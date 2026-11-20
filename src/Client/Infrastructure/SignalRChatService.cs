using LanForge.Client.Application;
using LanForge.Shared.Chat;
using Microsoft.AspNetCore.SignalR.Client;

namespace LanForge.Client.Infrastructure;

/// <summary>Cliente SignalR real contra el ChatHub del servidor.</summary>
public sealed class SignalRChatService : IChatService
{
    private readonly object _channelsLock = new();
    private readonly HashSet<string> _joinedChannels = new(StringComparer.Ordinal);
    private HubConnection? _connection;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public event Action<ChatMessage>? MessageReceived;
    public event Action<IReadOnlyList<ChatChannelInfo>>? ChannelsChanged;
    public event Action<string, IReadOnlyList<string>>? MembersChanged;

    public async Task ConnectAsync(string baseUrl, string userName, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(CancellationToken.None);

        var url = $"{baseUrl.TrimEnd('/')}/api/v1/chat?user={Uri.EscapeDataString(userName)}";
        _connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<ChatMessage>(ChatHubContract.ReceiveMessageMethod,
            message => MessageReceived?.Invoke(message));
        _connection.On<IReadOnlyList<ChatChannelInfo>>(ChatHubContract.ReceiveChannelsMethod,
            channels => ChannelsChanged?.Invoke(channels));
        _connection.On<string, IReadOnlyList<string>>(ChatHubContract.ReceiveMembersMethod,
            (channel, members) => MembersChanged?.Invoke(channel, members));
        _connection.Reconnected += async _ => await RejoinChannelsAsync();

        await _connection.StartAsync(cancellationToken);
    }

    public async Task JoinAsync(string channel, CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            await _connection.InvokeAsync("Join", channel, cancellationToken);
            lock (_channelsLock)
            {
                _joinedChannels.Add(channel);
            }
        }
    }

    public async Task LeaveAsync(string channel, CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            await _connection.InvokeAsync("Leave", channel, cancellationToken);
            lock (_channelsLock)
            {
                _joinedChannels.Remove(channel);
            }
        }
    }

    public async Task SendAsync(string channel, string text, CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            await _connection.InvokeAsync("Send", channel, text, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ChatChannelInfo>> ListChannelsAsync(CancellationToken cancellationToken = default) =>
        _connection is null ? [] : await _connection.InvokeAsync<IReadOnlyList<ChatChannelInfo>>("ListChannels", cancellationToken);

    public async Task<IReadOnlyList<string>> ListMembersAsync(string channel, CancellationToken cancellationToken = default) =>
        _connection is null ? [] : await _connection.InvokeAsync<IReadOnlyList<string>>("ListMembers", channel, cancellationToken);

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            lock (_channelsLock)
            {
                _joinedChannels.Clear();
            }

            try
            {
                await _connection.StopAsync(cancellationToken);
                await _connection.DisposeAsync();
            }
            catch (Exception)
            {
                // Desconexión best-effort.
            }

            _connection = null;
        }
    }

    /// <summary>
    /// Una reconexión de SignalR crea una conexión nueva en el servidor, que ya no
    /// pertenece a ninguno de los grupos anteriores. Se vuelve a unir a los
    /// canales locales para seguir recibiendo mensajes aunque el usuario no haya
    /// vuelto a abrir la pantalla de Jugadores.
    /// </summary>
    private async Task RejoinChannelsAsync()
    {
        if (_connection is null)
        {
            return;
        }

        string[] channels;
        lock (_channelsLock)
        {
            channels = _joinedChannels.ToArray();
        }

        foreach (var channel in channels)
        {
            await _connection.InvokeAsync("Join", channel);
        }
    }
}
