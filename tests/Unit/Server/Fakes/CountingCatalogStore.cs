using LanForge.Server.Application;
using LanForge.Server.Domain;

namespace LanForge.UnitTests.Server.Fakes;

/// <summary>
/// Store en memoria que cuenta las consultas por juego. Sirve para comprobar que el
/// listado del catalogo no hace una consulta por paquete (N+1).
/// </summary>
public sealed class CountingCatalogStore : ICatalogStore
{
    private readonly InMemoryCatalogStore _inner = new();

    public int ListGamesCalls { get; private set; }

    public int GetGameCalls { get; private set; }

    public void ResetCounters()
    {
        ListGamesCalls = 0;
        GetGameCalls = 0;
    }

    public Task UpsertGameAsync(Game game, CancellationToken cancellationToken = default) =>
        _inner.UpsertGameAsync(game, cancellationToken);

    public Task<Game?> GetGameAsync(string gameId, CancellationToken cancellationToken = default)
    {
        GetGameCalls++;
        return _inner.GetGameAsync(gameId, cancellationToken);
    }

    public Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken cancellationToken = default)
    {
        ListGamesCalls++;
        return _inner.ListGamesAsync(cancellationToken);
    }

    public Task AddPackageAsync(Package package, CancellationToken cancellationToken = default) =>
        _inner.AddPackageAsync(package, cancellationToken);

    public Task ReplacePackageAsync(Package package, CancellationToken cancellationToken = default) =>
        _inner.ReplacePackageAsync(package, cancellationToken);

    public Task<Package?> GetPackageAsync(string packageId, CancellationToken cancellationToken = default) =>
        _inner.GetPackageAsync(packageId, cancellationToken);

    public Task<IReadOnlyList<Package>> ListLatestPackagesAsync(CancellationToken cancellationToken = default) =>
        _inner.ListLatestPackagesAsync(cancellationToken);

    public Task UpsertSeenClientAsync(SeenClient client, CancellationToken cancellationToken = default) =>
        _inner.UpsertSeenClientAsync(client, cancellationToken);

    public Task<IReadOnlyList<SeenClient>> ListSeenClientsAsync(CancellationToken cancellationToken = default) =>
        _inner.ListSeenClientsAsync(cancellationToken);

    public Task RecordOperationAsync(ServerOperation operation, CancellationToken cancellationToken = default) =>
        _inner.RecordOperationAsync(operation, cancellationToken);

    public Task<IReadOnlyList<ServerOperation>> ListOperationsAsync(CancellationToken cancellationToken = default) =>
        _inner.ListOperationsAsync(cancellationToken);

    public Task<bool> DeleteGameAsync(string gameId, CancellationToken cancellationToken = default) =>
        _inner.DeleteGameAsync(gameId, cancellationToken);
}
