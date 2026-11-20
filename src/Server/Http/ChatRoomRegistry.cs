using System.Collections.Concurrent;
using LanForge.Shared.Chat;

namespace LanForge.Server.Http;

/// <summary>
/// Registro en memoria de canales efímeros: un canal existe mientras tenga miembros.
/// "#general" es permanente. Thread-safe; vive como singleton en el contenedor.
/// </summary>
public sealed class ChatRoomRegistry
{
    public const string GeneralChannel = LanForge.Shared.Chat.ChatHubContract.GeneralChannel;

    private readonly ConcurrentDictionary<string, ChannelState> _channels = new(StringComparer.Ordinal);

    public ChatRoomRegistry()
    {
        // El canal general siempre existe.
        _channels[GeneralChannel] = new ChannelState();
    }

    /// <summary>Añade una conexión a un canal. Devuelve true si el canal se acaba de crear.</summary>
    public bool Join(string connectionId, string userName, string channel)
    {
        ValidateChannel(channel);
        var state = _channels.GetOrAdd(channel, _ => new ChannelState());
        lock (state)
        {
            state.Members[connectionId] = userName;
        }

        return false;
    }

    /// <summary>Quita una conexión de un canal. Devuelve true si el canal quedó vacío y se eliminó.</summary>
    public bool Leave(string connectionId, string channel)
    {
        if (!_channels.TryGetValue(channel, out var state))
        {
            return false;
        }

        var removed = false;
        lock (state)
        {
            removed = state.Members.TryRemove(connectionId, out _);
        }

        if (removed && !IsPermanent(channel) && state.Members.IsEmpty)
        {
            // Solo eliminamos si seguimos siendo el estado registrado (race con un Join simultáneo).
            _channels.TryRemove(new KeyValuePair<string, ChannelState>(channel, state));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Quita una conexión de todos sus canales y devuelve los canales cuya lista
    /// de miembros cambió. El hub los vuelve a emitir para que los clientes vean
    /// tanto desconexiones como cambios de nombre por reconexión.
    /// </summary>
    public IReadOnlyList<string> LeaveAll(string connectionId)
    {
        var changed = new List<string>();
        foreach (var (channel, _) in _channels)
        {
            if (IsMember(connectionId, channel))
            {
                Leave(connectionId, channel);
                changed.Add(channel);
            }
        }

        return changed;
    }

    public IReadOnlyList<string> Members(string channel)
    {
        if (!_channels.TryGetValue(channel, out var state))
        {
            return [];
        }

        lock (state)
        {
            return state.Members.Values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <summary>
    /// Indica si una conexion concreta pertenece a un canal. La pertenencia se guarda por
    /// connectionId: dos jugadores pueden llamarse igual y eso no los hace el mismo miembro.
    /// </summary>
    public bool IsMember(string connectionId, string channel)
    {
        if (!_channels.TryGetValue(channel, out var state))
        {
            return false;
        }

        lock (state)
        {
            return state.Members.ContainsKey(connectionId);
        }
    }

    public IReadOnlyList<ChatChannelInfo> List() =>
        _channels
            .Select(pair => new ChatChannelInfo(pair.Key, Members(pair.Key).Count))
            .OrderBy(c => c.Name == GeneralChannel ? 0 : 1)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool Exists(string channel) => _channels.ContainsKey(channel);

    public static void ValidateChannel(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel) || channel.Length > 64 ||
            channel.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '#')))
        {
            throw new ArgumentException($"Nombre de canal inválido: '{channel}'.", nameof(channel));
        }
    }

    private static bool IsPermanent(string channel) => string.Equals(channel, GeneralChannel, StringComparison.Ordinal);

    private sealed class ChannelState
    {
        public ConcurrentDictionary<string, string> Members { get; } = new();
    }
}
