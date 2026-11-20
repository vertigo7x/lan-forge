using LanForge.Shared.Models;
using LanForge.Shared.Protocol;
using LanForge.Shared.Security;

namespace LanForge.Client.Application;

public sealed record SyncResult(
    int EntryCount,
    Fingerprint ServerFingerprint,
    string ServerName,
    string? LanPartyLogoPath);

/// <summary>Sincronización del catálogo: handshake + confianza TOFU + descarga de entradas.</summary>
public sealed class CatalogSyncService
{
    private readonly IServerApi _api;
    private readonly TrustManager _trust;
    private readonly IClientStore _store;

    public CatalogSyncService(IServerApi api, TrustManager trust, IClientStore store)
    {
        _api = api;
        _trust = trust;
        _store = store;
    }

    /// <summary>
    /// Sincroniza el catálogo. Si el servidor es nuevo, solo se fija el fingerprint cuando
    /// <paramref name="confirmNewServer"/> es true (el usuario lo confirmó fuera de banda).
    /// </summary>
    public async Task<SyncResult> SyncAsync(bool confirmNewServer = false, CancellationToken cancellationToken = default)
    {
        var hello = await _api.GetHelloAsync(cancellationToken);
        if (!ProtocolVersion.IsCompatible(hello.ProtocolVersion))
        {
            throw new ProtocolMismatchException(hello.ProtocolVersion, ProtocolVersion.Current);
        }

        var check = await _trust.CheckAsync(hello, cancellationToken);
        switch (check.State)
        {
            case TrustState.FingerprintMismatch:
                throw new FingerprintMismatchException(check.Pinned!.ToString(), check.Presented.ToString());
            case TrustState.NewServer when !confirmNewServer:
                throw new TrustRequiredException(check.Presented.ToString(), hello.ServerName, _api.BaseUrl);
            case TrustState.NewServer:
                await _trust.PinAsync(hello, _api.BaseUrl, cancellationToken);
                break;
        }

        var entries = await _api.GetCatalogAsync(cancellationToken);
        await _store.ReplaceCatalogAsync(entries, cancellationToken);
        return new SyncResult(entries.Count, check.Presented, hello.ServerName, hello.LanPartyLogoPath);
    }

    public async Task<IReadOnlyList<CatalogEntry>> GetLocalCatalogAsync(CancellationToken cancellationToken = default) =>
        await _store.GetCatalogAsync(cancellationToken);
}
