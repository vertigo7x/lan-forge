namespace LanForge.Shared.Protocol;

/// <summary>Respuesta de GET /api/v1/hello: handshake inicial antes de operar.</summary>
public sealed record HelloResponse(
    int ProtocolVersion,
    string Fingerprint,
    string ServerName,
    string ServerId,
    string? LanPartyLogoPath = null);
