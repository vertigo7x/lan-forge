using LanForge.Shared.Models;

namespace LanForge.UnitTests.Models;

public sealed class PackageVersionTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4")]
    [InlineData("1.0-beta")]
    [InlineData("2.0.0-rc.1")]
    public void ValidVersions_Accepted(string value)
    {
        Assert.True(PackageVersion.IsValid(value));
        _ = new PackageVersion(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1.2.x")]
    [InlineData("01.2")]
    [InlineData("1.2.3-")]
    [InlineData("v1.2")]
    public void InvalidVersions_Rejected(string? value)
    {
        Assert.False(PackageVersion.IsValid(value));
        Assert.Throws<ArgumentException>(() => new PackageVersion(value!));
    }

    [Fact]
    public void CompareTo_NumericNotLexicographic()
    {
        Assert.True(new PackageVersion("1.10.0") > new PackageVersion("1.2.0"));
    }

    [Fact]
    public void CompareTo_ReleaseGreaterThanPrerelease()
    {
        Assert.True(new PackageVersion("1.0.0") > new PackageVersion("1.0.0-beta"));
        Assert.True(new PackageVersion("1.0.0-beta") < new PackageVersion("1.0.0"));
    }

    [Fact]
    public void CompareTo_PaddingTreatedAsZero()
    {
        Assert.Equal(0, new PackageVersion("1.2").CompareTo(new PackageVersion("1.2.0.0")));
    }

    [Fact]
    public void TryParse_Invalid_ReturnsFalse()
    {
        Assert.False(PackageVersion.TryParse("nope", out var version));
        Assert.Null(version);
    }
}
