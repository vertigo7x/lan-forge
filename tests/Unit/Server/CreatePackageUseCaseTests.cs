using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.Infrastructure;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using LanForge.UnitTests.Server.Fakes;

namespace LanForge.UnitTests.Server;

public sealed class CreatePackageUseCaseTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private CreatePackageUseCase CreateUseCase(InMemoryCatalogStore store) =>
        new(
            store,
            new FilePackageContentStore(Path.Combine(_temp.Path, "packages")),
            new FileServerIdentityStore(Path.Combine(_temp.Path, "identity")));

    private CreatePackageUseCase CreateUseCaseWithSpyStore(InMemoryCatalogStore store, RecordingContentStore content) =>
        new(
            store,
            content,
            new FileServerIdentityStore(Path.Combine(_temp.Path, "identity")));

    private static CreatePackageRequest ValidRequest(string sourceDirectory) => new()
    {
        GameId = "quake3",
        Title = "Quake III Arena",
        Version = "1.32",
        SourceDirectory = sourceDirectory,
        EntryPoint = "run.exe",
    };

    [Fact]
    public async Task CreateAsync_Una_entrada_valida_persiste_y_devuelve_el_paquete_creado()
    {
        var store = new InMemoryCatalogStore();
        var useCase = CreateUseCase(store);
        var source = _temp.CreateSourceDirectory(
            ("run.exe", "binary"),
            ("data/level.dat", "level"));

        var result = await useCase.CreateAsync(ValidRequest(source));

        Assert.Equal("quake3", result.PackageId);
        Assert.Equal("quake3", result.GameId);
        Assert.Equal("Quake III Arena", result.Title);
        Assert.Equal("1.32", result.Version);
        Assert.Equal("run.exe", result.EntryPoint);
        Assert.Equal("binary".Length + "level".Length, result.TotalSizeBytes);
        Assert.Equal(2, result.FileCount);
        Assert.Null(result.PreviousVersion);

        var persisted = await store.GetPackageAsync("quake3");
        Assert.NotNull(persisted);
        Assert.Equal("1.32", persisted.Version);
        Assert.True(ManifestVerifier.Verify(persisted.Manifest));
    }

    [Fact]
    public async Task CreateAsync_Una_version_distinta_reemplaza_la_anterior()
    {
        var store = new InMemoryCatalogStore();
        var useCase = CreateUseCase(store);
        var source = _temp.CreateSourceDirectory(("run.exe", "binary"));

        await useCase.CreateAsync(ValidRequest(source));
        var result = await useCase.CreateAsync(ValidRequest(source) with { Version = "1.33" });

        Assert.Equal("1.33", result.Version);
        Assert.Equal("1.32", result.PreviousVersion);
        var persisted = await store.GetPackageAsync("quake3");
        Assert.NotNull(persisted);
        Assert.Equal("1.33", persisted.Version);
    }

    [Fact]
    public async Task CreateAsync_Una_version_ya_publicada_lanza_el_conflicto_especifico()
    {
        var store = new InMemoryCatalogStore();
        var useCase = CreateUseCase(store);
        var source = _temp.CreateSourceDirectory(("run.exe", "binary"));

        await useCase.CreateAsync(ValidRequest(source));
        var error = await Assert.ThrowsAsync<PackageVersionConflictException>(
            () => useCase.CreateAsync(ValidRequest(source)));

        Assert.Equal("quake3", error.GameId);
        Assert.Equal("1.32", error.Version);
    }

    [Fact]
    public async Task CreateAsync_Una_entrada_invalida_no_persiste_nada()
    {
        var store = new InMemoryCatalogStore();
        var useCase = CreateUseCase(store);
        var source = _temp.CreateSourceDirectory(("run.exe", "binary"));

        var error = await Assert.ThrowsAsync<PackageValidationException>(
            () => useCase.CreateAsync(ValidRequest(source) with { GameId = "quake 3" }));

        Assert.Contains(error.Errors, e => e.Field == "gameId" && e.Code == "invalid_format");
        Assert.Null(await store.GetPackageAsync("quake3"));
        Assert.False(File.Exists(Path.Combine(_temp.Path, "packages", "quake3.zip")));
    }

    [Fact]
    public async Task CreateAsync_El_directorio_inexistente_se_traduce_a_una_excepcion_de_aplicacion()
    {
        var store = new InMemoryCatalogStore();
        var useCase = CreateUseCase(store);

        var error = await Assert.ThrowsAsync<PackageSourceNotFoundException>(
            () => useCase.CreateAsync(ValidRequest(Path.Combine(_temp.Path, "no-existe"))));

        Assert.Contains("no-existe", error.SourceDirectory);
        Assert.Null(await store.GetPackageAsync("quake3"));
    }

    [Fact]
    public async Task CreateAsync_El_directorio_vacio_se_traduce_a_una_excepcion_de_aplicacion()
    {
        var store = new InMemoryCatalogStore();
        var useCase = CreateUseCase(store);
        var empty = Path.Combine(_temp.Path, "empty");
        Directory.CreateDirectory(empty);

        var error = await Assert.ThrowsAsync<EmptySourceDirectoryException>(
            () => useCase.CreateAsync(ValidRequest(empty)));

        Assert.Equal(empty, error.SourceDirectory);
    }

    [Fact]
    public async Task CreateAsync_Un_punto_de_entrada_inexistente_no_deja_paquete_persistido()
    {
        var store = new InMemoryCatalogStore();
        var useCase = CreateUseCase(store);
        var source = _temp.CreateSourceDirectory(("run.exe", "binary"));

        var error = await Assert.ThrowsAsync<PackageEntryPointNotFoundException>(
            () => useCase.CreateAsync(ValidRequest(source) with { EntryPoint = "missing.exe" }));

        Assert.Equal("entryPoint", error.Field);
        Assert.Null(await store.GetPackageAsync("quake3"));
        Assert.False(File.Exists(Path.Combine(_temp.Path, "packages", "quake3.zip")));
    }

    [Fact]
    public async Task CreateAsync_Una_operacion_otorgada_lanza_el_conflicto_sin_persistir_ni_sobrescribir_el_contenido()
    {
        var store = new InMemoryCatalogStore();
        var content = new RecordingContentStore();
        var useCase = CreateUseCaseWithSpyStore(store, content);
        var source = _temp.CreateSourceDirectory(("run.exe", "binary"));

        await useCase.CreateAsync(ValidRequest(source));
        var error = await Assert.ThrowsAsync<PackageVersionConflictException>(
            () => useCase.CreateAsync(ValidRequest(source)));

        Assert.Equal("quake3", error.GameId);
        Assert.Equal(1, content.ImportCount);
        var persisted = await store.GetPackageAsync("quake3");
        Assert.NotNull(persisted);
        Assert.Equal("1.32", persisted.Version);
    }

    private sealed class RecordingContentStore : IPackageContentStore
    {
        public int ImportCount { get; private set; }

        public Task ImportArchiveAsync(string packageId, string sourceDirectory, CancellationToken cancellationToken = default)
        {
            ImportCount++;
            return Task.CompletedTask;
        }

        public Task<Stream?> OpenArchiveAsync(string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task<Stream?> OpenReadAsync(string packageId, string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task<bool> DeleteArchiveAsync(string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
