using LanForge.Shared.Protocol;

namespace LanForge.UnitTests.Protocol;

public sealed class ProtocolCompatibilityTests
{
    [Fact]
    public void SameVersion_IsCompatible()
    {
        Assert.True(ProtocolVersion.IsCompatible(ProtocolVersion.Current));
    }

    [Theory]
    [InlineData(ProtocolVersion.Current - 1)]
    [InlineData(ProtocolVersion.Current + 1)]
    public void DifferentVersion_IsNotCompatible(int serverVersion)
    {
        Assert.False(ProtocolVersion.IsCompatible(serverVersion));
    }
}
