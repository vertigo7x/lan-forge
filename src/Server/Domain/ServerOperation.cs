namespace LanForge.Server.Domain;

public enum OperationStatus
{
    Started,
    Completed,
    Failed,
}

/// <summary>Operación registrada por el servidor (descarga, sincronización...).</summary>
public sealed record ServerOperation(
    string OperationId,
    string ClientId,
    string Type,
    OperationStatus Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc = null,
    string? Detail = null);
