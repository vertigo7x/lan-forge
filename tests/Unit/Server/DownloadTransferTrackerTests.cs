using LanForge.Server.Http;
using Microsoft.AspNetCore.Http;

namespace LanForge.UnitTests.Server;

/// <summary>
/// La senal que decide si una descarga se completo o se corto: los bytes que el servidor
/// escribio frente a los que la respuesta declaraba.
/// </summary>
/// <remarks>
/// Estas pruebas cubren la clase aislada: la integracion (HTTP de verdad) esta en
/// <c>tests/Integration/DownloadCompletionTests.cs</c>.
/// </remarks>
public sealed class DownloadTransferTrackerTests
{
    [Fact]
    public async Task UnaRespuestaCompleta_SeConsideraCompleta()
    {
        var (context, tracker) = CrearTracker(100);

        await context.Response.Body.WriteAsync(new byte[100]);

        Assert.Equal(100, tracker.WrittenBytes);
        Assert.True(tracker.IsComplete);
    }

    [Fact]
    public async Task UnaRespuestaCortada_SeConsideraIncompleta()
    {
        var (context, tracker) = CrearTracker(100);

        await context.Response.Body.WriteAsync(new byte[60]);

        Assert.Equal(60, tracker.WrittenBytes);
        Assert.False(tracker.IsComplete);
    }

    [Fact]
    public async Task UnaRespuestaSinLongitudDeclarada_SeConsideraIncompleta()
    {
        // Sin Content-Length no hay forma de saber que falto, asi que no se puede afirmar
        // que la descarga se completo.
        var (context, tracker) = CrearTracker(null);

        await context.Response.Body.WriteAsync(new byte[60]);

        Assert.False(tracker.IsComplete);
    }

    [Fact]
    public async Task UnRangoSeMideContraLaLongitudDelRango()
    {
        // La respuesta de un rango declara la longitud del rango, no la del archivo. Por eso
        // una reanudacion completa (que deja el archivo entero sin leer) se cuenta como
        // descarga completa.
        var (context, tracker) = CrearTracker(90);

        await context.Response.Body.WriteAsync(new byte[90]);

        Assert.Equal(90, tracker.WrittenBytes);
        Assert.True(tracker.IsComplete);
    }

    [Fact]
    public async Task LosBytesSeCuentanUnaSolaVez()
    {
        // La cuenta no puede depender de la sobrecarga que use el servidor: si se contasen
        // dos veces, una respuesta completa pareceria mas larga de lo declarado.
        var (context, tracker) = CrearTracker(2000);
        var payload = new byte[1000];

        await context.Response.Body.WriteAsync(payload);
        await context.Response.Body.WriteAsync(payload);

        Assert.Equal(2000, tracker.WrittenBytes);
        Assert.True(tracker.IsComplete);
    }

    [Fact]
    public async Task UnaEscrituraFallida_NoCuentaLosBytesQueNoSalieron()
    {
        // Si la escritura falla (el cliente se fue), los bytes no llegaron a salir y no
        // pueden contarse como entregados.
        var context = new DefaultHttpContext();
        context.Response.Body = new FailingStream();
        var tracker = new DownloadTransferTracker(context);
        tracker.DeclareLength(100);

        await Assert.ThrowsAnyAsync<IOException>(async () =>
            await context.Response.Body.WriteAsync(new byte[100].AsMemory()));

        Assert.Equal(0, tracker.WrittenBytes);
        Assert.False(tracker.IsComplete);
    }

    /// <summary>
    /// Crea un tracker con la longitud de la respuesta ya declarada, como hace ASP.NET
    /// Core al empezar a enviar las cabeceras.
    /// </summary>
    private static (HttpContext Context, DownloadTransferTracker Tracker) CrearTracker(long? declaredBytes)
    {
        var context = new DefaultHttpContext();
        var tracker = new DownloadTransferTracker(context);
        tracker.DeclareLength(declaredBytes);
        return (context, tracker);
    }

    /// <summary>Stream que falla al escribir, como cuando el cliente corta la conexion.</summary>
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("El cliente se fue.");
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("El cliente se fue."));
    }
}
