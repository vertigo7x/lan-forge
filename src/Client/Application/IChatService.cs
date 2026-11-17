using LanForge.Shared.Chat;

namespace LanForge.Client.Application;

/// <summary>Puerto del chat de la sala (SignalR). Efímero: sin historial.</summary>
public interface IChatService
{
    bool IsConnected { get; }

    event Action<ChatMessage>? MessageReceived;
    event Action<IReadOnlyList<ChatChannelInfo>>? ChannelsChanged;
    event Action<string, IReadOnlyList<string>>? MembersChanged;

    Task ConnectAsync(string baseUrl, string userName, CancellationToken cancellationToken = default);

    Task JoinAsync(string channel, CancellationToken cancellationToken = default);

    Task LeaveAsync(string channel, CancellationToken cancellationToken = default);

    Task SendAsync(string channel, string text, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatChannelInfo>> ListChannelsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListMembersAsync(string channel, CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
