using LanForge.Client.Infrastructure;

namespace LanForge.UnitTests.Client;

public sealed class TxtPropertiesTests
{
    [Fact]
    public void Parse_ExtractsKeyValuePairs()
    {
        var properties = TxtProperties.Parse(["name=Servidor", "fingerprint=abc-123", "novalor", "=vacio", "k="]);

        Assert.Equal("Servidor", properties["name"]);
        Assert.Equal("abc-123", properties["fingerprint"]);
        Assert.Equal(3, properties.Count);
        Assert.Equal(string.Empty, properties["k"]);
    }
}
