using System.Text.Json;
using LanForge.Server.Application;
using LanForge.Shared;
using LanForge.Shared.Logging;

namespace LanForge.Server.Infrastructure;

/// <summary>Log JSONL diario: {dataDir}/logs/server-{yyyyMMdd}.jsonl, una línea por entrada.</summary>
public sealed class JsonlServerLog : IServerLog
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonlServerLog(string dataDirectory)
    {
        var logsDir = Path.Combine(dataDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        _filePath = Path.Combine(logsDir, $"server-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl");
    }

    public async Task LogAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        var line = System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(entry, SharedJson.Options) + Environment.NewLine);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            await stream.WriteAsync(line, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
}
