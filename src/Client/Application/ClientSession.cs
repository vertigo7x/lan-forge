using LanForge.Client.Domain;
using LanForge.Shared.Logging;
using LanForge.Shared.Models;

namespace LanForge.Client.Application;

/// <summary>
/// Orquesta el flujo completo del jugador: sincronizar, instalar (descargar → validar → instalar)
/// y lanzar. Es la fachada que consume la UI.
/// </summary>
public sealed class ClientSession
{
    private readonly IServerApi _api;
    private readonly CatalogSyncService _sync;
    private readonly TrustManager _trust;
    private readonly PackageDownloader _downloader;
    private readonly GameInstaller _installer;
    private readonly GameLauncher _launcher;
    private readonly IClientStore _store;
    private readonly IClientLog? _log;
    private readonly string _clientId;
    private readonly string _downloadsRoot;

    public ClientSession(
        IServerApi api,
        CatalogSyncService sync,
        TrustManager trust,
        PackageDownloader downloader,
        GameInstaller installer,
        GameLauncher launcher,
        IClientStore store,
        string downloadsRoot,
        string clientId,
        IClientLog? log = null)
    {
        _api = api;
        _sync = sync;
        _trust = trust;
        _downloader = downloader;
        _installer = installer;
        _launcher = launcher;
        _store = store;
        _downloadsRoot = Path.GetFullPath(downloadsRoot);
        _clientId = clientId;
        _log = log;
    }

    public Task<SyncResult> SyncAsync(bool confirmNewServer, CancellationToken cancellationToken = default) =>
        RunLoggedAsync("catalog.sync", () => _sync.SyncAsync(confirmNewServer, cancellationToken), cancellationToken);

    /// <summary>Flujo completo: manifiesto → firma → descarga con progreso → hashes → instalación confinada.</summary>
    public Task<InstalledGame> InstallAsync(
        string gameId, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
        RunLoggedAsync("game.install", async () =>
        {
            var manifest = await _api.GetManifestAsync(gameId, cancellationToken);

            // 1. Firma y firmante de confianza ANTES de descargar nada.
            var trustedFingerprint = await _trust.GetTrustedFingerprintAsync(cancellationToken);
            PackageValidator.ValidateSignature(manifest, trustedFingerprint);

            // 2. Descarga a staging (zip único reanudable) + extracción + validación de integridad.
            var staging = Path.Combine(_downloadsRoot, manifest.GameId);
            try
            {
                var extractedDirectory = await _downloader.DownloadAsync(manifest, staging, progress, cancellationToken);
                await PackageValidator.ValidateFilesAsync(manifest, extractedDirectory, cancellationToken);

                // 3. Instalación portable confinada (limpia versiones antiguas).
                return await _installer.InstallAsync(manifest, extractedDirectory, cancellationToken);
            }
            finally
            {
                TryDeleteStaging(staging);
            }
        }, cancellationToken);

    public Task PlayAsync(string gameId, CancellationToken cancellationToken = default) =>
        RunLoggedAsync("game.launch", async () =>
        {
            var installed = await _store.GetInstalledAsync(gameId, cancellationToken)
                ?? throw new LanForgeClientException($"El juego '{gameId}' no está instalado.");
            _launcher.Launch(installed);
            return installed;
        }, cancellationToken);

    public Task UninstallAsync(string gameId, CancellationToken cancellationToken = default) =>
        RunLoggedAsync("game.uninstall", () => _installer.UninstallAsync(gameId, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<CatalogEntry>> GetLocalCatalogAsync(CancellationToken cancellationToken = default) =>
        _sync.GetLocalCatalogAsync(cancellationToken);

    public Task<IReadOnlyList<InstalledGame>> GetInstalledAsync(CancellationToken cancellationToken = default) =>
        _store.ListInstalledAsync(cancellationToken);

    public async Task<ServerTrustInfo?> GetTrustAsync(CancellationToken cancellationToken = default) =>
        await _store.GetServerTrustAsync(cancellationToken);

    public Task<byte[]?> DownloadCoverAsync(string fileName, CancellationToken cancellationToken = default) =>
        _api.DownloadCoverAsync(fileName, cancellationToken);

    public Task<byte[]?> DownloadLanPartyLogoAsync(string fileName, CancellationToken cancellationToken = default) =>
        _api.DownloadLanPartyLogoAsync(fileName, cancellationToken);

    private void TryDeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (IOException)
        {
            // Mejor esfuerzo; el staging se reutiliza/reanuda en la próxima instalación.
        }
    }

    private async Task RunLoggedAsync(string operationType, Func<Task> action, CancellationToken cancellationToken) =>
        await RunLoggedAsync<object?>(operationType, async () =>
        {
            await action();
            return null;
        }, cancellationToken);

    private async Task<T> RunLoggedAsync<T>(string operationType, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid().ToString("n");
        var started = DateTimeOffset.UtcNow;
        await RecordAsync(new ClientOperation(operationId, operationType, ClientOperationStatus.Started, started), cancellationToken);
        try
        {
            var result = await action();
            await RecordAsync(new ClientOperation(operationId, operationType, ClientOperationStatus.Completed, started, DateTimeOffset.UtcNow), cancellationToken);
            await WriteLogAsync(operationId, LogSeverity.Info, $"{operationType}.completed", string.Empty, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordAsync(new ClientOperation(operationId, operationType, ClientOperationStatus.Failed, started, DateTimeOffset.UtcNow, "cancelado"), CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await RecordAsync(new ClientOperation(operationId, operationType, ClientOperationStatus.Failed, started, DateTimeOffset.UtcNow, ex.Message), CancellationToken.None);
            await WriteLogAsync(operationId, LogSeverity.Error, $"{operationType}.failed", ex.Message, CancellationToken.None);
            throw;
        }
    }

    private Task RecordAsync(ClientOperation operation, CancellationToken cancellationToken) =>
        _store.RecordOperationAsync(operation, cancellationToken);

    private async Task WriteLogAsync(string operationId, LogSeverity severity, string eventName, string message, CancellationToken cancellationToken)
    {
        if (_log is not null)
        {
            await _log.LogAsync(new LogEntry(DateTimeOffset.UtcNow, _clientId, operationId, severity, eventName, message), cancellationToken);
        }
    }
}
