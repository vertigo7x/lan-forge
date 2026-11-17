using LanForge.Client.ViewModels;

namespace LanForge.UnitTests.Client;

public sealed class CoverResolverTests
{
    private static readonly string[] Assets =
    [
        "avares://LanForge.Client/Assets/cover-neon-drift.png",
        "avares://LanForge.Client/Assets/cover-starfall-arena.png",
    ];

    [Fact]
    public void CachedCoverPath_devuelve_null_si_no_existe()
    {
        var dir = Directory.CreateTempSubdirectory("lanforge-cover").FullName;
        try
        {
            Assert.Null(CoverResolver.CachedCoverPath(dir, "juego.png"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CachedCoverPath_devuelve_la_ruta_si_existe()
    {
        var dir = Directory.CreateTempSubdirectory("lanforge-cover").FullName;
        try
        {
            var covers = CoverResolver.CoversDirectory(dir);
            Directory.CreateDirectory(covers);
            var file = Path.Combine(covers, "juego.png");
            File.WriteAllText(file, "x");

            var path = CoverResolver.CachedCoverPath(dir, "juego.png");
            Assert.NotNull(path);
            Assert.Equal(file, path);
            Assert.StartsWith("file://", CoverResolver.CachedCoverUri(dir, "juego.png"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("sub/carpeta.png")]
    [InlineData("no-es-imagen.txt")]
    [InlineData("")]
    [InlineData(null)]
    public void CachedCoverPath_rechaza_nombres_no_validos(string? fileName)
    {
        var dir = Directory.CreateTempSubdirectory("lanforge-cover").FullName;
        try
        {
            Assert.Null(CoverResolver.CachedCoverPath(dir, fileName));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CoverTargetPath_devuelve_la_ruta_aunque_el_fichero_no_exista()
    {
        var dir = Directory.CreateTempSubdirectory("lanforge-cover").FullName;
        try
        {
            var path = CoverResolver.CoverTargetPath(dir, "juego.png");
            Assert.NotNull(path);
            Assert.Equal(Path.Combine(CoverResolver.CoversDirectory(dir), "juego.png"), path);
            // Todavia no esta descargada: la version cacheada sigue siendo null.
            Assert.Null(CoverResolver.CachedCoverPath(dir, "juego.png"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FallbackAsset_es_estable_para_el_mismo_juego()
    {
        var first = CoverResolver.FallbackAsset("neon-drift", Assets);
        var second = CoverResolver.FallbackAsset("neon-drift", Assets);
        Assert.Equal(first, second);
        Assert.Contains(first, Assets);
    }

    [Fact]
    public void FallbackAsset_falla_con_lista_vacia()
    {
        Assert.Throws<ArgumentException>(() => CoverResolver.FallbackAsset("x", Array.Empty<string>()));
    }
}
