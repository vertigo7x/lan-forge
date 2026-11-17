using System.Text.Json;
using LanForge.Client.Application;
using LanForge.Shared;
using LanForge.Shared.Logging;

namespace LanForge.Client.Infrastructure;

/// <summary>Log JSONL diario del cliente: {dataDir}/logs/client-{yyyyMMdd}.jsonl.</summary>
public sealed class JsonlClientLog : IClientLog
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonlClientLog(string dataDirectory)
    {
        var logsDir = Path.Combine(dataDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        _filePath = Path.Combine(logsDir, $"client-{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl");
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
