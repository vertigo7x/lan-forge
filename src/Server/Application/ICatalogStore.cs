using LanForge.Server.Domain;

namespace LanForge.Server.Application;

/// <summary>Puerto de persistencia del catálogo (SQLite en producción, in-memory en tests).</summary>
public interface ICatalogStore
{
    Task UpsertGameAsync(Game game, CancellationToken cancellationToken = default);

    Task<Game?> GetGameAsync(string gameId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserta un paquete nuevo. Lanza <see cref="InvalidOperationException"/> si ya existe.</summary>
    Task AddPackageAsync(Package package, CancellationToken cancellationToken = default);

    Task ReplacePackageAsync(Package package, CancellationToken cancellationToken = default);

    Task<Package?> GetPackageAsync(string packageId, CancellationToken cancellationToken = default);

    /// <summary>Paquete activo por juego (MVP: el último publicado de cada juego).</summary>
    Task<IReadOnlyList<Package>> ListLatestPackagesAsync(CancellationToken cancellationToken = default);

    Task UpsertSeenClientAsync(SeenClient client, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SeenClient>> ListSeenClientsAsync(CancellationToken cancellationToken = default);

    Task RecordOperationAsync(ServerOperation operation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServerOperation>> ListOperationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Borra el juego y sus paquetes. Devuelve false si no habia nada que borrar.
    /// </summary>
    Task<bool> DeleteGameAsync(string gameId, CancellationToken cancellationToken = default);
}
