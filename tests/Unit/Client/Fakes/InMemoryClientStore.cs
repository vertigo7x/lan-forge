using LanForge.Client.Application;
using LanForge.Client.Domain;
using LanForge.Shared.Models;

namespace LanForge.UnitTests.Client.Fakes;

public sealed class InMemoryClientStore : IClientStore
{
    private string? _clientId;
    private ServerTrustInfo? _trust;
    private List<CatalogEntry> _catalog = [];
    private readonly Dictionary<string, InstalledGame> _installed = new(StringComparer.Ordinal);
    private readonly List<ClientOperation> _operations = [];

    public Task<string> GetOrCreateClientIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_clientId ??= Guid.NewGuid().ToString("n"));

    public Task<ServerTrustInfo?> GetServerTrustAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_trust);

    private readonly Dictionary<string, string> _settings = new(StringComparer.Ordinal);

    public Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_settings.GetValueOrDefault(key));

    public Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _settings[key] = value;
        return Task.CompletedTask;
    }

    public Task SaveServerTrustAsync(ServerTrustInfo trust, CancellationToken cancellationToken = default)
    {
        _trust = trust;
        return Task.CompletedTask;
    }

    public Task ClearServerTrustAsync(CancellationToken cancellationToken = default)
    {
        _trust = null;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CatalogEntry>>(_catalog.ToArray());

    public Task ReplaceCatalogAsync(IEnumerable<CatalogEntry> entries, CancellationToken cancellationToken = default)
    {
        _catalog = entries.ToList();
        return Task.CompletedTask;
    }

    public Task<InstalledGame?> GetInstalledAsync(string gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_installed.GetValueOrDefault(gameId));

    public Task<IReadOnlyList<InstalledGame>> ListInstalledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstalledGame>>(_installed.Values.ToArray());

    public Task SaveInstalledAsync(InstalledGame game, CancellationToken cancellationToken = default)
    {
        _installed[game.GameId] = game;
        return Task.CompletedTask;
    }

    public Task RemoveInstalledAsync(string gameId, CancellationToken cancellationToken = default)
    {
        _installed.Remove(gameId);
        return Task.CompletedTask;
    }

    public Task RecordOperationAsync(ClientOperation operation, CancellationToken cancellationToken = default)
    {
        _operations.Add(operation);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ClientOperation>> ListOperationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ClientOperation>>(_operations.ToArray());
}
