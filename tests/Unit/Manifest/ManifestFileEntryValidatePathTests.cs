using LanForge.Shared.Manifest;
using LanForge.Shared.Security;

namespace LanForge.UnitTests.Manifest;

/// <summary>
/// Reglas de seguridad de <see cref="ManifestFileEntry.ValidatePath"/>: solo admite
/// rutas relativas dentro del paquete. Una ruta absoluta (raiz de Unix, UNC o
/// unidad con letra) no es una ruta relativa, aunque se normalice quitando la
/// barra inicial: normalizarla no la convierte en relativa.
/// </summary>
public sealed class ManifestFileEntryValidatePathTests
{
    [Fact]
    public void ValidatePath_rechaza_una_ruta_absoluta_unix()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ManifestFileEntry.ValidatePath("/tmp/game.exe"));

        Assert.Equal("relativePath", exception.ParamName);
    }

    [Fact]
    public void ValidatePath_rechaza_una_ruta_absoluta_unix_con_subdirectorios()
    {
        Assert.Throws<ArgumentException>(
            () => ManifestFileEntry.ValidatePath("/tmp/juegos/game.exe"));
    }

    [Fact]
    public void ValidatePath_rechaza_una_ruta_absoluta_unix_con_barra_inicial_doblada()
    {
        // '//tmp/game.exe' es una ruta absoluta Unix con la barra inicial repetida.
        Assert.Throws<ArgumentException>(
            () => ManifestFileEntry.ValidatePath("//tmp/game.exe"));
    }

    [Fact]
    public void ValidatePath_rechaza_una_ruta_unc_absoluta()
    {
        // Una ruta UNC es absoluta tambien, aunque use barra invertida.
        Assert.Throws<ArgumentException>(
            () => ManifestFileEntry.ValidatePath(@"\\servidor\compartido\game.exe"));
    }

    [Fact]
    public void ValidatePath_rechaza_una_ruta_absoluta_con_espacios_alrededor()
    {
        // Los espacios no pueden esconder la barra inicial: la ruta sigue siendo absoluta.
        Assert.Throws<ArgumentException>(
            () => ManifestFileEntry.ValidatePath("  /tmp/game.exe  "));
    }

    [Fact]
    public void ValidatePath_rechaza_una_raiz_absoluta_vacia()
    {
        // '/' se normalizaba a la cadena vacia, que no es una ruta de archivo.
        Assert.Throws<ArgumentException>(
            () => ManifestFileEntry.ValidatePath("/"));
    }

    [Theory]
    [InlineData("game.exe")]
    [InlineData("bin/game.exe")]
    [InlineData("bin/sub/game.exe")]
    public void ValidatePath_acepta_una_ruta_relativa(string ruta)
    {
        Assert.Equal(ruta, ManifestFileEntry.ValidatePath(ruta));
    }

    [Theory]
    [InlineData("../game.exe")]
    [InlineData("bin/../game.exe")]
    [InlineData("..")]
    public void ValidatePath_sigue_rechazando_el_salto_de_directorio(string ruta)
    {
        // El guardia de '..' no cambia con este arreglo.
        Assert.Throws<ArgumentException>(() => ManifestFileEntry.ValidatePath(ruta));
    }

    [Theory]
    [InlineData("C:\\Juegos\\game.exe")]
    [InlineData("C:/Juegos/game.exe")]
    public void ValidatePath_sigue_rechazando_una_unidad_con_letra(string ruta)
    {
        // El guardia de ':' no cambia con este arreglo.
        Assert.Throws<ArgumentException>(() => ManifestFileEntry.ValidatePath(ruta));
    }

    [Fact]
    public void ValidatePath_normaliza_los_separadores_de_la_ruta_relativa()
    {
        // La normalizacion existente no se rompe: la barra invertida pasa a '/'.
        Assert.Equal("bin/game.exe", ManifestFileEntry.ValidatePath(@"bin\game.exe"));
    }

    [Fact]
    public void Un_manifiesto_ya_firmado_sigue_verificando()
    {
        // El cambio afecta a que rutas se aceptan, no a como se serializa una
        // ruta relativa. Los bytes canonicos firmados no cambian, asi que un
        // manifiesto publicado antes sigue verificando en el cliente nuevo.
        var keys = Ed25519.GenerateKeyPair();
        var entry = ManifestFileEntry.Create("tmp/game.exe", new string('a', 64), 1);
        var payload = PackageManifestPayload.Create(
            "warcraft3", "warcraft3", "Warcraft III", "1.0.0",
            new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero),
            Convert.ToBase64String(keys.PublicKey),
            [entry],
            entryPoint: "tmp/game.exe");

        var signed = ManifestSigner.Sign(payload, keys);

        Assert.Equal("tmp/game.exe", signed.EntryPoint);
        Assert.True(ManifestVerifier.Verify(signed));
    }
}
