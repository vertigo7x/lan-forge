using LanForge.Server.Application;
using LanForge.Server.Domain;
using LanForge.Server.Infrastructure;
using LanForge.UnitTests.Server.Fakes;

namespace LanForge.UnitTests.Server;

public sealed class DeleteGameTests
{
    [Fact]
    public async Task Borra_el_juego_del_store_y_su_contenido_en_disco()
    {
        var root = Directory.CreateTempSubdirectory("lanforge-delete");
        try
        {
            var store = new InMemoryCatalogStore();
            var content = new FilePackageContentStore(Path.Combine(root.FullName, "packages"));
            var covers = new InMemoryCoverStore();
            var catalog = new CatalogService(store, content, new FakeIdentity());

            var source = Path.Combine(root.FullName, "juego");
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "juego.exe"), "contenido");
            await catalog.PublishPackageAsync("neon-drift", "Neon Drift", "1.0.0", source, "juego.exe");

            // Portada asociada al juego.
            var coverFile = Path.Combine(root.FullName, "cover.png");
            await File.WriteAllTextAsync(coverFile, "png");
            await catalog.SetCoverAsync("neon-drift", coverFile, covers);

            Assert.True(File.Exists(Path.Combine(root.FullName, "packages", "neon-drift.zip")));
            Assert.NotNull(await store.GetGameAsync("neon-drift"));

            var deleted = await catalog.DeleteGameAsync("neon-drift", covers);

            Assert.True(deleted);
            Assert.Null(await store.GetGameAsync("neon-drift"));
            Assert.Null(await store.GetPackageAsync("neon-drift"));
            Assert.False(File.Exists(Path.Combine(root.FullName, "packages", "neon-drift.zip")));
            Assert.False(covers.Exists("neon-drift.png"));
            Assert.Empty(await catalog.ListCatalogAsync());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Un_juego_inexistente_no_se_borra_ni_falla()
    {
        var root = Directory.CreateTempSubdirectory("lanforge-delete");
        try
        {
            var store = new InMemoryCatalogStore();
            var content = new FilePackageContentStore(Path.Combine(root.FullName, "packages"));
            var catalog = new CatalogService(store, content, new FakeIdentity());

            Assert.False(await catalog.DeleteGameAsync("no-existe", new InMemoryCoverStore()));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Un_game_id_invalido_se_rechaza()
    {
        var root = Directory.CreateTempSubdirectory("lanforge-delete");
        try
        {
            var store = new InMemoryCatalogStore();
            var content = new FilePackageContentStore(Path.Combine(root.FullName, "packages"));
            var catalog = new CatalogService(store, content, new FakeIdentity());

            await Assert.ThrowsAsync<ArgumentException>(() =>
                catalog.DeleteGameAsync("id con espacios", new InMemoryCoverStore()));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class FakeIdentity : IServerIdentity
    {
        private readonly Shared.Security.KeyPair _keyPair = Shared.Security.Ed25519.GenerateKeyPair();

        public string ServerId => "test-server";

        public string ServerName => "Test";

        public Shared.Security.KeyPair KeyPair => _keyPair;

        public Shared.Security.Fingerprint Fingerprint =>
            Shared.Security.Fingerprint.FromPublicKey(_keyPair.PublicKey);
    }
}
