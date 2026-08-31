using FluentAssertions;
using MyVPN.Client;

namespace MyVPN.Client.Tests;

public sealed class WireGuardKeyPairTests
{
    [Fact]
    public void Generate_ProducesDistinctValidBase64Keys()
    {
        var (priv, pub) = WireGuardKeyPair.Generate();

        priv.Should().HaveLength(44);
        pub.Should().HaveLength(44);
        priv.Should().NotBe(pub);
        WireGuardKeyPair.IsValidPublicKey(pub).Should().BeTrue();
        WireGuardKeyPair.IsValidPublicKey(priv).Should().BeTrue(); // also 32 raw bytes

        var again = WireGuardKeyPair.Generate();
        again.PublicKey.Should().NotBe(pub);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64")]
    [InlineData("YWJjZA==")] // too short
    [InlineData("ERERERERERERERERERERERERERERERERERERERERERE= ")] // whitespace
    public void IsValidPublicKey_RejectsInvalid(string key)
        => WireGuardKeyPair.IsValidPublicKey(key).Should().BeFalse();

    [Fact]
    public void IsValidPublicKey_AcceptsKnown32ByteKey()
        => WireGuardKeyPair.IsValidPublicKey("ERERERERERERERERERERERERERERERERERERERERERE=")
            .Should().BeTrue();
}
