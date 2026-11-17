namespace LanForge.Shared.Chat;

/// <summary>Mensaje de chat efímero (no se persiste; solo se retransmite a los miembros del canal).</summary>
public sealed record ChatMessage(string Channel, string Sender, string Text, DateTimeOffset SentUtc);

/// <summary>Canal de chat activo con su número de miembros conectados.</summary>
public sealed record ChatChannelInfo(string Name, int Members);

/// <summary>Miembro conectado a un canal.</summary>
public sealed record ChatMember(string Name);
