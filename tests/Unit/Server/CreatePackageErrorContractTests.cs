using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Shared.Models;
using LanForge.UnitTests.Server.Fakes;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Reglas críticas del contrato de error: las excepciones de aplicación exponen
/// únicamente datos de negocio, nunca detalle interno de infraestructura.
/// </summary>
public sealed class CreatePackageErrorContractTests
{
    [Fact]
    public void PackageValidationException_Expone_errores_por_campo_sin_stack_trace_en_el_mensaje()
    {
        var errors = new[]
        {
            new ApiFieldError("gameId", "invalid_format", "El identificador del juego no es válido."),
        };

        var exception = new PackageValidationException(errors);

        Assert.Equal("La solicitud no cumple las validaciones del paquete.", exception.Message);
        Assert.Same(errors, exception.Errors);
        Assert.DoesNotContain(nameof(Exception.StackTrace), exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageVersionConflictException_Expone_solo_identificadores_de_negocio()
    {
        var exception = new PackageVersionConflictException("quake3", "1.32");

        Assert.Equal("quake3", exception.GameId);
        Assert.Equal("1.32", exception.Version);
        Assert.Equal("Ya está publicada la versión '1.32' de 'quake3'.", exception.Message);
        Assert.DoesNotContain(nameof(Exception.StackTrace), exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_Un_error_de_persistencia_no_deja_el_paquete_persistido()
    {
        var store = new FailingCatalogStore();
        var useCase = new CreatePackageUseCase(
            store,
            new NoOpContentStore(),
            new StaticServerIdentity());
        var source = Path.Combine(Path.GetTempPath(), "lanforge-error-contract-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "run.exe"), "binary");

        try
        {
            var error = await Assert.ThrowsAsync<IOException>(
                () => useCase.CreateAsync(new CreatePackageRequest
                {
                    GameId = "quake3",
                    Title = "Quake III Arena",
                    Version = "1.32",
                    SourceDirectory = source,
                    EntryPoint = "run.exe",
                }));

            Assert.Equal("persistencia no disponible", error.Message);
            Assert.Null(await store.GetPackageAsync("quake3"));
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public async Task CreateAsync_Un_error_de_persistencia_no_reemplaza_una_version_existente()
    {
        var store = new FailingCatalogStore { FailOnAdd = false, FailOnReplace = true };
        var useCase = new CreatePackageUseCase(
            store,
            new NoOpContentStore(),
            new StaticServerIdentity());
        var source = Path.Combine(Path.GetTempPath(), "lanforge-error-contract-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "run.exe"), "binary");

        try
        {
            await store.AddPackageAsync(new Package
            {
                PackageId = "quake3",
                GameId = "quake3",
                Version = "1.32",
                EntryPoint = "run.exe",
                Arguments = null,
                TotalSizeBytes = 0,
                FileCount = 0,
                CreatedUtc = DateTimeOffset.UtcNow,
                Manifest = null!,
            });

            var error = await Assert.ThrowsAsync<IOException>(
                () => useCase.CreateAsync(new CreatePackageRequest
                {
                    GameId = "quake3",
                    Title = "Quake III Arena",
                    Version = "1.33",
                    SourceDirectory = source,
                    EntryPoint = "run.exe",
                }));

            Assert.Equal("persistencia no disponible", error.Message);
            var persisted = await store.GetPackageAsync("quake3");
            Assert.NotNull(persisted);
            Assert.Equal("1.32", persisted.Version);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    private sealed class FailingCatalogStore : ICatalogStore
    {
        private readonly InMemoryCatalogStore _inner = new();

        public bool FailOnAdd { get; init; } = true;

        public bool FailOnReplace { get; init; }

        public Task AddPackageAsync(Package package, CancellationToken cancellationToken = default)
        {
            if (FailOnAdd)
            {
                throw new IOException("persistencia no disponible");
            }

            return _inner.AddPackageAsync(package, cancellationToken);
        }

        public Task ReplacePackageAsync(Package package, CancellationToken cancellationToken = default)
        {
            if (FailOnReplace)
            {
                throw new IOException("persistencia no disponible");
            }

            return _inner.ReplacePackageAsync(package, cancellationToken);
        }

        public Task<Package?> GetPackageAsync(string packageId, CancellationToken cancellationToken = default) =>
            _inner.GetPackageAsync(packageId, cancellationToken);

        public Task<Game?> GetGameAsync(string gameId, CancellationToken cancellationToken = default) =>
            _inner.GetGameAsync(gameId, cancellationToken);

        public Task UpsertGameAsync(Game game, CancellationToken cancellationToken = default) =>
            _inner.UpsertGameAsync(game, cancellationToken);

        public Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken cancellationToken = default) =>
            _inner.ListGamesAsync(cancellationToken);

        public Task<IReadOnlyList<Package>> ListLatestPackagesAsync(CancellationToken cancellationToken = default) =>
            _inner.ListLatestPackagesAsync(cancellationToken);

        public Task UpsertSeenClientAsync(SeenClient client, CancellationToken cancellationToken = default) =>
            _inner.UpsertSeenClientAsync(client, cancellationToken);

        public Task<IReadOnlyList<SeenClient>> ListSeenClientsAsync(CancellationToken cancellationToken = default) =>
            _inner.ListSeenClientsAsync(cancellationToken);

        public Task RecordOperationAsync(ServerOperation operation, CancellationToken cancellationToken = default) =>
            _inner.RecordOperationAsync(operation, cancellationToken);

        public Task<bool> DeleteGameAsync(string gameId, CancellationToken cancellationToken = default) =>
            _inner.DeleteGameAsync(gameId, cancellationToken);

        public Task<IReadOnlyList<ServerOperation>> ListOperationsAsync(int limit = ICatalogStore.DefaultOperationsLimit, CancellationToken cancellationToken = default) =>
            _inner.ListOperationsAsync(limit, cancellationToken);

        public Task<int> PruneOperationsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) =>
            _inner.PruneOperationsAsync(olderThan, cancellationToken);
    }

    private sealed class NoOpContentStore : IPackageContentStore
    {
        public Task ImportArchiveAsync(string packageId, string sourceDirectory, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Stream?> OpenArchiveAsync(string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task<Stream?> OpenReadAsync(string packageId, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task<bool> DeleteArchiveAsync(string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class StaticServerIdentity : IServerIdentity
    {
        public Shared.Security.KeyPair KeyPair { get; } = Shared.Security.Ed25519.GenerateKeyPair();

        public Shared.Security.Fingerprint Fingerprint => Shared.Security.Fingerprint.FromPublicKey(KeyPair.PublicKey);

        public string ServerId => "server-test";

        public string ServerName => "Servidor de pruebas";
    }
}
