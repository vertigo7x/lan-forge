using LanForge.Shared.Protocol;
using LanForge.Shared.Security;

namespace LanForge.Client.Application;

public enum TrustState
{
    /// <summary>Sin fingerprint fijado: el usuario debe confirmarlo fuera de banda (TOFU).</summary>
    NewServer,

    /// <summary>El fingerprint presentado coincide con el fijado.</summary>
    Trusted,

    /// <summary>El fingerprint presentado difiere del fijado: rechazar.</summary>
    FingerprintMismatch,
}

public sealed record TrustCheck(TrustState State, Fingerprint? Pinned, Fingerprint Presented);

/// <summary>Gestión de la confianza TOFU: fijar el fingerprint y verificarlo en cada contacto.</summary>
public sealed class TrustManager
{
    private readonly IClientStore _store;

    public TrustManager(IClientStore store)
    {
        _store = store;
    }

    public async Task<TrustCheck> CheckAsync(HelloResponse hello, CancellationToken cancellationToken = default)
    {
        var presented = Fingerprint.Parse(hello.Fingerprint);
        var pinned = await _store.GetServerTrustAsync(cancellationToken);
        if (pinned is null)
        {
            return new TrustCheck(TrustState.NewServer, null, presented);
        }

        var pinnedFingerprint = Fingerprint.Parse(pinned.Fingerprint);
        return pinnedFingerprint.Equals(presented) && string.Equals(pinned.ServerId, hello.ServerId, StringComparison.Ordinal)
            ? new TrustCheck(TrustState.Trusted, pinnedFingerprint, presented)
            : new TrustCheck(TrustState.FingerprintMismatch, pinnedFingerprint, presented);
    }

    public async Task PinAsync(HelloResponse hello, string baseUrl, CancellationToken cancellationToken = default)
    {
        await _store.SaveServerTrustAsync(
            new ServerTrustInfo(hello.ServerId, hello.Fingerprint, baseUrl, DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public async Task<Fingerprint> GetTrustedFingerprintAsync(CancellationToken cancellationToken = default)
    {
        var pinned = await _store.GetServerTrustAsync(cancellationToken)
            ?? throw new LanForgeClientException("No hay servidor de confianza fijado. Sincroniza primero.");
        return Fingerprint.Parse(pinned.Fingerprint);
    }
}
