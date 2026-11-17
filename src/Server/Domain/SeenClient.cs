namespace LanForge.Server.Domain;

/// <summary>Cliente visto por el servidor (vía header X-LanForge-Client-Id).</summary>
public sealed record SeenClient(
    string ClientId,
    DateTimeOffset LastSeenUtc,
    string? IpAddress = null);
