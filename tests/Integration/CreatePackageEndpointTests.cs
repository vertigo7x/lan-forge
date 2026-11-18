using System.Net;
using System.Net.Http.Json;
using LanForge.Server.Http;
using LanForge.Shared;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using LanForge.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>El formulario crea paquetes por HTTP: POST /api/v1/packages sobre Kestrel real.</summary>
public sealed class CreatePackageEndpointTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static HttpClient CreateClient(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var http = new HttpClient { BaseAddress = new Uri(addresses!.Addresses.First()) };
        http.DefaultRequestHeaders.Add(ApiEndpoints.ClientIdHeader, "itest-organizer");
        return http;
    }

    private static CreatePackageRequest ValidRequest(string sourceDirectory) => new()
    {
        GameId = "quake3",
        Title = "Quake III Arena",
        Version = "1.32",
        SourceDirectory = sourceDirectory,
        EntryPoint = "run.exe",
    };

    [Fact]
    public async Task Publicar_un_paquete_devuelve_201_con_el_paquete_creado()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        using var http = CreateClient(app);
        var source = _temp.CreateSourceDirectory(
            ("run.exe", "fake-binary"),
            ("content/pak0.bin", "pak-data"));

        var response = await http.PostAsJsonAsync("/api/v1/packages", ValidRequest(source), SharedJson.Options);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatePackageResponse>(SharedJson.Options);
        Assert.NotNull(created);
        Assert.Equal("quake3", created.GameId);
        Assert.Equal("quake3", created.PackageId);
        Assert.Equal("Quake III Arena", created.Title);
        Assert.Equal("1.32", created.Version);
        Assert.Equal("run.exe", created.EntryPoint);
        Assert.Equal(2, created.FileCount);
        Assert.Equal("fake-binary".Length + "pak-data".Length, created.TotalSizeBytes);
        Assert.Null(created.PreviousVersion);
        Assert.NotEqual(default, created.CreatedUtc);

        // El paquete queda en el catálogo y su manifiesto está firmado.
        var entries = await http.GetFromJsonAsync<List<CatalogEntry>>("/api/v1/catalog", SharedJson.Options);
        Assert.NotNull(entries);
        var entry = Assert.Single(entries!);
        Assert.Equal("quake3", entry.GameId);
        Assert.Equal("1.32", entry.Version);

        var manifestBytes = await http.GetByteArrayAsync("/api/v1/manifest/quake3");
        var manifest = SignedPackageManifest.FromJson(manifestBytes);
        Assert.NotNull(manifest);
        Assert.True(ManifestVerifier.Verify(manifest!));

        // El zip del paquete se puede descargar.
        var zip = await http.GetByteArrayAsync("/api/v1/download/quake3");
        Assert.Equal((byte)'P', zip[0]);
    }

    [Fact]
    public async Task Una_solicitud_invalida_devuelve_400_con_el_detalle_por_campo()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        using var http = CreateClient(app);
        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));

        var request = ValidRequest(source) with { GameId = "quake 3", Version = "v1" };
        var response = await http.PostAsJsonAsync("/api/v1/packages", request, SharedJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(SharedJson.Options);
        Assert.NotNull(error);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(error.Errors, e => e.Field == "gameId" && e.Code == "invalid_format");
        Assert.Contains(error.Errors, e => e.Field == "version" && e.Code == "invalid_format");

        // No se publica nada cuando la solicitud es inválida.
        var entries = await http.GetFromJsonAsync<List<CatalogEntry>>("/api/v1/catalog", SharedJson.Options);
        Assert.NotNull(entries);
        Assert.Empty(entries!);
    }
}
