using System.Diagnostics;
using LanForge.Server.Application;
using LanForge.Server.Infrastructure;
using LanForge.UnitTests.Server.Fakes;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Publicacion sin bloquear el hilo que llama. El organizador publica desde el hilo de
/// UI: comprimir y hashear un juego de varios GB congelaba la ventana sin progreso ni
/// cancelacion. Estas pruebas comprueban que la operacion larga sale del hilo llamador.
/// </summary>
public sealed class AsyncPublishTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task ImportArchive_NoBloqueaElHiloQueLlama()
    {
        var content = new FilePackageContentStore(Path.Combine(_temp.Path, "packages"));
        var source = _temp.CreateSourceDirectory(("run.exe", "binario"), ("data/level.dat", "datos"));

        var import = content.ImportArchiveAsync("quake3", source);

        // La llamada devuelve sin esperar: si la compresion se ejecutara en el hilo que
        // llama, la tarea ya estaria completada al volver de la llamada.
        Assert.False(import.IsCompleted);

        await import;

        Assert.True(File.Exists(Path.Combine(_temp.Path, "packages", "quake3.zip")));
    }

    [Fact]
    public async Task ImportArchive_RespetaLaCancelacion()
    {
        var content = new FilePackageContentStore(Path.Combine(_temp.Path, "packages"));
        var source = _temp.CreateSourceDirectory(("run.exe", "binario"));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => content.ImportArchiveAsync("quake3", source, cancellation.Token));
    }

    [Fact]
    public async Task ImportArchive_NoDejaElTmpCuandoLaCompresionFalla()
    {
        var content = new FilePackageContentStore(Path.Combine(_temp.Path, "packages"));
        var source = Path.Combine(_temp.Path, "no-existe");

        await Assert.ThrowsAnyAsync<Exception>(() => content.ImportArchiveAsync("quake3", source));

        // Ni el .zip ni el .tmp a medias quedan en la raiz de contenido.
        Assert.Empty(Directory.GetFiles(Path.Combine(_temp.Path, "packages")));
    }

    [Fact]
    public async Task ImportArchive_PublicaElZipCorrecto()
    {
        var content = new FilePackageContentStore(Path.Combine(_temp.Path, "packages"));
        var source = _temp.CreateSourceDirectory(("run.exe", "binario"));

        await content.ImportArchiveAsync("quake3", source);

        var archive = Path.Combine(_temp.Path, "packages", "quake3.zip");
        Assert.True(File.Exists(archive));
        using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
        Assert.Single(zip.Entries);
        Assert.Equal("run.exe", zip.Entries[0].FullName);
    }

    [Fact]
    public async Task PublishPackage_NoBloqueaElHiloQueLlama()
    {
        var store = new InMemoryCatalogStore();
        var content = new FilePackageContentStore(Path.Combine(_temp.Path, "packages"));
        var identity = new FileServerIdentityStore(Path.Combine(_temp.Path, "identity"));
        var service = new CatalogService(store, content, identity);
        var source = _temp.CreateSourceDirectory(("run.exe", "binario"), ("data/level.dat", "datos"));

        var callerThread = Environment.CurrentManagedThreadId;
        var publish = service.PublishPackageAsync("quake3", "Quake III", "1.32", source, "run.exe");

        // La llamada devuelve el control al hilo que llama antes de terminar la compresion
        // y el hashing: si fuera sincrono, la tarea ya estaria completada al volver.
        Assert.False(publish.IsCompleted);
        Assert.Equal(callerThread, Environment.CurrentManagedThreadId);

        var entry = await publish;
        Assert.Equal("quake3", entry.GameId);
        Assert.Equal(2, entry.FileCount);
    }

    [Fact]
    public async Task PublishPackage_HonraLaCancelacionDuranteElHashing()
    {
        var store = new InMemoryCatalogStore();
        var content = new FilePackageContentStore(Path.Combine(_temp.Path, "packages"));
        var identity = new FileServerIdentityStore(Path.Combine(_temp.Path, "identity"));
        var service = new CatalogService(store, content, identity);
        var source = _temp.CreateSourceDirectory(("run.exe", "binario"));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.PublishPackageAsync("quake3", "Quake III", "1.32", source, "run.exe", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task PublishPackage_PublicaEnPocosSegundosParaUnJuegoPequeno()
    {
        // Guarda de red: la operacion completa (hashear + comprimir + firmar + guardar)
        // debe seguir siendo rapida para un juego pequeno, sin que el paso a Task.Run
        // haya introducido una espera.
        var store = new InMemoryCatalogStore();
        var content = new FilePackageContentStore(Path.Combine(_temp.Path, "packages"));
        var identity = new FileServerIdentityStore(Path.Combine(_temp.Path, "identity"));
        var service = new CatalogService(store, content, identity);
        var source = _temp.CreateSourceDirectory(("run.exe", "binario"));

        var stopwatch = Stopwatch.StartNew();
        await service.PublishPackageAsync("quake3", "Quake III", "1.32", source, "run.exe");
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"La publicacion tardo {stopwatch.Elapsed}.");
    }
}
