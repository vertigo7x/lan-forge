using LanForge.Shared.Logging;

namespace LanForge.Server.Application;

/// <summary>Puerto de logs de eventos del servidor (JSONL en disco).</summary>
public interface IServerLog
{
    Task LogAsync(LogEntry entry, CancellationToken cancellationToken = default);
}
