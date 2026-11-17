namespace LanForge.Client.Domain;

public enum ClientOperationStatus
{
    Started,
    Completed,
    Failed,
}

/// <summary>Operación del cliente (instalación, sincronización...), correlacionable con el servidor.</summary>
public sealed record ClientOperation(
    string OperationId,
    string Type,
    ClientOperationStatus Status,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc = null,
    string? Detail = null);
