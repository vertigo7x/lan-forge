using LanForge.Shared.Security;

namespace LanForge.UnitTests.Security;

public sealed class FingerprintTests
{
    [Fact]
    public void FromPublicKey_SameKey_AlwaysSameFingerprint()
    {
        var keys = Ed25519.GenerateKeyPair();

        var a = Fingerprint.FromPublicKey(keys.PublicKey);
        var b = Fingerprint.FromPublicKey(keys.PublicKey);

        Assert.Equal(a, b);
    }

    [Fact]
    public void FromPublicKey_DifferentKeys_DifferentFingerprint()
    {
        var a = Fingerprint.FromPublicKey(Ed25519.GenerateKeyPair().PublicKey);
        var b = Fingerprint.FromPublicKey(Ed25519.GenerateKeyPair().PublicKey);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ToString_AndParse_RoundTrip()
    {
        var fingerprint = Fingerprint.FromPublicKey(Ed25519.GenerateKeyPair().PublicKey);

        var parsed = Fingerprint.Parse(fingerprint.ToString());

        Assert.Equal(fingerprint, parsed);
    }

    [Fact]
    public void ToString_IsGroupedLowercaseHex()
    {
        var fingerprint = Fingerprint.FromPublicKey(Ed25519.GenerateKeyPair().PublicKey);

        var text = fingerprint.ToString();

        Assert.Matches("^[0-9a-f]{8}(-[0-9a-f]{8}){7}$", text);
    }

    [Fact]
    public void Parse_AcceptsCompactHex()
    {
        var fingerprint = Fingerprint.FromPublicKey(Ed25519.GenerateKeyPair().PublicKey);
        var compact = fingerprint.ToString().Replace("-", string.Empty);

        Assert.Equal(fingerprint, Fingerprint.Parse(compact));
    }

    [Fact]
    public void Parse_NullOrEmpty_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => Fingerprint.Parse(""));
        Assert.ThrowsAny<ArgumentException>(() => Fingerprint.Parse("   "));
        Assert.ThrowsAny<ArgumentException>(() => Fingerprint.Parse(null!));
    }

    [Theory]
    [InlineData("zzzz")]
    [InlineData("a1b2")]
    [InlineData("a1b2c3d4-a1b2c3d4")] // demasiado corto
    public void Parse_Invalid_Throws(string text)
    {
        Assert.Throws<FormatException>(() => Fingerprint.Parse(text));
    }

    [Fact]
    public void FromPublicKey_WrongLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => Fingerprint.FromPublicKey(new byte[16]));
    }
}
