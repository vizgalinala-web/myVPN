using FluentAssertions;
using WgPeerSync;

namespace WgPeerSync.Tests;

public sealed class PeerSyncPlannerTests
{
    private const string KeyA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string KeyB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB=";

    [Fact]
    public void Plan_EmitsSetThenRemove_InStableOrder()
    {
        var desired = new Dictionary<string, PeerState>(StringComparer.Ordinal)
        {
            [KeyA] = new PeerState(KeyA, "10.8.0.2/32")
        };
        var previous = new HashSet<string>(StringComparer.Ordinal) { KeyA, KeyB };

        var commands = PeerSyncPlanner.Plan("wg0", desired, previous);

        commands.Should().HaveCount(2);
        commands[0].ToString().Should().Be($"wg set wg0 peer {KeyA} allowed-ips 10.8.0.2/32");
        commands[1].ToString().Should().Be($"wg set wg0 peer {KeyB} remove");
        commands[0].ToArgumentList().Should().NotContain(a => a.Contains(' '));
    }

    [Fact]
    public void Plan_RejectsUnsafeInterface()
    {
        var act = () => PeerSyncPlanner.Plan("wg0;rm", new Dictionary<string, PeerState>(), new HashSet<string>());
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("wg0", true)]
    [InlineData("wg-vpn1", true)]
    [InlineData("wg0;id", false)]
    [InlineData("", false)]
    public void IsSafeInterfaceName(string name, bool expected)
        => PeerSyncPlanner.IsSafeInterfaceName(name).Should().Be(expected);

    [Fact]
    public void IsValidPublicKey_Accepts32ByteBase64()
        => PeerSyncPlanner.IsValidPublicKey(KeyA).Should().BeTrue();
}
