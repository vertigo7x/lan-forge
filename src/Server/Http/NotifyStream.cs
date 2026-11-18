namespace LanForge.Server.Http;

/// <summary>
/// Envuelve un stream y notifica una sola vez al disponerse (descarga completada/cancelada).
/// </summary>
/// <remarks>
/// El aviso se dispara cuando se cierra el stream, que es DESPUES de que la copia al cliente
/// haya terminado (o haya fallado). Por eso el cierre no dice por si solo si la transferencia
/// se completo: eso lo dice la cuenta de bytes, que es determinista.
/// </remarks>
public sealed class NotifyStream : Stream
{
    private readonly Stream _inner;
    private readonly Action _onDisposed;
    private int _notified;

    public NotifyStream(Stream inner, Action onDisposed)
    {
        _inner = inner;
        _onDisposed = onDisposed;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Notify();
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        Notify();
        await _inner.DisposeAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void Notify()
    {
        if (Interlocked.Exchange(ref _notified, 1) == 0)
        {
            _onDisposed();
        }
    }
}
