namespace LanForge.Shared.Chat;

/// <summary>Constantes del ChatHub (compartidas entre servidor y cliente; el cliente no referencia al servidor).</summary>
public static class ChatHubContract
{
    public const string Path = "/api/v1/chat";

    /// <summary>Canal permanente de toda la sala.</summary>
    public const string GeneralChannel = "general";

    public const string ReceiveMessageMethod = "Message";
    public const string ReceiveChannelsMethod = "Channels";
    public const string ReceiveMembersMethod = "Members";

    public const int MaxMessageLength = 500;
}
