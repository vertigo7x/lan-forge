using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Shared.Logging;

namespace LanForge.Server.Http;

/// <summary>
/// Cierra la operacion de una descarga. El cierre del stream no dice si la transferencia
/// llego entera o se corto: eso lo dice la peticion. Si el cliente se va a mitad, la
/// peticion queda cancelada y la operacion se registra como Failed, no como Completed.
/// </summary>
/// <remarks>
/// La senal es <see cref="HttpContext.RequestAborted"/>: Kestrel cancela ese token cuando el
/// cliente corta la conexion, y el token ya esta cancelado cuando el stream se cierra. Antes,
/// toda descarga se registraba como Completed, tambien las cortadas: las metricas y el
/// diagnostico mintian justo en el caso que hace falta depurar.
/// </remarks>
public sealed class DownloadCompletionReporter
{
    private readonly ICatalogStore _store;
    private readonly IServerLog _log;
    private int _reported;

    public DownloadCompletionReporter(ICatalogStore store, IServerLog log)
    {
        _store = store;
        _log = log;
    }

    /// <summary>
    /// Registra el final de la descarga. <paramref name="aborted"/> indica que el cliente
    /// corto la transferencia antes de recibir el paquete entero. Solo tiene efecto la
    /// primera vez: el stream puede cerrarse mas de una vez (Dispose y DisposeAsync).
    /// </summary>
    public void Report(
        string operationId,
        string clientId,
        string detail,
        DateTimeOffset startedUtc,
        bool aborted)
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0)
        {
            return;
        }

        var status = aborted ? OperationStatus.Failed : OperationStatus.Completed;
        var severity = aborted ? LogSeverity.Warning : LogSeverity.Info;
        var eventName = aborted ? "download.aborted" : "download.completed";
        var message = aborted ? $"{detail} (cancelada por el cliente)" : detail;

        _ = WriteAsync(
            new ServerOperation(
                operationId, clientId, "package.download", status,
                startedUtc, DateTimeOffset.UtcNow, message),
            new LogEntry(
                DateTimeOffset.UtcNow, clientId, operationId,
                severity, eventName, message));
    }

    /// <summary>
    /// Escribe la operacion y la linea de log. El cierre de una descarga nunca debe tumbar la
    /// peticion que ya esta respondiendo, pero un fallo aqui tampoco puede perderse en
    /// silencio: se registra como error del servidor.
    /// </summary>
    private async Task WriteAsync(ServerOperation operation, LogEntry entry)
    {
        try
        {
            await _store.RecordOperationAsync(operation, CancellationToken.None);
            await _log.LogAsync(entry, CancellationToken.None);
        }
        catch (Exception ex)
        {
            try
            {
                await _log.LogAsync(new LogEntry(
                    DateTimeOffset.UtcNow, operation.ClientId, operation.OperationId,
                    LogSeverity.Error, "download.close.failed", ex.Message), CancellationToken.None);
            }
            catch (Exception)
            {
                // Si tampoco se puede escribir el log, no hay mas donde reportarlo.
            }
        }
    }
}
