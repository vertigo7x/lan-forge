using System.Net;
using System.Net.Http.Json;
using LanForge.Server.Application;
using LanForge.Server.Http;
using LanForge.Shared;
using LanForge.Shared.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>El organizador puede borrar un juego publicado: endpoint HTTP sobre Kestrel real.</summary>
public sealed class DeleteGameEndpointTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Delete_omite_el_juego_del_catalogo_y_borra_su_contenido()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);

        var catalog = app.Services.GetRequiredService<CatalogService>();
        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");

        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        using var http = new HttpClient { BaseAddress = new Uri(addresses!.Addresses.First()) };
        http.DefaultRequestHeaders.Add(ApiEndpoints.ClientIdHeader, "itest-client-1");

        var before = await http.GetFromJsonAsync<List<CatalogEntry>>("/api/v1/catalog", SharedJson.Options);
        Assert.NotNull(before);
        Assert.Single(before!);

        var response = await http.DeleteAsync("/api/v1/games/quake3");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var after = await http.GetFromJsonAsync<List<CatalogEntry>>("/api/v1/catalog", SharedJson.Options);
        Assert.NotNull(after);
        Assert.Empty(after!);

        // El paquete ya no se puede descargar.
        var download = await http.GetAsync("/api/v1/download/quake3");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);

        // Borrarlo otra vez responde 404: no hay nada que borrar.
        var again = await http.DeleteAsync("/api/v1/games/quake3");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Un_game_id_invalido_responde_400()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);

        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        using var http = new HttpClient { BaseAddress = new Uri(addresses!.Addresses.First()) };

        var response = await http.DeleteAsync("/api/v1/games/id%20con%20espacios");
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound,
            $"Se esperaba 400 o 404, pero fue {(int)response.StatusCode}.");
    }
}
