using System.Net;
using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.Http;
using LanForge.Server.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanForge.IntegrationTests;

/// <summary>
/// El formulario de creación de paquetes contra el endpoint real: el cliente HTTP
/// habla con un Kestrel de verdad (POST /api/v1/packages) y traduce la respuesta
/// al resultado que la UI entiende.
///
/// Se cubre el flujo completo sin mocks: éxito, error de validación por campo,
/// conflicto de versión, error de red y respuesta 500.
/// </summary>
public sealed class HttpPackageCreationServiceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static HttpPackageCreationService CreateService(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        return new HttpPackageCreationService(addresses!.Addresses.First());
    }

    private static PackageCreationRequest ValidRequest(string sourceDirectory) => new(
        GameId: "quake3",
        Title: "Quake III Arena",
        Version: "1.32",
        SourceDirectory: sourceDirectory,
        EntryPoint: "run.exe",
        Arguments: null);

    [Fact]
    public async Task Crea_el_paquete_contra_el_endpoint_real()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        using var service = CreateService(app);
        var source = _temp.CreateSourceDirectory(
            ("run.exe", "fake-binary"),
            ("content/pak0.bin", "pak-data"));

        var result = await service.CreateAsync(ValidRequest(source));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.NotNull(result.Package);
        Assert.Equal("quake3", result.Package.PackageId);
        Assert.Equal("quake3", result.Package.GameId);
        Assert.Equal("Quake III Arena", result.Package.Title);
        Assert.Equal("1.32", result.Package.Version);
        Assert.Equal(2, result.Package.FileCount);
        Assert.True(result.Package.TotalSizeBytes > 0);
        Assert.Empty(result.FieldErrors);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task Mapea_los_errores_de_validacion_del_servidor_a_su_campo()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        using var service = CreateService(app);
        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));

        // El servidor exige una ruta absoluta: esta solicitud es valida para el
        // formulario (campo obligatorio y con longitud) pero no para el servidor.
        var request = ValidRequest(source) with { Version = "no-es-una-version" };

        var result = await service.CreateAsync(request);

        Assert.False(result.Succeeded);
        Assert.Null(result.Package);
        Assert.True(
            result.FieldErrors.ContainsKey(PackageCreationFields.Version),
            $"Se esperaba un error en el campo 'version'. Errores: {string.Join(", ", result.FieldErrors.Keys)}");
        Assert.Contains(
            "N[.N[.N[.N]]]",
            result.FieldErrors[PackageCreationFields.Version],
            StringComparison.Ordinal);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Mapea_el_error_de_carpeta_inexistente_al_campo_source_directory()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        using var service = CreateService(app);
        var missing = Path.Combine(_temp.Path, "no-existe");

        var result = await service.CreateAsync(ValidRequest(missing));

        Assert.False(result.Succeeded);
        Assert.True(
            result.FieldErrors.ContainsKey(PackageCreationFields.SourceDirectory),
            $"Se esperaba un error en el campo 'sourceDirectory'. Errores: {string.Join(", ", result.FieldErrors.Keys)}");
    }

    [Fact]
    public async Task Traduce_un_conflicto_de_version_en_un_mensaje_general()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        using var service = CreateService(app);
        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));

        var first = await service.CreateAsync(ValidRequest(source));
        Assert.True(first.Succeeded, first.ErrorMessage);

        // La segunda peticion con la misma version choca: 409 sin detalle por campo.
        var second = await service.CreateAsync(ValidRequest(source));

        Assert.False(second.Succeeded);
        Assert.Null(second.Package);
        Assert.Empty(second.FieldErrors);
        Assert.NotNull(second.ErrorMessage);
        Assert.NotEmpty(second.ErrorMessage);
    }

    [Fact]
    public async Task Devuelve_un_mensaje_generico_cuando_el_servidor_no_responde()
    {
        // Puerto cerrado: no hay nadie escuchando. Es el escenario de red caida.
        using var service = new HttpPackageCreationService("http://127.0.0.1:1");
        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));

        var result = await service.CreateAsync(ValidRequest(source));

        Assert.False(result.Succeeded);
        Assert.Null(result.Package);
        Assert.Empty(result.FieldErrors);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("servidor", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Devuelve_un_mensaje_generico_cuando_el_servidor_devuelve_500()
    {
        // Host propio con un almacen de catalogo que falla al guardar: el endpoint
        // responde 500 sin detallar el error interno.
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = options.DataDirectory,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ICatalogStore, FailingCatalogStore>();
        builder.Services.AddSingleton<IPackageContentStore>(
            new FilePackageContentStore(Path.Combine(options.DataDirectory, "packages")));
        builder.Services.AddSingleton<IServerIdentity>(
            new FileServerIdentityStore(options.DataDirectory));
        builder.Services.AddSingleton<IServerLog>(
            new JsonlServerLog(options.DataDirectory));
        builder.Services.AddSingleton<CatalogService>();
        builder.Services.AddSingleton<CreatePackageUseCase>();
        builder.Services.AddSingleton<ICoverStore>(
            new FileCoverStore(Path.Combine(options.DataDirectory, "covers")));
        builder.Services.AddSingleton<ChatRoomRegistry>();
        builder.Services.AddSignalR();
        await using var app = builder.Build();
        app.MapHub<ChatHub>(ChatHub.Path);
        app.MapLanForgeApi();
        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        using var service = new HttpPackageCreationService(addresses!.Addresses.First());
        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));

        var result = await service.CreateAsync(ValidRequest(source));

        Assert.False(result.Succeeded);
        Assert.Null(result.Package);
        Assert.Empty(result.FieldErrors);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task Envia_los_datos_del_formulario_en_el_cuerpo_de_la_peticion()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        await using var app = ServerWebHost.CreateApp(options);
        await app.StartAsync();
        using var service = CreateService(app);
        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));

        var result = await service.CreateAsync(ValidRequest(source) with
        {
            GameId = "neon-drift",
            Title = "Neon Drift",
            Version = "2.1.0",
            Arguments = "--fullscreen",
        });

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal("neon-drift", result.Package!.GameId);
        Assert.Equal("Neon Drift", result.Package.Title);
        Assert.Equal("2.1.0", result.Package.Version);
    }

    /// <summary>
    /// Almacen de catalogo que falla al guardar: sirve para provocar la respuesta
    /// 500 del endpoint sin tocar el codigo de produccion.
    /// </summary>
    private sealed class FailingCatalogStore : ICatalogStore
    {
        public Task UpsertGameAsync(Game game, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Game?> GetGameAsync(string gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Game?>(null);

        public Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Game>>([]);

        public Task AddPackageAsync(Package package, CancellationToken cancellationToken = default) =>
            throw new IOException("persistencia no disponible");

        public Task ReplacePackageAsync(Package package, CancellationToken cancellationToken = default) =>
            throw new IOException("persistencia no disponible");

        public Task<Package?> GetPackageAsync(string packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Package?>(null);

        public Task<IReadOnlyList<Package>> ListLatestPackagesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Package>>([]);

        public Task UpsertSeenClientAsync(SeenClient client, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SeenClient>> ListSeenClientsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SeenClient>>([]);

        public Task RecordOperationAsync(ServerOperation operation, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> DeleteGameAsync(string gameId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyList<ServerOperation>> ListOperationsAsync(
            int limit = ICatalogStore.DefaultOperationsLimit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServerOperation>>([]);

        public Task<int> PruneOperationsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }
}
