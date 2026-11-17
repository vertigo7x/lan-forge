using LanForge.Client.Domain;
using LanForge.Shared.Models;

namespace LanForge.Client.Application;

/// <summary>Puerto de persistencia local del cliente (SQLite en producción, in-memory en tests).</summary>
public interface IClientStore
{
    Task<string> GetOrCreateClientIdAsync(CancellationToken cancellationToken = default);

    Task<ServerTrustInfo?> GetServerTrustAsync(CancellationToken cancellationToken = default);

    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);

    Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken = default);

    Task SaveServerTrustAsync(ServerTrustInfo trust, CancellationToken cancellationToken = default);

    Task ClearServerTrustAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default);

    Task ReplaceCatalogAsync(IEnumerable<CatalogEntry> entries, CancellationToken cancellationToken = default);

    Task<InstalledGame?> GetInstalledAsync(string gameId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstalledGame>> ListInstalledAsync(CancellationToken cancellationToken = default);

    Task SaveInstalledAsync(InstalledGame game, CancellationToken cancellationToken = default);

    Task RemoveInstalledAsync(string gameId, CancellationToken cancellationToken = default);

    Task RecordOperationAsync(ClientOperation operation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientOperation>> ListOperationsAsync(CancellationToken cancellationToken = default);
}
