using LanForge.Server.Domain;
using LanForge.Server.ViewModels;

namespace LanForge.UnitTests.Server;

/// <summary>
/// El listado del catalogo del organizador se refresca cada 2 segundos. Si en cada
/// refresco se crearan filas nuevas, el boton de borrar perderia su confirmacion y el
/// organizador tendria que pulsar dos veces en menos de 2 segundos para que el borrado
/// se ejecutara. Estas pruebas comprueban que la fila se reutiliza.
/// </summary>
public sealed class CatalogRowCacheTests
{
    private static Package CreatePackage(string gameId, string version, int fileCount = 1, long size = 10) => new()
    {
        PackageId = gameId,
        GameId = gameId,
        Version = version,
        EntryPoint = "run.exe",
        Arguments = null,
        TotalSizeBytes = size,
        FileCount = fileCount,
        CreatedUtc = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero),
        Manifest = null!,
    };

    [Fact]
    public void ElRefrescoReutilizaLaFilaYConservaLaConfirmacionDeBorrado()
    {
        var cache = new CatalogRowCache();
        var packages = new[] { CreatePackage("quake3", "1.32") };
        var games = new[] { new Game("quake3", "Quake III Arena") };

        var first = cache.Build(packages, games);
        Assert.Single(first);
        first[0].ConfirmText = "¿Seguro?";

        // El refresco llega 2 segundos despues, con el mismo catalogo.
        var second = cache.Build(packages, games);

        Assert.Same(first[0], second[0]);
        Assert.Equal("¿Seguro?", second[0].ConfirmText);
    }

    [Fact]
    public void ElRefrescoActualizaLosDatosDelPaqueteSinTocarLaConfirmacion()
    {
        var cache = new CatalogRowCache();
        var first = cache.Build(
            [CreatePackage("quake3", "1.32", fileCount: 1, size: 10)],
            [new Game("quake3", "Quake III Arena")]);
        first[0].ConfirmText = "¿Seguro?";

        // Se republica otra version y cambia el titulo.
        var second = cache.Build(
            [CreatePackage("quake3", "1.33", fileCount: 5, size: 900)],
            [new Game("quake3", "Quake III Arena Remastered")]);

        Assert.Same(first[0], second[0]);
        Assert.Equal("1.33", second[0].Version);
        Assert.Equal(5, second[0].FileCount);
        Assert.Equal(900, second[0].TotalSizeBytes);
        Assert.Equal("Quake III Arena Remastered", second[0].Title);
        Assert.Equal("¿Seguro?", second[0].ConfirmText);
    }

    [Fact]
    public void CadaJuegoConservaSuPropiaConfirmacion()
    {
        var cache = new CatalogRowCache();
        var packages = new[] { CreatePackage("quake3", "1.32"), CreatePackage("doom", "1.9") };
        var games = new[] { new Game("quake3", "Quake III Arena"), new Game("doom", "Doom") };

        var first = cache.Build(packages, games);
        first[0].ConfirmText = "¿Seguro?";

        var second = cache.Build(packages, games);

        Assert.Equal("¿Seguro?", second[0].ConfirmText);
        Assert.Equal("Borrar", second[1].ConfirmText);
    }

    [Fact]
    public void LaFilaDeUnJuegoBorradoNoResucitaEnElRefresco()
    {
        var cache = new CatalogRowCache();
        var first = cache.Build(
            [CreatePackage("quake3", "1.32"), CreatePackage("doom", "1.9")],
            [new Game("quake3", "Quake III Arena"), new Game("doom", "Doom")]);
        first[1].ConfirmText = "¿Seguro?";

        // Doom se borra del catalogo.
        var second = cache.Build(
            [CreatePackage("quake3", "1.32")],
            [new Game("quake3", "Quake III Arena")]);

        Assert.Single(second);
        Assert.Equal("quake3", second[0].GameId);

        // Y si vuelve a publicarse, la fila nueva viene con la confirmacion en estado normal.
        var third = cache.Build(
            [CreatePackage("quake3", "1.32"), CreatePackage("doom", "1.9")],
            [new Game("quake3", "Quake III Arena"), new Game("doom", "Doom")]);

        Assert.Equal(2, third.Length);
        Assert.Equal("Borrar", third[1].ConfirmText);
        Assert.NotSame(first[1], third[1]);
    }

    [Fact]
    public void QuitarUnJuegoDescartaSuFila()
    {
        var cache = new CatalogRowCache();
        var packages = new[] { CreatePackage("quake3", "1.32") };
        var games = new[] { new Game("quake3", "Quake III Arena") };

        var first = cache.Build(packages, games);
        first[0].ConfirmText = "¿Seguro?";

        cache.Remove("quake3");

        var second = cache.Build(packages, games);
        Assert.NotSame(first[0], second[0]);
        Assert.Equal("Borrar", second[0].ConfirmText);
    }

    [Fact]
    public void LaFilaNotificaLosCambiosDeSusPropiedades()
    {
        var row = new CatalogRow("quake3", "Quake III Arena", "1.32", 1, 10);
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.Update("Quake III Arena Remastered", "1.33", 5, 900);

        Assert.Contains(nameof(CatalogRow.Title), changed);
        Assert.Contains(nameof(CatalogRow.Version), changed);
        Assert.Contains(nameof(CatalogRow.FileCount), changed);
        Assert.Contains(nameof(CatalogRow.TotalSizeBytes), changed);
    }
}
