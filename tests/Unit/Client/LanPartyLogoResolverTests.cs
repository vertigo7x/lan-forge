using LanForge.Client.ViewModels;

namespace LanForge.UnitTests.Client;

public sealed class LanPartyLogoResolverTests
{
    private const string ValidName = "lan-party-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png";

    [Fact]
    public void TargetPath_confina_el_logo_en_el_directorio_de_branding()
    {
        var path = LanPartyLogoResolver.TargetPath(@"C:\LanForge\Client", ValidName);

        Assert.NotNull(path);
        Assert.Contains("branding", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(ValidName, path, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../lan-party-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png")]
    [InlineData("lan-party-short.png")]
    [InlineData("logo.png")]
    [InlineData("lan-party-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.gif")]
    [InlineData(null)]
    public void TargetPath_rechaza_nombres_no_anunciados_por_el_servidor(string? fileName)
    {
        Assert.Null(LanPartyLogoResolver.TargetPath("C:\\LanForge\\Client", fileName));
    }
}
