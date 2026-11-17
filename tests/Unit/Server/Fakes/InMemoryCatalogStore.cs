using LanForge.Server.Application;
using LanForge.Server.Domain;

namespace LanForge.UnitTests.Server.Fakes;

/// <summary>Implementación en memoria de ICatalogStore para tests unitarios del catálogo.</summary>
public sealed class InMemoryCatalogStore : ICatalogStore
{
    private readonly Dictionary<string, Game> _games = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Package> _packages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SeenClient> _seenClients = new(StringComparer.Ordinal);
    private readonly List<ServerOperation> _operations = [];

    public Task UpsertGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        _games[game.Id] = game;
        return Task.CompletedTask;
    }

    public Task<Game?> GetGameAsync(string gameId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_games.GetValueOrDefault(gameId));

    public Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Game>>(_games.Values.OrderBy(g => g.Id, StringComparer.Ordinal).ToArray());

    public Task AddPackageAsync(Package package, CancellationToken cancellationToken = default)
    {
        if (_packages.ContainsKey(package.PackageId))
        {
            throw new InvalidOperationException($"El paquete '{package.PackageId}' ya existe.");
        }

        _packages[package.PackageId] = package;
        return Task.CompletedTask;
    }

    public Task ReplacePackageAsync(Package package, CancellationToken cancellationToken = default)
    {
        _packages[package.PackageId] = package;
        return Task.CompletedTask;
    }

    public Task<Package?> GetPackageAsync(string packageId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_packages.GetValueOrDefault(packageId));

    public Task<IReadOnlyList<Package>> ListLatestPackagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Package>>(_packages.Values.OrderBy(p => p.GameId, StringComparer.Ordinal).ToArray());

    public Task UpsertSeenClientAsync(SeenClient client, CancellationToken cancellationToken = default)
    {
        _seenClients[client.ClientId] = client;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SeenClient>> ListSeenClientsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SeenClient>>(_seenClients.Values.OrderBy(c => c.ClientId, StringComparer.Ordinal).ToArray());

    public Task RecordOperationAsync(ServerOperation operation, CancellationToken cancellationToken = default)
    {
        _operations.Add(operation);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ServerOperation>> ListOperationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServerOperation>>(_operations.ToArray());
}
