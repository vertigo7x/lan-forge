using LanForge.Client.Application;
using LanForge.Client.Infrastructure;

namespace LanForge.Client;

/// <summary>Configuración de datos del cliente.</summary>
public sealed record ClientOptions(string DataDirectory)
{
    /// <summary>
    /// Variable de entorno que fija la carpeta de datos. Sirve para las pruebas de UI
    /// (no tocar el perfil real) y para ejecutar el cliente con los datos en otro sitio.
    /// </summary>
    public const string DataDirectoryVariable = "LANFORGE_CLIENT_DATA";

    public string DownloadsDirectory => Path.Combine(DataDirectory, "downloads");

    public string InstallRoot => Path.Combine(DataDirectory, "games");

    public static ClientOptions Default()
    {
        var configured = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new(configured);
        }

        return new(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanForge", "Client"));
    }
}

/// <summary>Raíz de composición del cliente: construye la sesión contra un servidor concreto.</summary>
public static class ClientHost
{
    public static async Task<ClientSession> CreateAsync(
        ClientOptions options, string baseUrl, HttpMessageHandler? handler = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.DataDirectory);
        var store = new SqliteClientStore(Path.Combine(options.DataDirectory, "lanforge-client.db"));
        var clientId = await store.GetOrCreateClientIdAsync(cancellationToken);
        var api = new HttpServerApi(baseUrl, clientId, handler);
        var trust = new TrustManager(store);
        var sync = new CatalogSyncService(api, trust, store);
        var downloader = new PackageDownloader(api);
        var installer = new GameInstaller(store, options.InstallRoot);
        var launcher = new GameLauncher(new SystemProcessStarter());
        var log = new JsonlClientLog(options.DataDirectory);
        return new ClientSession(
            api, sync, trust, downloader, installer, launcher, store,
            options.DownloadsDirectory, clientId, log);
    }
}
