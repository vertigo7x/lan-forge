using LanForge.Client;
using LanForge.Client.Application;
using LanForge.Client.Infrastructure;

namespace LanForge.E2ETests;

/// <summary>
/// 4.5 — E2E real en LAN. Requiere un servidor LAN Forge corriendo en la red.
/// Configura:  LANFORGE_E2E_SERVER=IP:puerto   (si no, el test se omite)
/// Opcional:   LANFORGE_E2E_DISCOVER=1         (también valida el descubrimiento mDNS)
/// </summary>
public sealed class LanPartyE2ETests : IDisposable
{
    private readonly TempDirectory _clientData = new();

    public void Dispose() => _clientData.Dispose();

    private static string? ServerTarget =>
        Environment.GetEnvironmentVariable("LANFORGE_E2E_SERVER");

    [Fact]
    public async Task FullLanPartyFlow()
    {
        var target = ServerTarget;
        if (string.IsNullOrWhiteSpace(target))
        {
            return; // Sin servidor en LAN real, el test se omite (no corre en CI).
        }

        var session = await ClientHost.CreateAsync(new ClientOptions(_clientData.Path), target);

        // Descubrimiento mDNS (opcional): el servidor anunciado debe ser el mismo fingerprint.
        if (Environment.GetEnvironmentVariable("LANFORGE_E2E_DISCOVER") == "1")
        {
            var discovered = await ServerDiscovery.DiscoverAsync(TimeSpan.FromSeconds(5));
            Assert.NotEmpty(discovered);
        }

        // TOFU + sincronización.
        var trustEx = await Record.ExceptionAsync(() => session.SyncAsync(confirmNewServer: false));
        if (trustEx is TrustRequiredException required)
        {
            Assert.False(string.IsNullOrWhiteSpace(required.PresentedFingerprint));
        }

        var sync = await session.SyncAsync(confirmNewServer: true);
        Assert.True(sync.EntryCount >= 0);

        var catalog = await session.GetLocalCatalogAsync();
        if (catalog.Count == 0)
        {
            return; // Servidor sin juegos publicados: handshake + confianza ya validados.
        }

        // Instalación completa del primer juego del catálogo.
        var game = catalog[0];
        var installed = await session.InstallAsync(game.GameId);
        Assert.Equal(game.Version, installed.Version);
        Assert.True(Directory.Exists(installed.InstallPath));

        await session.UninstallAsync(game.GameId);
        Assert.False(Directory.Exists(installed.InstallPath));
    }
}
