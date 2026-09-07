using FluentAssertions;
using MyVPN.Client;

namespace MyVPN.Client.Tests;

public sealed class ConfigBuilderTests
{
    [Fact]
    public void BuildLocalQuickConfig_InsertsPrivateKeyAndPeerFields()
    {
        var config = new DeviceVpnConfigurationResponse(
            DeviceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ServerId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ServerName: "Germany 1",
            Address: "10.8.0.2/32",
            Dns: "1.1.1.1",
            Peer: new WireGuardPeerDto(
                PublicKey: "ERERERERERERERERERERERERERERERERERERERERERE=",
                Endpoint: "de1.example.com:51820",
                AllowedIps: "0.0.0.0/0, ::/0",
                PersistentKeepalive: 25),
            WireGuardQuickConfig: "unused-template");

        var quick = MyVpnApiClient.BuildLocalQuickConfig(config, "PRIVATE_KEY_PLACEHOLDER");

        quick.Should().Contain("PrivateKey = PRIVATE_KEY_PLACEHOLDER");
        quick.Should().Contain("Address = 10.8.0.2/32");
        quick.Should().Contain("DNS = 1.1.1.1");
        quick.Should().Contain("PublicKey = ERERERERERERERERERERERERERERERERERERERERERE=");
        quick.Should().Contain("Endpoint = de1.example.com:51820");
        quick.Should().Contain("AllowedIPs = 0.0.0.0/0, ::/0");
        quick.Should().Contain("PersistentKeepalive = 25");
        quick.Should().Contain("Block untunneled traffic");
        quick.Should().NotContain("\r");
    }
}
