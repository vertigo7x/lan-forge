using LanForge.Shared.Chat;
using LanForge.Shared.Models;

namespace LanForge.Client.ViewModels;

/// <summary>
/// Canal de chat tal como se pinta en la lista de canales. El id del canal es el gameId
/// de las salas por juego, asi que la vista necesita el titulo del juego, no el id.
/// </summary>
public sealed class ChatChannelItem
{
    public ChatChannelItem(string name, string displayName, int members)
    {
        Name = name;
        DisplayName = displayName;
        Members = members;
    }

    /// <summary>Identificador del canal (gameId en las salas por juego).</summary>
    public string Name { get; }

    /// <summary>Texto que ve el jugador: el titulo del juego o "general".</summary>
    public string DisplayName { get; }

    public int Members { get; }

    public static ChatChannelItem From(ChatChannelInfo info, IReadOnlyList<CatalogEntry> catalog) =>
        new(info.Name, DisplayNameFor(info.Name, catalog), info.Members);

    /// <summary>Resuelve el nombre visible de un canal a partir del catalico conocido.</summary>
    public static string DisplayNameFor(string channel, IReadOnlyList<CatalogEntry> catalog)
    {
        if (string.Equals(channel, ChatHubContract.GeneralChannel, StringComparison.Ordinal))
        {
            return ChatHubContract.GeneralChannel;
        }

        var title = catalog.FirstOrDefault(e => string.Equals(e.GameId, channel, StringComparison.Ordinal))?.Title;
        return string.IsNullOrWhiteSpace(title) ? channel : title;
    }
}
