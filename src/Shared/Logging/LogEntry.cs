namespace LanForge.Shared.Logging;

/// <summary>
/// Entrada de log JSON correlacionada. Cliente y servidor emiten la misma forma:
/// una línea JSON por entrada, enlazable por ClientId/OperationId.
/// </summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string ClientId,
    string OperationId,
    LogSeverity Severity,
    string Event,
    string Message,
    IReadOnlyDictionary<string, string>? Data = null);
