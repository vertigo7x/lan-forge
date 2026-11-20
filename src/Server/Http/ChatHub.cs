using LanForge.Shared.Chat;
using Microsoft.AspNetCore.SignalR;

namespace LanForge.Server.Http;

/// <summary>
/// Hub SignalR del chat de la LAN. Los mensajes son efímeros: solo se retransmiten al grupo
/// del canal; al quedar vacío, el canal se elimina (ver ChatRoomRegistry).
/// </summary>
public sealed class ChatHub : Hub
{
    public const string Path = LanForge.Shared.Chat.ChatHubContract.Path;

    public const string ReceiveMessageMethod = LanForge.Shared.Chat.ChatHubContract.ReceiveMessageMethod;
    public const string ReceiveChannelsMethod = LanForge.Shared.Chat.ChatHubContract.ReceiveChannelsMethod;
    public const string ReceiveMembersMethod = LanForge.Shared.Chat.ChatHubContract.ReceiveMembersMethod;

    private readonly ChatRoomRegistry _rooms;

    public ChatHub(ChatRoomRegistry rooms)
    {
        _rooms = rooms;
    }

    public async Task Join(string channel)
    {
        ChatRoomRegistry.ValidateChannel(channel);
        _rooms.Join(Context.ConnectionId, UserName(), channel);
        await Groups.AddToGroupAsync(Context.ConnectionId, channel);
        await BroadcastMembersAsync(channel);
        await BroadcastChannelsAsync();
    }

    public async Task Leave(string channel)
    {
        _rooms.Leave(Context.ConnectionId, channel);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, channel);
        await BroadcastMembersAsync(channel);
        await BroadcastChannelsAsync();
    }

    public async Task Send(string channel, string text)
    {
        ChatRoomRegistry.ValidateChannel(channel);
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > LanForge.Shared.Chat.ChatHubContract.MaxMessageLength)
        {
            return;
        }

        // Solo las conexiones que entraron en el canal pueden hablar en el. La pertenencia se
        // comprueba por connectionId: si se comprobara por nombre, cualquiera que se llamara
        // igual que un miembro podria escribir en el canal sin estar dentro.
        if (!_rooms.IsMember(Context.ConnectionId, channel))
        {
            return;
        }

        var message = new ChatMessage(channel, UserName(), text, DateTimeOffset.UtcNow);
        await Clients.Group(channel).SendAsync(ReceiveMessageMethod, message);
    }

    public Task<IReadOnlyList<ChatChannelInfo>> ListChannels() => Task.FromResult(_rooms.List());

    public Task<IReadOnlyList<string>> ListMembers(string channel) => Task.FromResult(_rooms.Members(channel));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var changedChannels = _rooms.LeaveAll(Context.ConnectionId);
        foreach (var channel in changedChannels)
        {
            await BroadcastMembersAsync(channel);
        }

        await BroadcastChannelsAsync();
        await base.OnDisconnectedAsync(exception);
    }

    private string UserName()
    {
        var name = Context.GetHttpContext()?.Request.Query["user"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(name) ? "jugador" : name.Trim();
    }

    private async Task BroadcastMembersAsync(string channel)
    {
        await Clients.Group(channel).SendAsync(ReceiveMembersMethod, channel, _rooms.Members(channel));
    }

    private async Task BroadcastChannelsAsync()
    {
        // Todos los clientes mantienen la lista de canales activos (la ve también la app del servidor).
        await Clients.All.SendAsync(ReceiveChannelsMethod, _rooms.List());
    }
}
