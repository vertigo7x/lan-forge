using System.Net;
using LanForge.Server.Application;
using LanForge.Server.Http;
using LanForge.Shared;
using LanForge.Shared.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>
/// Correlacion de la peticion: el identificador de operacion que propone el cliente debe
/// ser el mismo en la fila de operaciones, en la linea de log y en la respuesta.
/// </summary>
public sealed class OperationIdTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task OperationId_DelCliente_EsElMismoEnLaOperacionElLogYLaRespuesta()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var http = new HttpClient { BaseAddress = new Uri(address) };
        http.DefaultRequestHeaders.Add(ApiEndpoints.ClientIdHeader, "itest-client-1");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/catalog");
        request.Headers.Add(ApiEndpoints.OperationIdHeader, "OP-MIO");
        using var response = await http.SendAsync(request);

        // La respuesta devuelve el identificador efectivo, para que el cliente pueda correlacionar.
        Assert.True(response.Headers.TryGetValues(ApiEndpoints.OperationIdHeader, out var values));
        Assert.Equal("OP-MIO", values!.Single());

        // La fila de operaciones usa ese mismo identificador, no uno nuevo generado por el servidor.
        var store = app.Services.GetRequiredService<ICatalogStore>();
        var operations = await store.ListOperationsAsync();
        var operation = Assert.Single(operations);
        Assert.Equal("OP-MIO", operation.OperationId);
        Assert.Equal("itest-client-1", operation.ClientId);

        // Y la linea de log que describe esa peticion lleva el mismo identificador.
        var entries = await ReadLogEntriesAsync();
        var catalogServed = Assert.Single(entries, e => e.Event == "catalog.served");
        Assert.Equal("OP-MIO", catalogServed.OperationId);
        Assert.Equal("itest-client-1", catalogServed.ClientId);
    }

    [Fact]
    public async Task OperationId_SinCabeceraDelCliente_SeGeneraYSeDevuelve()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        using var http = new HttpClient { BaseAddress = new Uri(address) };
        using var response = await http.GetAsync("/api/v1/catalog");

        Assert.True(response.Headers.TryGetValues(ApiEndpoints.OperationIdHeader, out var values));
        var operationId = values!.Single();
        Assert.False(string.IsNullOrWhiteSpace(operationId));

        var store = app.Services.GetRequiredService<ICatalogStore>();
        var operation = Assert.Single(await store.ListOperationsAsync());
        Assert.Equal(operationId, operation.OperationId);
    }

    /// <summary>Lee las lineas del log JSONL del servidor (una por operacion).</summary>
    private async Task<List<LogEntry>> ReadLogEntriesAsync()
    {
        var logsDir = Path.Combine(_temp.Path, "logs");
        var files = Directory.GetFiles(logsDir, "*.jsonl");
        var entries = new List<LogEntry>();
        foreach (var file in files)
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
}
