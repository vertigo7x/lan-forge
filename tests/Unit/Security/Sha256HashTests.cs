using System.Text;
using LanForge.Shared.Security;

namespace LanForge.UnitTests.Security;

public sealed class Sha256HashTests
{
    [Fact]
    public void ComputeHex_KnownVector()
    {
        var hash = Sha256Hash.ComputeHex("abc"u8);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public async Task ComputeHexAsync_Stream_MatchesBytesOverload()
    {
        var data = Encoding.UTF8.GetBytes("lan-forge");
        using var stream = new MemoryStream(data);

        var hash = await Sha256Hash.ComputeHexAsync(stream);

        Assert.Equal(Sha256Hash.ComputeHex(data), hash);
    }
}
