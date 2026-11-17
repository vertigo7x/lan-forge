using LanForge.Client;
using LanForge.Client.Application;
using LanForge.Server.Application;
using LanForge.Server.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace LanForge.IntegrationTests;

/// <summary>
/// 4.4 — Integración completa en loopback: servidor real (Kestrel + SQLite) y cliente real
/// (HttpServerApi + SQLite), sin UI ni mDNS: el descubrimiento se simula con la dirección directa.
/// </summary>
public sealed class FullLoopbackTests : IDisposable
{
    private readonly TempDirectory _serverData = new();
    private readonly TempDirectory _clientData = new();

    public void Dispose()
    {
        _serverData.Dispose();
        _clientData.Dispose();
    }

    private async Task<(WebApplication App, string BaseAddress)> StartServerAsync()
    {
        var options = new ServerOptions(_serverData.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        var app = ServerWebHost.CreateApp(options);
        var catalog = app.Services.GetRequiredService<CatalogService>();
        var source = _serverData.CreateSourceDirectory(
            ("run.exe", "fake-game-binary"),
            ("content/level1.dat", "level-content"));
        await catalog.PublishPackageAsync("quake3", "Quake III Arena", "1.32", source, "run.exe");
        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return (app, addresses!.Addresses.First());
    }

    [Fact]
    public async Task DiscoverToInstall_FullFlowOverLoopback()
    {
        var (app, baseAddress) = await StartServerAsync();
        try
        {
            // 1. El cliente se conecta (discovery simulado con la dirección).
            var session = await ClientHost.CreateAsync(
                new ClientOptions(_clientData.Path), baseAddress, cancellationToken: CancellationToken.None);

            // 2. Primer contacto: exige confirmación TOFU del fingerprint.
            var ex = await Assert.ThrowsAsync<TrustRequiredException>(
                () => session.SyncAsync(confirmNewServer: false, CancellationToken.None));
            Assert.False(string.IsNullOrWhiteSpace(ex.PresentedFingerprint));

            // 3. Confirmación fuera de banda + sincronización.
            var sync = await session.SyncAsync(confirmNewServer: true, CancellationToken.None);
            Assert.Equal(1, sync.EntryCount);
            var catalog = await session.GetLocalCatalogAsync();
            Assert.Contains(catalog, e => e.GameId == "quake3" && e.Version == "1.32");

            // 4. Instalación completa con progreso.
            // Se usa un IProgress síncrono (no Progress<T>): Progress<T> encola el callback
            // en el hilo de fondo y la aserción de "Finished" puede correr antes de que llegue.
            var progress = new List<DownloadProgress>();
            var installed = await session.InstallAsync(
                "quake3", new SynchronousProgress<DownloadProgress>(progress.Add), CancellationToken.None);

            Assert.Equal("1.32", installed.Version);
            Assert.Equal("fake-game-binary", File.ReadAllText(Path.Combine(installed.InstallPath, "run.exe")));
            Assert.Equal("level-content", File.ReadAllText(Path.Combine(installed.InstallPath, "content", "level1.dat")));
            Assert.Contains(progress, p => p.Finished);

            // 5. Segunda sincronización: ya es de confianza, no pide nada.
            var sync2 = await session.SyncAsync(confirmNewServer: false, CancellationToken.None);
            Assert.Equal(1, sync2.EntryCount);

            // 6. Uninstall limpia.
            await session.UninstallAsync("quake3", CancellationToken.None);
            Assert.False(Directory.Exists(installed.InstallPath));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task ChangedServerIdentity_AfterPinning_ThrowsMismatch()
    {
        var (app, baseAddress) = await StartServerAsync();
        try
        {
            var options = new ClientOptions(Path.Combine(_clientData.Path, "c1"));
            var session = await ClientHost.CreateAsync(options, baseAddress, cancellationToken: CancellationToken.None);
            await session.SyncAsync(confirmNewServer: true, CancellationToken.None);

            // El servidor se reinicia con NUEVA identidad (otro dataDir) en la misma dirección.
            await app.StopAsync();
            var impostorData = Path.Combine(_serverData.Path, "impostor");
            var impostor = ServerWebHost.CreateApp(new ServerOptions(impostorData, 0, "127.0.0.1"));
            await impostor.StartAsync();
            try
            {
                var addresses = impostor.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
                var impostorAddress = addresses!.Addresses.First();

                var session2 = await ClientHost.CreateAsync(
                    new ClientOptions(Path.Combine(_clientData.Path, "c1")),
                    impostorAddress, cancellationToken: CancellationToken.None);

                await Assert.ThrowsAsync<FingerprintMismatchException>(
                    () => session2.SyncAsync(confirmNewServer: false, CancellationToken.None));
            }
            finally
            {
                await impostor.StopAsync();
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
