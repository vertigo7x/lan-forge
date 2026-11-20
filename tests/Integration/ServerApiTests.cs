using System.Net;
using System.Net.Http.Json;
using LanForge.Server.Application;
using LanForge.Server.Http;
using LanForge.Shared;
using LanForge.Shared.Manifest;
using LanForge.Shared.Models;
using LanForge.Shared.Protocol;
using LanForge.Shared.Security;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace LanForge.IntegrationTests;

/// <summary>Flujo completo sobre Kestrel real: publicar → hello → catalog → manifest → download.</summary>
public sealed class ServerApiTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task FullFlow_EndToEndOverHttp()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);

        // Sembrar el catálogo antes de arrancar Kestrel.
        var catalog = app.Services.GetRequiredService<CatalogService>();
        var source = _temp.CreateSourceDirectory(
            ("run.exe", "fake-binary"),
            ("content/pak0.bin", "pak-data"));
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");
        var coverPath = Path.Combine(_temp.Path, "cover.png");
        await File.WriteAllBytesAsync(coverPath, [137, 80, 78, 71, 13, 10, 26, 10]);
        await catalog.SetCoverAsync("quake3", coverPath, app.Services.GetRequiredService<ICoverStore>());
        var logoPath = Path.Combine(_temp.Path, "lan-party-logo.png");
        WriteTransparentPng(logoPath);
        var logoFileName = await app.Services.GetRequiredService<ILanPartyBrandingStore>().SaveLogoAsync(logoPath);

        await app.StartAsync();
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        using var http = new HttpClient { BaseAddress = new Uri(addresses!.Addresses.First()) };
        http.DefaultRequestHeaders.Add(ApiEndpoints.ClientIdHeader, "itest-client-1");

        // Hello: versión de protocolo + fingerprint verificable.
        var identity = app.Services.GetRequiredService<IServerIdentity>();
        var hello = await http.GetFromJsonAsync<HelloResponse>("/api/v1/hello", SharedJson.Options);
        Assert.NotNull(hello);
        Assert.Equal(ProtocolVersion.Current, hello.ProtocolVersion);
        Assert.Equal(identity.Fingerprint, Fingerprint.Parse(hello.Fingerprint));
        Assert.False(string.IsNullOrWhiteSpace(hello.ServerName));
        Assert.False(string.IsNullOrWhiteSpace(hello.ServerId));
        Assert.Equal(logoFileName, hello.LanPartyLogoPath);

        // Catalog: una entrada por juego publicado.
        var entries = await http.GetFromJsonAsync<List<CatalogEntry>>("/api/v1/catalog", SharedJson.Options);
        var entry = Assert.Single(entries!);
        Assert.Equal("quake3", entry.GameId);
        Assert.Equal("1.32", entry.Version);
        Assert.Equal(2, entry.FileCount);

        // Manifest: JSON válido y firmado por la identidad del servidor.
        var manifestBytes = await http.GetByteArrayAsync("/api/v1/manifest/quake3");
        var manifest = SignedPackageManifest.FromJson(manifestBytes);
        Assert.NotNull(manifest);
        Assert.True(ManifestVerifier.Verify(manifest));
        Assert.True(ManifestVerifier.MatchesFingerprint(manifest, identity.Fingerprint));

        // Download por archivo: contenido exacto y 404 fuera del manifiesto.
        var downloaded = await http.GetByteArrayAsync("/api/v1/download/quake3/content/pak0.bin");
        Assert.Equal("pak-data"u8.ToArray(), downloaded);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/download/quake3/nope.bin")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/manifest/unknown")).StatusCode);

        // Descarga del zip completo: cabecera PK y mismo contenido extraíble.
        var zipBytes = await http.GetByteArrayAsync("/api/v1/download/quake3");
        Assert.Equal((byte)'P', zipBytes[0]);
        Assert.Equal((byte)'K', zipBytes[1]);
        await using (var zipStream = new MemoryStream(zipBytes))
        using (var archive = new System.IO.Compression.ZipArchive(zipStream))
        {
            Assert.Equal(2, archive.Entries.Count);
        }

        // El header de cliente fue registrado.
        var store = app.Services.GetRequiredService<ICatalogStore>();
        var seen = await store.ListSeenClientsAsync();
        Assert.Contains(seen, c => c.ClientId == "itest-client-1");

        // Portada del juego: el catálogo la referencia y el endpoint la sirve.
        var coverEntry = entries!.Single();
        Assert.False(string.IsNullOrWhiteSpace(coverEntry.CoverPath));
        var coverBytes = await http.GetByteArrayAsync($"/api/v1/covers/{coverEntry.CoverPath}");
        Assert.Equal((byte)137, coverBytes[0]);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/covers/..%2Fidentity.json")).StatusCode);

        // Branding global: se anuncia por hello y solo el archivo configurado es público.
        var logoBytes = await http.GetByteArrayAsync($"/api/v1/branding/{logoFileName}");
        Assert.Equal((byte)137, logoBytes[0]);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/branding/..%2Fidentity.json")).StatusCode);
    }

    private static void WriteTransparentPng(string path)
    {
        using var bitmap = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
