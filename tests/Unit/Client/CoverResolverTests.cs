using LanForge.Client.ViewModels;

namespace LanForge.UnitTests.Client;

public sealed class CoverResolverTests
{
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

    [Theory]
    [InlineData(@"..\escape.png")]
    [InlineData(@"sub\carpeta.png")]
    [InlineData(@"C:\absoluta.png")]
    [InlineData(@"juego.png\..\otro.png")]
    [InlineData(@"..\..\..\Windows\System32\config.png")]
    public void CoverResolver_rechaza_las_formas_de_ruta_de_windows(string? fileName)
    {
        // El producto es Windows x64, pero la validacion del nombre es por patron:
        // se comprueba aqui con formas de ruta de Windows sin depender del host Linux.
        var dir = Directory.CreateTempSubdirectory("lanforge-cover").FullName;
        try
        {
            Assert.Null(CoverResolver.CoverTargetPath(dir, fileName));
            Assert.Null(CoverResolver.CachedCoverPath(dir, fileName));
            Assert.Null(CoverResolver.CachedCoverUri(dir, fileName));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CoverTargetPath_confina_la_portada_dentro_del_directorio_de_covers()
    {
        // Con un directorio de datos con forma Windows el resultado sigue dentro de "covers".
        var dataDirectory = @"C:\Users\Ana\AppData\Roaming\LanForge\Client";
        var path = CoverResolver.CoverTargetPath(dataDirectory, "juego.png");

        Assert.NotNull(path);
        Assert.EndsWith("juego.png", path, StringComparison.Ordinal);
        Assert.Contains("covers", path, StringComparison.Ordinal);
        // El nombre plano no puede escalar: ni separadores ni "..".
        Assert.DoesNotContain("..", path, StringComparison.Ordinal);
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

}
