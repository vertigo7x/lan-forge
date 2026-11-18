using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.Infrastructure;
using LanForge.Shared.Manifest;
using LanForge.Shared.Security;
using LanForge.UnitTests.Server.Fakes;

namespace LanForge.UnitTests.Server;

public sealed class CatalogServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private CatalogService CreateService(ICatalogStore? store = null, TimeProvider? timeProvider = null)
    {
        store ??= new InMemoryCatalogStore();
        var content = new FilePackageContentStore(Path.Combine(_temp.Path, "packages"));
        var identity = new FileServerIdentityStore(Path.Combine(_temp.Path, "identity"));
        return new CatalogService(store, content, identity, timeProvider: timeProvider);
    }

    [Fact]
    public async Task Publish_CreatesValidSignedManifest_WithServerFingerprint()
    {
        var service = CreateService();
        var source = _temp.CreateSourceDirectory(
            ("run.exe", "binary-fake"),
            ("data/level1.dat", "level-data"));

        var entry = await service.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");

        Assert.Equal("quake3", entry.PackageId);
        Assert.Equal(2, entry.FileCount);
        Assert.Equal("level-data".Length + "binary-fake".Length, entry.TotalSizeBytes);

        var manifest = await service.GetManifestAsync("quake3");
        Assert.NotNull(manifest);
        Assert.True(ManifestVerifier.Verify(manifest));
        Assert.Equal("Quake III Arena", manifest.GameTitle);
        Assert.Equal("run.exe", manifest.EntryPoint);
        Assert.Equal(2, manifest.Files.Count);
    }

    [Fact]
    public void Publish_IdentityIsStableAcrossInstances()
    {
        var source = _temp.CreateSourceDirectory(("a.exe", "a"));
        var identityDir = Path.Combine(_temp.Path, "identity");
        var first = new FileServerIdentityStore(identityDir);
        var second = new FileServerIdentityStore(identityDir);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.ServerId, second.ServerId);
    }

    [Fact]
    public async Task Publish_EmptyDirectory_Throws()
    {
        var service = CreateService();
        var empty = Path.Combine(_temp.Path, "empty");
        Directory.CreateDirectory(empty);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PublishPackageAsync("game", "Game", "1.0", empty, "game.exe"));
    }

    [Fact]
    public async Task Publish_MissingDirectory_Throws()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => service.PublishPackageAsync("game", "Game", "1.0", Path.Combine(_temp.Path, "nope"), "game.exe"));
    }

    [Fact]
    public async Task Publish_DuplicateVersion_Throws()
    {
        var service = CreateService();
        var v1 = _temp.CreateSourceDirectory(("a.exe", "a"));

        await service.PublishPackageAsync("game", "Game", "1.0", v1, "a.exe");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PublishPackageAsync("game", "Game", "1.0", v1, "a.exe"));
    }

    [Fact]
    public async Task Publish_NewerVersion_ReplacesArchiveAndMetadata()
    {
        var service = CreateService();
        var v1 = _temp.CreateSourceDirectory(("old.txt", "v1"));
        var v2 = _temp.CreateSourceDirectory(("new.txt", "v2"));

        await service.PublishPackageAsync("game", "Game", "1.0", v1, "old.txt");
        await service.PublishPackageAsync("game", "Game", "1.1", v2, "new.txt");

        Assert.True(File.Exists(Path.Combine(_temp.Path, "packages", "game.zip")));
        Assert.Null(await service.OpenDownloadAsync("game", "old.txt"));
        var file = await service.OpenDownloadAsync("game", "new.txt");
        Assert.NotNull(file);
        using var reader = new StreamReader(file.Stream);
        Assert.Equal("v2", await reader.ReadToEndAsync());

        var archive = await service.OpenArchiveDownloadAsync("game");
        Assert.NotNull(archive);

        var manifest = await service.GetManifestAsync("game");
        Assert.NotNull(manifest);
        Assert.Equal("1.1", manifest.Version);
        Assert.Single(manifest.Files);
        Assert.Equal("new.txt", manifest.Files[0].Path);
    }

    [Fact]
    public async Task OpenDownload_DeclaredFile_ReturnsContentAndEntry()
    {
        var service = CreateService();
        var source = _temp.CreateSourceDirectory(("bin/run.exe", "payload"));

        await service.PublishPackageAsync("game", "Game", "1.0", source, "bin/run.exe");

        var file = await service.OpenDownloadAsync("game", "bin/run.exe");
        Assert.NotNull(file);
        Assert.Equal("bin/run.exe", file.Entry.Path);
        using var reader = new StreamReader(file.Stream);
        Assert.Equal("payload", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task OpenDownload_UndeclaredOrUnknown_ReturnsNull()
    {
        var service = CreateService();
        var source = _temp.CreateSourceDirectory(("a.exe", "a"));
        await service.PublishPackageAsync("game", "Game", "1.0", source, "a.exe");

        Assert.Null(await service.OpenDownloadAsync("game", "other.exe"));
        Assert.Null(await service.OpenDownloadAsync("unknown", "a.exe"));
    }

    [Theory]
    [InlineData("../identity/identity.json")]
    [InlineData("..%2fidentity.json")]
    [InlineData("a/../../escape.txt")]
    public async Task OpenDownload_PathTraversal_ReturnsNull(string maliciousPath)
    {
        var service = CreateService();
        var source = _temp.CreateSourceDirectory(("a/nested.txt", "safe"));
        await service.PublishPackageAsync("game", "Game", "1.0", source, "a/nested.txt");

        Assert.Null(await service.OpenDownloadAsync("game", maliciousPath));
    }

    [Fact]
    public async Task AddOrUpdateGame_UpsertsWithoutLosingDescription()
    {
        var service = CreateService();

        await service.AddOrUpdateGameAsync("game", "Game", "descripción");
        var updated = await service.AddOrUpdateGameAsync("game", "Game Remastered");

        Assert.Equal("Game Remastered", updated.Title);
        Assert.Equal("descripción", updated.Description);
    }

    [Fact]
    public async Task Publish_ConservaDescripcionYPortadaDelJuego()
    {
        var store = new InMemoryCatalogStore();
        var service = CreateService(store);

        // El organizador guarda la ficha del juego (descripcion y portada) antes de publicar.
        await service.AddOrUpdateGameAsync("game", "Game", "descripción del juego");
        var covers = new FileCoverStore(Path.Combine(_temp.Path, "covers"));
        var image = Path.Combine(_temp.Path, "cover.png");
        await File.WriteAllBytesAsync(image, [137, 80, 78, 71, 13, 10, 26, 10]);
        await service.SetCoverAsync("game", image, covers);

        var source = _temp.CreateSourceDirectory(("run.exe", "bin"));
        await service.PublishPackageAsync("game", "Game", "1.0", source, "run.exe");

        var game = await store.GetGameAsync("game");
        Assert.NotNull(game);
        Assert.Equal("descripción del juego", game!.Description);
        Assert.False(string.IsNullOrWhiteSpace(game.CoverFile));

        // Y el catalogo sigue sirviendo la portada.
        var entry = Assert.Single(await service.ListCatalogAsync());
        Assert.Equal(game.CoverFile, entry.CoverPath);
    }

    [Fact]
    public async Task Publish_RepublicarActualizaElTituloDelCatalogo()
    {
        var store = new InMemoryCatalogStore();
        var service = CreateService(store);

        var v1 = _temp.CreateSourceDirectory(("a.txt", "v1"));
        var v2 = _temp.CreateSourceDirectory(("b.txt", "v2"));

        await service.PublishPackageAsync("game", "Titulo Antiguo", "1.0", v1, "a.txt");
        await service.PublishPackageAsync("game", "Titulo Nuevo", "1.1", v2, "b.txt");

        // El manifiesto firmado y el catalogo deben llamarse igual.
        var manifest = await service.GetManifestAsync("game");
        Assert.NotNull(manifest);
        var entry = Assert.Single(await service.ListCatalogAsync());
        Assert.Equal(manifest!.GameTitle, entry.Title);
        Assert.Equal("Titulo Nuevo", entry.Title);
    }

    [Fact]
    public async Task Publish_NuevoJuego_CreaLaFilaDelCatalogo()
    {
        var store = new InMemoryCatalogStore();
        var service = CreateService(store);
        var source = _temp.CreateSourceDirectory(("run.exe", "bin"));

        await service.PublishPackageAsync("game", "Game", "1.0", source, "run.exe");

        var game = await store.GetGameAsync("game");
        Assert.NotNull(game);
        Assert.Equal("Game", game!.Title);
        Assert.Null(game.Description);
        Assert.Null(game.CoverFile);
    }

    [Fact]
    public async Task ListCatalog_ReturnsPublishedPackages()
    {
        var service = CreateService();
        var sourceA = _temp.CreateSourceDirectory(("a.exe", "aa"));
        var sourceB = _temp.CreateSourceDirectory(("b.exe", "bbb"));

        await service.PublishPackageAsync("alpha", "Alpha", "1.0", sourceA, "a.exe");
        await service.PublishPackageAsync("beta", "Beta", "2.0", sourceB, "b.exe");

        var entries = await service.ListCatalogAsync();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.GameId == "alpha" && e.TotalSizeBytes == 2);
        Assert.Contains(entries, e => e.GameId == "beta" && e.Version == "2.0");
    }

    [Fact]
    public async Task ListCatalog_LeeLosJuegosUnaSolaVez_NoHaceNMasUna()
    {
        // El endpoint de catalogo lo llaman todos los clientes en cada sincronizacion:
        // leer los juegos una vez por paquete (N+1) multiplicaba las consultas por el
        // numero de juegos publicados.
        var store = new CountingCatalogStore();
        var service = CreateService(store);
        var sourceA = _temp.CreateSourceDirectory(("a.exe", "aa"));
        var sourceB = _temp.CreateSourceDirectory(("b.exe", "bbb"));
        var sourceC = _temp.CreateSourceDirectory(("c.exe", "cccc"));

        await service.PublishPackageAsync("alpha", "Alpha", "1.0", sourceA, "a.exe");
        await service.PublishPackageAsync("beta", "Beta", "2.0", sourceB, "b.exe");
        await service.PublishPackageAsync("gamma", "Gamma", "3.0", sourceC, "c.exe");

        store.ResetCounters();
        var entries = await service.ListCatalogAsync();

        Assert.Equal(3, entries.Count);
        Assert.Equal(1, store.ListGamesCalls);
        Assert.Equal(0, store.GetGameCalls);
    }

    [Fact]
    public async Task RecordOperation_PodaLasOperacionesMasAntiguasQueLaRetencion()
    {
        // La tabla de operaciones recibe una fila por cada sincronizacion y descarga.
        // Sin poda, creceria durante toda la vida del servidor.
        var store = new InMemoryCatalogStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(store, timeProvider: clock);

        await service.RecordOperationAsync(NewOperation("vieja", clock.GetUtcNow().AddDays(-30)));
        await service.RecordOperationAsync(NewOperation("reciente", clock.GetUtcNow().AddDays(-1)));

        var remaining = await store.ListOperationsAsync();
        var loaded = Assert.Single(remaining);
        Assert.Equal("reciente", loaded.OperationId);
    }

    [Fact]
    public async Task RecordOperation_NoPodaLoQueEstaDentroDeLaRetencion()
    {
        var store = new InMemoryCatalogStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(store, timeProvider: clock);

        await service.RecordOperationAsync(NewOperation("a", clock.GetUtcNow().AddDays(-6)));
        await service.RecordOperationAsync(NewOperation("b", clock.GetUtcNow()));

        Assert.Equal(2, (await store.ListOperationsAsync()).Count);
    }

    [Fact]
    public async Task RecordOperation_PodaContraElStoreReal_NoSoloContraElFake()
    {
        // La poda por antiguedad se ejecuta contra el store de produccion (SQLite), no
        // solo contra el fake en memoria: si el DELETE no llegara al store, la tabla de
        // operaciones seguiria creciendo durante toda la vida del servidor.
        using var store = new SqliteCatalogStore(Path.Combine(_temp.Path, "prune.db"));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(store, timeProvider: clock);

        await service.RecordOperationAsync(NewOperation("vieja", clock.GetUtcNow().AddDays(-30)));
        await service.RecordOperationAsync(NewOperation("reciente", clock.GetUtcNow().AddDays(-1)));

        var remaining = await store.ListOperationsAsync();
        var loaded = Assert.Single(remaining);
        Assert.Equal("reciente", loaded.OperationId);
    }

    [Fact]
    public async Task RecordOperation_NoPodaDentroDeLaVentanaDeUnMinuto()
    {
        // Podar en cada registro significaria un DELETE por cada descarga. El servicio
        // poda como mucho una vez por minuto: dentro de esa ventana, una operacion que
        // caduque se queda hasta la siguiente poda.
        var store = new InMemoryCatalogStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(store, timeProvider: clock);

        // La primera llamada poda (el reloj aun no haAdvancesado) y fija la ventana.
        await service.RecordOperationAsync(NewOperation("primera", clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromSeconds(30));

        // Esta operacion caduca dentro de la ventana: no debe podarse todavia.
        await service.RecordOperationAsync(NewOperation("caducada", clock.GetUtcNow().AddDays(-30)));
        Assert.Equal(2, (await store.ListOperationsAsync()).Count);

        // Pasado el minuto, la siguiente llamada poda.
        clock.Advance(TimeSpan.FromSeconds(31));
        await service.RecordOperationAsync(NewOperation("disparadora", clock.GetUtcNow()));

        var remaining = await store.ListOperationsAsync();
        Assert.DoesNotContain(remaining, o => o.OperationId == "caducada");
        Assert.Equal(2, remaining.Count);
    }

    [Fact]
    public async Task RecordOperation_PodaDeNuevoPasadoElMinuto()
    {
        var store = new InMemoryCatalogStore();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero));
        var service = CreateService(store, timeProvider: clock);

        await service.RecordOperationAsync(NewOperation("a", clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(2));
        await service.RecordOperationAsync(NewOperation("b", clock.GetUtcNow().AddDays(-30)));

        var remaining = await store.ListOperationsAsync();
        Assert.DoesNotContain(remaining, o => o.OperationId == "b");
    }

    private static ServerOperation NewOperation(string id, DateTimeOffset startedUtc) => new(
        id, "client-1", "catalog.sync", OperationStatus.Completed, startedUtc, startedUtc, null);

    [Fact]
    public async Task SetCover_CatalogServesCoverAndRejectsTraversal()
    {
        var service = CreateService();
        var source = _temp.CreateSourceDirectory(("run.exe", "bin"));
        await service.PublishPackageAsync("game", "Game", "1.0", source, "run.exe");

        var image = Path.Combine(_temp.Path, "cover.png");
        await File.WriteAllBytesAsync(image, [137, 80, 78, 71, 13, 10, 26, 10]); // cabecera PNG
        var store = new FileCoverStore(Path.Combine(_temp.Path, "covers"));
        var fileName = await service.SetCoverAsync("game", image, store);

        var entry = Assert.Single(await service.ListCatalogAsync());
        Assert.Equal(fileName, entry.CoverPath);

        var coverStream = await store.OpenReadAsync(fileName);
        Assert.NotNull(coverStream);
        Assert.Null(await store.OpenReadAsync("../identity.json"));
        Assert.Null(await store.OpenReadAsync("a/b.png"));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetCoverAsync(
            "game", Path.Combine(_temp.Path, "notes.txt"), store));
    }
}
