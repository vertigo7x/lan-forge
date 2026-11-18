using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.Http;
using LanForge.Shared.Logging;
using LanForge.UnitTests.Server.Fakes;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Estado final de una descarga segun como termino. El cierre del stream no dice si la
/// transferencia llego entera o se corto: eso lo dice la peticion. Si el cliente se va a
/// mitad, la peticion queda cancelada y la operacion se registra como Failed, no como
/// Completed.
///
/// Estas pruebas cubren la clase aislada: la integracion (HTTP de verdad) esta en
/// <c>tests/Integration/DownloadCompletionTests.cs</c>.
/// </summary>
public sealed class DownloadCompletionReporterTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnaDescargaCompleta_SeRegistraComoCompleted()
    {
        var store = new InMemoryCatalogStore();
        var log = new RecordingServerLog();
        var reporter = new DownloadCompletionReporter(store, log);

        reporter.Report("op-1", "client-1", "quake3", Started, aborted: false);

        await WaitForOperationsAsync(store);
        var operation = Assert.Single(await store.ListOperationsAsync());
        Assert.Equal(OperationStatus.Completed, operation.Status);
        Assert.Equal("package.download", operation.Type);
        Assert.Equal("quake3", operation.Detail);
        Assert.Equal("op-1", operation.OperationId);
    }

    [Fact]
    public async Task UnaDescargaCortada_SeRegistraComoFailed()
    {
        var store = new InMemoryCatalogStore();
        var log = new RecordingServerLog();
        var reporter = new DownloadCompletionReporter(store, log);

        reporter.Report("op-1", "client-1", "quake3", Started, aborted: true);

        await WaitForOperationsAsync(store);
        var operation = Assert.Single(await store.ListOperationsAsync());
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Contains("cancelada", operation.Detail!);
    }

    [Fact]
    public async Task LaDescargaCortada_EscribeElEventoDeLogAborted()
    {
        var store = new InMemoryCatalogStore();
        var log = new RecordingServerLog();
        var reporter = new DownloadCompletionReporter(store, log);

        reporter.Report("op-1", "client-1", "quake3", Started, aborted: true);

        await WaitForLogAsync(log);
        var entry = Assert.Single(log.Entries);
        Assert.Equal("download.aborted", entry.Event);
        Assert.Equal(LogSeverity.Warning, entry.Severity);
        Assert.Equal("op-1", entry.OperationId);
    }

    [Fact]
    public async Task LaDescargaCompleta_EscribeElEventoDeLogCompleted()
    {
        var store = new InMemoryCatalogStore();
        var log = new RecordingServerLog();
        var reporter = new DownloadCompletionReporter(store, log);

        reporter.Report("op-1", "client-1", "quake3", Started, aborted: false);

        await WaitForLogAsync(log);
        var entry = Assert.Single(log.Entries);
        Assert.Equal("download.completed", entry.Event);
        Assert.Equal(LogSeverity.Info, entry.Severity);
    }

    [Fact]
    public async Task ElCierreRepetido_SoloSeReportaUnaVez()
    {
        // El stream puede cerrarse mas de una vez (Dispose y DisposeAsync). Si el reporte
        // no fuera idempotente, la descarga apareceria dos veces en la vista de operaciones.
        var store = new InMemoryCatalogStore();
        var log = new RecordingServerLog();
        var reporter = new DownloadCompletionReporter(store, log);

        reporter.Report("op-1", "client-1", "quake3", Started, aborted: false);
        reporter.Report("op-1", "client-1", "quake3", Started, aborted: false);
        reporter.Report("op-1", "client-1", "quake3", Started, aborted: false);

        await WaitForLogAsync(log);
        // Que el log ya tenga una linea no basta: el reporte es asincrono, asi que se espera
        // a que se estabilice el numero de entradas.
        await Task.Delay(150);

        Assert.Single(await store.ListOperationsAsync());
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task UnFalloAlRegistrar_NoSePropagaYSeRegistraComoError()
    {
        // El cierre de una descarga nunca debe tumbar la peticion que ya esta respondiendo,
        // pero un fallo aqui tampoco puede perderse en silencio.
        var store = new FailingCatalogStore();
        var log = new RecordingServerLog();
        var reporter = new DownloadCompletionReporter(store, log);

        reporter.Report("op-1", "client-1", "quake3", Started, aborted: false);

        await WaitForLogAsync(log);
        var entry = Assert.Single(log.Entries);
        Assert.Equal("download.close.failed", entry.Event);
        Assert.Equal(LogSeverity.Error, entry.Severity);
    }

    [Fact]
    public async Task UnFalloEnElLog_NoPropagaLaExcepcion()
    {
        var store = new InMemoryCatalogStore();
        var log = new RecordingServerLog { Fails = true };
        var reporter = new DownloadCompletionReporter(store, log);

        // Report no lanza: la peticion que responde la descarga no debe caerse por esto.
        reporter.Report("op-1", "client-1", "quake3", Started, aborted: false);

        await Task.Delay(150);
        Assert.Single(await store.ListOperationsAsync());
    }

    /// <summary>Espera a que el reporte async se haya escrito en el store.</summary>
    private static async Task WaitForOperationsAsync(InMemoryCatalogStore store)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if ((await store.ListOperationsAsync()).Count > 0)
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Espera a que el reporte async se haya escrito en el log.</summary>
    private static async Task WaitForLogAsync(RecordingServerLog log)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (log.Entries.Count > 0)
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    /// <summary>Store que falla al registrar operaciones, para probar el camino de error.</summary>
    private sealed class FailingCatalogStore : ICatalogStore
    {
        public Task RecordOperationAsync(ServerOperation operation, CancellationToken cancellationToken = default) =>
            throw new IOException("La base de datos no se puede escribir.");

        public Task UpsertGameAsync(Game game, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Game?> GetGameAsync(string gameId, CancellationToken cancellationToken = default) => Task.FromResult<Game?>(null);

        public Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Game>>([]);

        public Task AddPackageAsync(Package package, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReplacePackageAsync(Package package, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Package?> GetPackageAsync(string packageId, CancellationToken cancellationToken = default) => Task.FromResult<Package?>(null);

        public Task<IReadOnlyList<Package>> ListLatestPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Package>>([]);

        public Task UpsertSeenClientAsync(SeenClient client, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SeenClient>> ListSeenClientsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SeenClient>>([]);

        public Task<IReadOnlyList<ServerOperation>> ListOperationsAsync(int limit = ICatalogStore.DefaultOperationsLimit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServerOperation>>([]);

        public Task<int> PruneOperationsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<bool> DeleteGameAsync(string gameId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
