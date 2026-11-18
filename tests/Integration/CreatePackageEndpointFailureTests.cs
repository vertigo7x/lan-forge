using System.Net;
using System.Net.Http.Json;
using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.Http;
using LanForge.Shared;
using LanForge.Shared.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanForge.IntegrationTests;

/// <summary>
/// Contrato observable del endpoint de publicación cuando falla la persistencia:
/// la respuesta no debe filtrar rutas, excepciones ni stack traces.
/// </summary>
public sealed class CreatePackageEndpointFailureTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Un_error_inesperado_de_persistencia_no_filtra_datos_internos()
    {
        var options = new ServerOptions(_temp.Path, HttpPort: 0, ListenAddress: "127.0.0.1");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = options.DataDirectory,
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ICatalogStore>(new FailingCatalogStore());
        builder.Services.AddSingleton<IPackageContentStore>(
            new LanForge.Server.Infrastructure.FilePackageContentStore(Path.Combine(options.DataDirectory, "packages")));
        builder.Services.AddSingleton<IServerIdentity>(
            new LanForge.Server.Infrastructure.FileServerIdentityStore(options.DataDirectory));
        builder.Services.AddSingleton<IServerLog>(
            new LanForge.Server.Infrastructure.JsonlServerLog(options.DataDirectory));
        builder.Services.AddSingleton<CatalogService>();
        builder.Services.AddSingleton<CreatePackageUseCase>();
        builder.Services.AddSingleton<ICoverStore>(
            new LanForge.Server.Infrastructure.FileCoverStore(Path.Combine(options.DataDirectory, "covers")));
        builder.Services.AddSingleton<ChatRoomRegistry>();
        builder.Services.AddSignalR();
        await using var app = builder.Build();
        app.MapHub<ChatHub>(ChatHub.Path);
        app.MapLanForgeApi();
        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        using var http = new HttpClient { BaseAddress = new Uri(addresses!.Addresses.First()) };
        var source = _temp.CreateSourceDirectory(("run.exe", "fake-binary"));

        var response = await http.PostAsJsonAsync("/api/v1/packages", new CreatePackageRequest
        {
            GameId = "quake3",
            Title = "Quake III Arena",
            Version = "1.32",
            SourceDirectory = source,
            EntryPoint = "run.exe",
        }, SharedJson.Options);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("persistencia no disponible", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(source, body, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePackageUseCase", body, StringComparison.Ordinal);
        Assert.True(response.Content.Headers.ContentLength is null or 0);
    }

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

        public Task<IReadOnlyList<ServerOperation>> ListOperationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ServerOperation>>([]);
    }
}
