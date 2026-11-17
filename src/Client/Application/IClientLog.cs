using LanForge.Shared.Logging;

namespace LanForge.Client.Application;

/// <summary>Puerto de logs del cliente (JSONL en disco).</summary>
public interface IClientLog
{
    Task LogAsync(LogEntry entry, CancellationToken cancellationToken = default);
}
