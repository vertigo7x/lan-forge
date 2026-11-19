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

        // Adjuntar la portada de un juego ya publicado. El endpoint de creacion de
        // paquetes habla JSON y el manifiesto firmado no admite una imagen, asi que
        // la portada se asigna en proceso, por su propio canal.
        builder.Services.AddSingleton<ICoverUploadService>(sp => new CatalogCoverUploadService(
            sp.GetRequiredService<CatalogService>(),
            sp.GetRequiredService<ICoverStore>()));

        // Previsualizacion de la portada en el selector del formulario.
        builder.Services.AddSingleton<ICoverPreviewLoader, AvaloniaCoverPreviewLoader>();

        // Capa de servicio del formulario de creacion de paquetes. El formulario de
        // la UI habla con el endpoint real por HTTP: el servidor se llama a si mismo
        // por el bucle local, de modo que la peticion recorre la misma ruta que
        // cualquier otro cliente (POST /api/v1/packages) y los errores por campo que
        // devuelve el servidor llegan intactos a los campos del formulario.
        //
        // La direccion se resuelve en el primer uso, no al registrar: el puerto puede
        // ser 0 (el sistema elige uno libre) y la direccion real solo se conoce con el
        // host ya arrancado.
        builder.Services.AddSingleton<IPackageCreationService>(sp =>
            new HttpPackageCreationService(() => ResolveLocalAddress(sp, options)));

        var app = builder.Build();
        app.MapHub<ChatHub>(ChatHub.Path);
        app.MapLanForgeApi();
        return app;
    }

    /// <summary>
    /// Direccion local del propio servidor, para que el formulario se llame a si
    /// mismo por la misma ruta que usaria cualquier otro cliente.
    ///
    /// Si el host ya esta arrancado se usa la direccion real (sirve para el puerto 0,
    /// donde el sistema elige uno libre). Si todavia no ha arrancado, se usa la
    /// configurada.
    /// </summary>
    private static string ResolveLocalAddress(IServiceProvider services, ServerOptions options)
    {
        var server = services.GetService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addresses = server?.Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        var bound = addresses?.Addresses.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(bound))
        {
            return bound;
        }

        return $"http://127.0.0.1:{options.HttpPort}";
    }
}
