using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.Http;
using LanForge.Shared;
using LanForge.Shared.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>
/// Estado final de una descarga segun como termino: el servidor no debe afirmar que una
/// descarga cortada por el cliente se completo. Antes las dos acababan como "Completed".
/// </summary>
public sealed class DownloadCompletionTests : IDisposable
{
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

        // Un paquete grande e incompresible para que la transferencia siga en curso cuando
        // el cliente corta: con datos repetidos el zip sale pequeno y el servidor termina
        // antes de que el cliente se vaya (la descarga SI se completo y asi se registra).
        var source = Path.Combine(_temp.Path, "src-grande");
        Directory.CreateDirectory(source);
        var payload = new byte[16 * 1024 * 1024];
        Random.Shared.NextBytes(payload);
        await File.WriteAllBytesAsync(Path.Combine(source, "run.exe"), payload);
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var http = new HttpClient { BaseAddress = new Uri(address) };

        // El cliente pide la respuesta, lee un poco y se va sin terminar: la peticion se cancela.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/download/quake3");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[64];
        var read = await body.ReadAsync(buffer);
        Assert.True(read > 0, "La descarga deberia haber entregado al menos un fragmento.");
        response.Dispose();

        var operation = await WaitForFinalStatusAsync(app, OperationStatus.Failed);
        Assert.Equal("package.download", operation.Type);
        Assert.Contains("cancelada", operation.Detail!);
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

    /// <summary>Espera a que la operacion de descarga alcance un estado final.</summary>
    private static async Task<ServerOperation> WaitForFinalStatusAsync(WebApplication app, OperationStatus expected)
    {
        var store = app.Services.GetRequiredService<ICatalogStore>();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var operation = (await store.ListOperationsAsync())
                .FirstOrDefault(o => o.Type == "package.download" && o.Status != OperationStatus.Started);
            if (operation is not null && operation.Status == expected)
            {
                return operation;
            }

            await Task.Delay(100);
        }

        var rows = await store.ListOperationsAsync();
        var actual = string.Join(", ", rows
            .Where(o => o.Type == "package.download")
            .Select(o => $"{o.Status}:{o.Detail}"));
        throw new Xunit.Sdk.XunitException(
            $"La operacion de descarga no llego a '{expected}'. Estado real: {actual}");
    }
}
