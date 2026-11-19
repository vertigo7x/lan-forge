using System.Net;
using System.Net.Http.Json;
using LanForge.Server.Application;
using LanForge.Server.Http;
using LanForge.Shared;
using LanForge.Shared.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>
/// El selector de imagen del formulario de publicar llega al servidor: la portada
/// se adjunta al juego recien creado y el catalogo la publica para que los
/// clientes la puedan descargar.
/// </summary>
public sealed class PackageCreateCoverTests : IDisposable
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

    [Fact]
    public async Task La_portada_elegida_se_adjunta_al_juego_y_el_catalogo_la_sirve()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        using var http = CreateClient(app);

        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));
        var image = CreateImage();

        // 1. El formulario crea el paquete por el endpoint real.
        var response = await http.PostAsJsonAsync(
            "/api/v1/packages",
            new CreatePackageRequest
            {
                GameId = "quake3",
                Title = "Quake III Arena",
                Version = "1.32",
                SourceDirectory = source,
                EntryPoint = "run.exe",
            },
            SharedJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // 2. La portada se adjunta al juego recien creado (el juego tiene que
        //    existir ya en el catalogo).
        var coverUploads = app.Services.GetRequiredService<ICoverUploadService>();
        var fileName = await coverUploads.UploadAsync("quake3", image);
        Assert.Equal("quake3.png", fileName);

        // 3. El catalogo la publica.
        var entries = await http.GetFromJsonAsync<List<CatalogEntry>>("/api/v1/catalog", SharedJson.Options);
        Assert.NotNull(entries);
        var entry = Assert.Single(entries!);
        Assert.Equal("quake3.png", entry.CoverPath);

        // 4. Y el servidor la sirve como imagen.
        var served = await http.GetAsync("/api/v1/covers/quake3.png");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            await File.ReadAllBytesAsync(image),
            await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task La_portada_se_adjunta_despues_de_crear_el_paquete()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        using var http = CreateClient(app);

        var coverUploads = app.Services.GetRequiredService<ICoverUploadService>();

        // Antes de crear el paquete el juego no existe: la portada no se puede
        // asignar todavia. Es la restriccion que obliga a subirla despues.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => coverUploads.UploadAsync("quake3", CreateImage()));

        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));
        var response = await http.PostAsJsonAsync(
            "/api/v1/packages",
            new CreatePackageRequest
            {
                GameId = "quake3",
                Title = "Quake III Arena",
                Version = "1.32",
                SourceDirectory = source,
                EntryPoint = "run.exe",
            },
            SharedJson.Options);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Ahora si: el juego existe y la portada se puede asignar.
        var fileName = await coverUploads.UploadAsync("quake3", CreateImage());
        Assert.Equal("quake3.png", fileName);
    }

    [Fact]
    public async Task El_servicio_de_portadas_rechaza_un_fichero_que_no_es_una_imagen()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();

        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));
        using var http = CreateClient(app);
        await http.PostAsJsonAsync(
            "/api/v1/packages",
            new CreatePackageRequest
            {
                GameId = "quake3",
                Title = "Quake III Arena",
                Version = "1.32",
                SourceDirectory = source,
                EntryPoint = "run.exe",
            },
            SharedJson.Options);

        var notAnImage = Path.Combine(_temp.Path, "documento.txt");
        await File.WriteAllTextAsync(notAnImage, "esto no es una imagen");

        var coverUploads = app.Services.GetRequiredService<ICoverUploadService>();

        await Assert.ThrowsAsync<ArgumentException>(() => coverUploads.UploadAsync("quake3", notAnImage));
    }

    /// <summary>Crea un PNG minimo y valido en disco.</summary>
    private string CreateImage()
    {
        var path = Path.Combine(_temp.Path, "portada.png");
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
        return path;
    }
}
