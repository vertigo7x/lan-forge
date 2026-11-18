using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.Http;
using LanForge.Shared;
using LanForge.Shared.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>
/// Estado final de una descarga segun como termino: el servidor no debe afirmar que una
/// descarga cortada por el cliente se completo. Antes las dos acababan como "Completed".
/// </summary>
/// <remarks>
/// El estado lo decide la cuenta de bytes, no el token de cancelacion de la peticion: el
/// token se cancela en desorden respecto al fallo de escritura, asi que la senal no era
/// fiable en ninguna de las dos direcciones. La prueba de la descarga cortada no depende
/// de tiempos: el cliente deja de leer y espera a que el servidor haya entregado ya
/// algunos bytes pero no todos (la transferencia esta a medias de verdad), y solo entonces
/// corta la conexion.
/// </remarks>
public sealed class DownloadCompletionTests : IDisposable
{
    /// <summary>
    /// Tamano del paquete de la prueba de corte. Tiene que ser mayor que todo lo que el
    /// servidor y el sistema pueden acumular en memoria intermedia (el pipe de Kestrel y
    /// el buffer del socket) para que el servidor se quede bloqueado escribiendo y no
    /// pueda terminar la transferencia antes de que el cliente corte.
    /// </summary>
    private const int LargePayloadBytes = 32 * 1024 * 1024;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Descarga_Completa_Se_Registra_Como_Completed()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        var catalog = app.Services.GetRequiredService<CatalogService>();
        var source = _temp.CreateSourceDirectory(("run.exe", "contenido-del-paquete"));
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(address) };

        var bytes = await http.GetByteArrayAsync("/api/v1/download/quake3");
        Assert.NotEmpty(bytes);

        var operation = await WaitForFinalStatusAsync(app, OperationStatus.Completed);
        Assert.Equal("package.download", operation.Type);
        Assert.Equal("quake3", operation.Detail);
    }

    [Fact]
    public async Task Descarga_Cortada_Por_El_Cliente_Se_Registra_Como_Failed()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        var catalog = app.Services.GetRequiredService<CatalogService>();

        // Un paquete grande e incompresible: con datos repetidos el zip sale pequeno, el
        // servidor termina de escribir antes de que el cliente corte y la descarga SI se
        // completa (que es justo el caso que esta prueba tiene que differentiate).
        var source = Path.Combine(_temp.Path, "src-grande");
        Directory.CreateDirectory(source);
        var payload = new byte[LargePayloadBytes];
        Random.Shared.NextBytes(payload);
        await File.WriteAllBytesAsync(Path.Combine(source, "run.exe"), payload);
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");

        var context = new TaskCompletionSource<HttpContext>();
        app.Use(async (ctx, next) =>
        {
            context.TrySetResult(ctx);
            await next();
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(address) };

        // El cliente pide la respuesta, lee un poco y luego deja de leer sin cerrar la
        // conexion: el servidor se queda bloqueado escribiendo porque nadie lee.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/download/quake3");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[64];
        var read = await body.ReadAsync(buffer);
        Assert.True(read > 0, "La descarga deberia haber entregado al menos un fragmento.");

        // Se espera a que el servidor este de verdad escribiendo a medias: que haya
        // entregado ya algunos bytes pero no todos. Eso demuestra que la transferencia
        // esta en curso, y que al cortarla ahora se corta de verdad y no antes de
        // empezar. Aqui no hay ningun plazo de tiempo: se espera a la condicion, que es
        // la que hace determinista la prueba.
        await WaitUntilTransferIsInFlightAsync(context.Task);

        // Ahora si: el cliente se va sin terminar. La transferencia se corto y el servidor
        // tiene que registrarlo como fallida, no como completada.
        response.Dispose();

        var operation = await WaitForFinalStatusAsync(app, OperationStatus.Failed);
        Assert.Equal("package.download", operation.Type);
        Assert.Contains("cancelada", operation.Detail!);
    }

    [Fact]
    public async Task Descarga_Completa_QueElCliente_Cierra_Despues_Sigue_Siendo_Completed()
    {
        // El caso contrario del corte: el cliente se baja el paquete entero y luego cierra
        // la conexion. Antes, el estado se miraba en el token de cancelacion de la
        // peticion, y ese token tambien se puede cancelar en una descarga completa (el
        // cliente cierra justo despues de recibir el ultimo byte). Con esa senal, una
        // descarga que si se completo se registraba como cancelada. Con la cuenta de
        // bytes no: se comparan los bytes entregados con los que la respuesta declaraba.
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        var catalog = app.Services.GetRequiredService<CatalogService>();
        var source = _temp.CreateSourceDirectory(("run.exe", "contenido-del-paquete"));
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(address) };

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/download/quake3");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStreamAsync();
        var sink = new MemoryStream();
        await body.CopyToAsync(sink);
        Assert.NotEmpty(sink.ToArray());

        // Y ahora cierra la conexion, sin haber dejado nada sin leer.
        response.Dispose();

        var operation = await WaitForFinalStatusAsync(app, OperationStatus.Completed);
        Assert.Equal("package.download", operation.Type);
        Assert.DoesNotContain("cancelada", operation.Detail!);
    }

    [Fact]
    public async Task Descarga_Rango_Parcial_Completa_NoSeMarcaComoFallida()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        var catalog = app.Services.GetRequiredService<CatalogService>();
        var source = _temp.CreateSourceDirectory(("run.exe", "contenido-del-paquete"));
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(address) };

        // Una reanudacion (Range) que el cliente pide y recibe entera es una descarga correcta.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/download/quake3");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(10, 99);
        using var response = await http.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(90, bytes.Length);

        var operation = await WaitForFinalStatusAsync(app, OperationStatus.Completed);
        Assert.Equal("package.download", operation.Type);
    }

    [Fact]
    public async Task Descarga_Rango_Parcial_Cortada_TambienSeRegistraComoFailed()
    {
        // El mismo camino de corte que la descarga completa, pero con un Range: la respuesta
        // declara la longitud del rango, no la del archivo, y la cuenta de bytes se compara
        // con esa longitud. Si se usara la longitud del archivo, una reanudacion a medias se
        // contaria como descarga completa.
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        var catalog = app.Services.GetRequiredService<CatalogService>();

        var source = Path.Combine(_temp.Path, "src-grande");
        Directory.CreateDirectory(source);
        var payload = new byte[LargePayloadBytes];
        Random.Shared.NextBytes(payload);
        await File.WriteAllBytesAsync(Path.Combine(source, "run.exe"), payload);
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");

        var context = new TaskCompletionSource<HttpContext>();
        app.Use(async (ctx, next) =>
        {
            context.TrySetResult(ctx);
            await next();
        });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(address) };

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/download/quake3");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, LargePayloadBytes - 1);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[64];
        Assert.True(await body.ReadAsync(buffer) > 0, "La descarga deberia haber entregado al menos un fragmento.");

        await WaitUntilTransferIsInFlightAsync(context.Task);
        response.Dispose();

        var operation = await WaitForFinalStatusAsync(app, OperationStatus.Failed);
        Assert.Equal("package.download", operation.Type);
        Assert.Contains("cancelada", operation.Detail!);
    }

    [Fact]
    public async Task Descarga_ElOperationIdEsElMismoEnLaCabeceraLaOperacionYElLog()
    {
        // La descarga tambien tiene que quedar correlacionada: el identificador que
        // propone el cliente es el mismo en la respuesta, en la fila de operacion y en
        // la linea de log. Antes cada uno lleva el suyo y no se puede seguir la traza.
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        var catalog = app.Services.GetRequiredService<CatalogService>();
        var source = _temp.CreateSourceDirectory(("run.exe", "contenido-del-paquete"));
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        http.DefaultRequestHeaders.Add(ApiEndpoints.ClientIdHeader, "itest-client-1");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/download/quake3");
        request.Headers.Add(ApiEndpoints.OperationIdHeader, "OP-DESCARGA");
        using var response = await http.SendAsync(request);
        await response.Content.ReadAsByteArrayAsync();

        Assert.True(response.Headers.TryGetValues(ApiEndpoints.OperationIdHeader, out var values));
        Assert.Equal("OP-DESCARGA", values!.Single());

        var store = app.Services.GetRequiredService<ICatalogStore>();
        var operation = await WaitForFinalStatusAsync(app, OperationStatus.Completed);
        Assert.Equal("OP-DESCARGA", operation.OperationId);
        Assert.Equal("itest-client-1", operation.ClientId);

        // Y la linea de log de la descarga lleva el mismo identificador.
        var entries = await ReadLogEntriesAsync();
        var completed = Assert.Single(entries, e => e.Event == "download.completed");
        Assert.Equal("OP-DESCARGA", completed.OperationId);
    }

    /// <summary>Lee las lineas del log JSONL del servidor.</summary>
    private async Task<List<LogEntry>> ReadLogEntriesAsync()
    {
        var logsDir = Path.Combine(_temp.Path, "logs");
        var entries = new List<LogEntry>();
        foreach (var file in Directory.GetFiles(logsDir, "*.jsonl"))
        {
            foreach (var line in await File.ReadAllLinesAsync(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = SharedJson.Deserialize<LogEntry>(line);
                Assert.NotNull(entry);
                entries.Add(entry!);
            }
        }

        return entries;
    }

    /// <summary>
    /// Espera a que la transferencia este de verdad en curso: que el servidor haya
    /// entregado ya algunos bytes pero no todos los que declaraba.
    /// </summary>
    /// <remarks>
    /// El cliente deja de leer en cuanto ha recibido el primer fragmento, asi que el
    /// servidor se queda bloqueado escribiendo en cuanto se llenan los buffers. Mientras
    /// eso pasa, la cuenta de bytes del servidor va por la mitad: exactamente la situacion
    /// de una descarga a medias, que es la que se quiere cortar.
    ///
    /// Aqui no hay ningun plazo de tiempo: se espera a la condicion, que es la que hace
    /// determinista la prueba. Si el paquete fuera tan pequeno que la transferencia
    /// terminara antes de que el cliente dejara de leer, la espera no se cumpliria y la
    /// prueba lo diria con un mensaje claro, en vez de fallar por el motivo equivocado.
    /// </remarks>
    private static async Task WaitUntilTransferIsInFlightAsync(Task<HttpContext> contextTask)
    {
        var context = await contextTask;
        long written = -1;
        long? declared = null;

        for (var attempt = 0; attempt < 400; attempt++)
        {
            if (TryGetTracker(context, out var tracker))
            {
                written = tracker.WrittenBytes;
                declared = tracker.DeclaredBytes;
                if (declared is { } total && written > 0 && written < total)
                {
                    return;
                }
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException(
            $"La descarga no llegó a quedar a medias: el servidor habia escrito {written} de " +
            $"{declared} bytes. El paquete tiene que ser mas grande que lo " +
            "que cabe en los buffers para que el servidor se quede bloqueado escribiendo: " +
            "si no, la prueba no seria determinista.");
    }

    /// <summary>Recupera el tracker de la descarga en curso, si el endpoint ya lo creo.</summary>
    private static bool TryGetTracker(HttpContext context, out DownloadTransferTracker tracker)
    {
        try
        {
            if (context.Items.TryGetValue(DownloadTransferTracker.ItemKey, out var value) &&
                value is DownloadTransferTracker found)
            {
                tracker = found;
                return true;
            }
        }
        catch (ObjectDisposedException)
        {
            // La peticion ya se cerro: no hay nada que consultar.
        }

        tracker = null!;
        return false;
    }

    /// <summary>Espera a que la operacion de descarga alcance un estado final.</summary>
    private static async Task<ServerOperation> WaitForFinalStatusAsync(WebApplication app, OperationStatus expected)
    {
        var store = app.Services.GetRequiredService<ICatalogStore>();
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var operation = (await store.ListOperationsAsync())
                .FirstOrDefault(o => o.Type == "package.download" && o.Status != OperationStatus.Started);
            if (operation is not null && operation.Status == expected)
            {
                return operation;
            }

            await Task.Delay(50);
        }

        var rows = await store.ListOperationsAsync();
        var actual = string.Join(", ", rows
            .Where(o => o.Type == "package.download")
            .Select(o => $"{o.Status}:{o.Detail}"));
        throw new Xunit.Sdk.XunitException(
            $"La operacion de descarga no llego a '{expected}'. Estado real: {actual}. " +
            "Si el estado real es 'Completed', la transferencia termino antes de que el " +
            "corte llegara al servidor: el paquete tiene que ser mas grande que lo que " +
            "cabe en los buffers para que el servidor se quede bloqueado escribiendo.");
    }
}
