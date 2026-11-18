using Microsoft.AspNetCore.Http;

namespace LanForge.Server.Http;

/// <summary>
/// Cuenta los bytes que el servidor ha entregado en la respuesta y permite decidir, sin
/// depender de tiempos ni del token de cancelacion, si la transferencia llego completa.
/// </summary>
/// <remarks>
/// La senal anterior era <c>HttpContext.RequestAborted.IsCancellationRequested</c> leida en el
/// momento de cerrar el stream. Ese token se cancela en desorden respecto al fallo de escritura:
/// a veces se cancela antes de que la copia termine (una descarga completa se registra como
/// cancelada) y a veces todavia no se ha cancelado cuando el stream se cierra (una descarga
/// cortada se registra como completada). En los dos sentidos la senal miente.
///
/// La senal que si es determinista es la de los bytes: la respuesta declara cuantos bytes
/// lleva (<c>Content-Length</c>, que ASP.NET Core calcula con la misma logica de rango que usa
/// para copiar) y aqui se cuentan los que de verdad salieron. Si coinciden, la respuesta
/// completa llego al cliente; si no, la transferencia se corto.
/// </remarks>
public sealed class DownloadTransferTracker
{
    /// <summary>
    /// Clave en <see cref="HttpContext.Items"/> donde se guarda el tracker de la descarga
    /// en curso, para que se pueda consultar desde fuera (por ejemplo, una prueba que
    /// quiere saber si el servidor esta escribiendo).
    /// </summary>
    public const string ItemKey = "LanForge.DownloadTransfer";

    private readonly CountingResponseBody _body;

    /// <summary>
    /// Envuelve el cuerpo de la respuesta para contar los bytes escritos. Se debe llamar
    /// antes de que la respuesta empiece a enviarse.
    /// </summary>
    public DownloadTransferTracker(HttpContext context)
    {
        _body = new CountingResponseBody(context.Response.Body);
        context.Response.Body = _body;
        context.Items[ItemKey] = this;

        // Al empezar la respuesta ya se sabe cuantos bytes lleva (el Content-Length que
        // calcula ASP.NET Core con la misma logica de rango que usa para copiar). Se anota
        // aqui para poder comparar sin depender de la peticion, que para entonces puede
        // estar ya cerrada.
        context.Response.OnStarting(() =>
        {
            DeclareLength(context.Response.ContentLength);
            return Task.CompletedTask;
        });
    }

    /// <summary>Bytes que el servidor ha escrito en la respuesta.</summary>
    public long WrittenBytes => _body.WrittenBytes;

    /// <summary>
    /// Bytes que la respuesta declaraba llevar. Lo fija el servidor al empezar la
    /// respuesta (es el <c>Content-Length</c> que calcula ASP.NET Core con la misma logica
    /// de rango que usa para copiar) y no cambia despues.
    /// </summary>
    public long? DeclaredBytes { get; private set; }

    /// <summary>
    /// Indica si la respuesta se entrego entera. Es <see langword="true"/> cuando los bytes
    /// escritos coinciden con los que la respuesta declaraba. Si la respuesta no declaraba
    /// longitud (no deberia pasar en una descarga) se considera incompleta: no hay forma
    /// de saber que falto.
    /// </summary>
    public bool IsComplete => DeclaredBytes is { } expected && expected == WrittenBytes;

    /// <summary>
    /// Fija la longitud que la respuesta declara. La escribe ASP.NET Core en la cabecera
    /// antes de copiar el cuerpo, asi que el tracker la anota en ese momento para poder
    /// comparar sin depender de la peticion, que para entonces puede estar ya cerrada.
    /// </summary>
    public void DeclareLength(long? contentLength) => DeclaredBytes = contentLength;
}

/// <summary>
/// Cuerpo de la respuesta que cuenta los bytes escritos sin alterar el flujo.
/// </summary>
/// <remarks>
/// Solo se sobrescriben las sobrecargas de <c>byte[]</c>: las de <c>ReadOnlySpan</c> y
/// <c>ReadOnlyMemory</c> de <see cref="Stream"/> delegan por defecto en las de <c>byte[]</c>,
/// asi que contarlas tambien ahi sumaria los mismos bytes dos veces.
/// </remarks>
internal sealed class CountingResponseBody : Stream
{
    private readonly Stream _inner;
    private long _writtenBytes;

    public CountingResponseBody(Stream inner) => _inner = inner;

    public long WrittenBytes => Interlocked.Read(ref _writtenBytes);

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

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        _inner.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _inner.Write(buffer, offset, count);
        Interlocked.Add(ref _writtenBytes, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        // Los bytes se cuentan despues de escribirlos: si la escritura falla, el error
        // propaga y esos bytes no llegaron a salir.
        await _inner.WriteAsync(buffer, offset, count, cancellationToken);
        Interlocked.Add(ref _writtenBytes, count);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
