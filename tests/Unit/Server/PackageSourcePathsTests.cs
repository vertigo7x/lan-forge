using LanForge.Server.Application;

namespace LanForge.UnitTests.Server;

/// <summary>
/// Pruebas de la conversion de la ruta absoluta del ejecutable (elegida con el
/// dialogo del sistema operativo) a la ruta relativa que espera el payload.
/// </summary>
public sealed class PackageSourcePathsTests
{
    [Theory]
    [InlineData("C:\\Juegos\\neon\\game.exe", true)]
    [InlineData("c:\\juegos\\neon\\game.EXE", true)]
    [InlineData("game.exe", true)]
    [InlineData("GAME.EXE", true)]
    [InlineData("game.dll", false)]
    [InlineData("game", false)]
    [InlineData("game.exe.bak", false)]
    [InlineData("", false)]
    public void IsExecutable_solo_acepta_la_extension_exe(string path, bool expected)
    {
        Assert.Equal(expected, PackageSourcePaths.IsExecutable(path));
    }

    [Fact]
    public void ToPackageRelativePath_devuelve_la_ruta_relativa_al_directorio_origen()
    {
        var relative = PackageSourcePaths.ToPackageRelativePath(
            @"C:\Juegos\neon",
            @"C:\Juegos\neon\game.exe");

        Assert.Equal("game.exe", relative);
    }

    [Fact]
    public void ToPackageRelativePath_acepta_un_subdirectorio()
    {
        var relative = PackageSourcePaths.ToPackageRelativePath(
            @"C:\Juegos\neon",
            @"C:\Juegos\neon\bin\game.exe");

        Assert.Equal("bin/game.exe", relative);
    }

    [Fact]
    public void ToPackageRelativePath_rechaza_un_ejecutable_fuera_de_la_carpeta_origen()
    {
        Assert.Throws<ArgumentException>(() => PackageSourcePaths.ToPackageRelativePath(
            @"C:\Juegos\neon",
            @"C:\Otros\game.exe"));
    }

    [Fact]
    public void ToPackageRelativePath_rechaza_un_ejecutable_que_no_termina_en_exe()
    {
        Assert.Throws<ArgumentException>(() => PackageSourcePaths.ToPackageRelativePath(
            @"C:\Juegos\neon",
            @"C:\Juegos\neon\game.dll"));
    }

    [Fact]
    public void ToPackageRelativePath_rechaza_una_carpeta_origen_vacia()
    {
        Assert.Throws<ArgumentException>(() => PackageSourcePaths.ToPackageRelativePath(
            string.Empty,
            @"C:\Juegos\neon\game.exe"));
    }
}
