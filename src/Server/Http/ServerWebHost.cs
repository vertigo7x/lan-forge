using LanForge.Server.Application;
using LanForge.Server.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LanForge.Server.Http;

/// <summary>Fábrica del host HTTP. Recibe opciones y devuelve la app lista para StartAsync.</summary>
public static class ServerWebHost
{
    public static WebApplication CreateApp(ServerOptions options)
    {
        Directory.CreateDirectory(options.DataDirectory);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = options.DataDirectory,
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls($"http://{options.ListenAddress}:{options.HttpPort}");

        builder.Services.AddSingleton<ICatalogStore>(
            new SqliteCatalogStore(Path.Combine(options.DataDirectory, "lanforge-server.db")));
        builder.Services.AddSingleton<IPackageContentStore>(
            new FilePackageContentStore(Path.Combine(options.DataDirectory, "packages")));
        builder.Services.AddSingleton<ICoverStore>(
            new FileCoverStore(Path.Combine(options.DataDirectory, "covers")));
        builder.Services.AddSingleton<ChatRoomRegistry>();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IServerIdentity>(
            new FileServerIdentityStore(options.DataDirectory));
        builder.Services.AddSingleton<IServerLog>(
            new JsonlServerLog(options.DataDirectory));
        builder.Services.AddSingleton<CatalogService>();
        builder.Services.AddSingleton<CreatePackageUseCase>();

        // Capa de servicio del formulario de creacion de paquetes. Ahora mismo se
        // resuelve con la simulacion (no toca la red); cuando exista el cliente
        // HTTP real, basta cambiar esta linea por la implementacion real: los
        // consumidores dependen de IPackageCreationService, no de esta clase.
        builder.Services.AddSingleton<IPackageCreationService, MockPackageCreationService>();

        var app = builder.Build();
        app.MapHub<ChatHub>(ChatHub.Path);
        app.MapLanForgeApi();
        return app;
    }
}
