using LanForge.Server.Application;
using LanForge.Shared.Logging;

namespace LanForge.UnitTests.Server.Fakes;

/// <summary>Log en memoria que guarda las lineas escritas, para poder inspeccionarlas.</summary>
public sealed class RecordingServerLog : IServerLog
{
    private readonly List<LogEntry> _entries = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Si es true, toda escritura falla: simula un log en disco caido.</summary>
    public bool Fails { get; set; }

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            _gate.Wait();
            try
            {
                return _entries.ToArray();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task LogAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        if (Fails)
        {
            throw new IOException("El log en disco no se puede escribir.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _entries.Add(entry);
        }
        finally
        {
            _gate.Release();
        }
    }
}
