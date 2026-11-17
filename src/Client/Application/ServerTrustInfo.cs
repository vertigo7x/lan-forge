namespace LanForge.Client.Application;

/// <summary>Fingerprint fijado por el usuario (TOFU) para un servidor concreto.</summary>
public sealed record ServerTrustInfo(
    string ServerId,
    string Fingerprint,
    string BaseUrl,
    DateTimeOffset PinnedUtc);
