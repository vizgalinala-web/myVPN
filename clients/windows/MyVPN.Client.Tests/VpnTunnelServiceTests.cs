using FluentAssertions;
using MyVPN.Client;

namespace MyVPN.Client.Tests;

public sealed class VpnTunnelServiceTests
{
    private sealed class FakeRunner : IProcessRunner
    {
        public string? FileName { get; private set; }
        public IReadOnlyList<string>? Arguments { get; private set; }
        public int ExitCode { get; set; }

        public int Run(string fileName, IReadOnlyList<string> arguments, out string standardOutput, out string standardError)
        {
            FileName = fileName;
            Arguments = arguments;
            standardOutput = "ok";
            standardError = string.Empty;
            return ExitCode;
        }
    }

    [Fact]
    public void Apply_Up_MissingConfig_Returns1()
    {
        var service = new VpnTunnelService(
            locator: new WireGuardLocator(_ => false, new[] { "/usr/bin/wg-quick" }, windows: false),
            runner: new FakeRunner(),
            windows: false);

        var result = service.Apply(Path.Combine(Path.GetTempPath(), "no-such-myvpn.conf"), up: true, dryRun: false);
        result.ExitCode.Should().Be(1);
        result.StandardError.Should().Contain("Config not found");
    }

    [Fact]
    public void Apply_Up_MissingWireGuard_Returns2()
    {
        var conf = Path.GetTempFileName();
        try
        {
            File.WriteAllText(conf, "[Interface]\n");
            var named = Path.Combine(Path.GetDirectoryName(conf)!, "myvpn.conf");
            File.Move(conf, named, overwrite: true);
            var service = new VpnTunnelService(
                locator: new WireGuardLocator(_ => false, new[] { "/usr/bin/wg-quick" }, windows: false),
                runner: new FakeRunner(),
                windows: false);

            var result = service.Apply(named, up: true, dryRun: false);
            result.ExitCode.Should().Be(2);
            result.WireGuardMissing.Should().BeTrue();
        }
        finally
        {
            // temp file may have been renamed
        }
    }

    [Fact]
    public void Apply_Up_DryRun_DoesNotCallRunner()
    {
        var runner = new FakeRunner();
        var exe = "/usr/bin/wg-quick";
        var conf = Path.Combine(Path.GetTempPath(), "myvpn.conf");
        File.WriteAllText(conf, "[Interface]\n");
        var service = new VpnTunnelService(
            locator: new WireGuardLocator(p => p == exe, new[] { exe }, windows: false),
            runner: runner,
            windows: false);

        var result = service.Apply(conf, up: true, dryRun: true);
        result.ExitCode.Should().Be(0);
        runner.FileName.Should().BeNull();
        result.DisplayCommand.Should().Contain("wg-quick");
    }

    [Fact]
    public void Apply_Up_RunsWgQuickWithoutShell()
    {
        var runner = new FakeRunner { ExitCode = 0 };
        var exe = "/usr/bin/wg-quick";
        var conf = Path.Combine(Path.GetTempPath(), "myvpn.conf");
        File.WriteAllText(conf, "[Interface]\n");
        var service = new VpnTunnelService(
            locator: new WireGuardLocator(p => p == exe, new[] { exe }, windows: false),
            runner: runner,
            windows: false);

        var result = service.Apply(conf, up: true, dryRun: false);
        result.ExitCode.Should().Be(0);
        runner.FileName.Should().Be(exe);
        runner.Arguments.Should().Equal("up", Path.GetFullPath(conf));
    }
}

public sealed class DesktopVpnControllerTests
{
    [Fact]
    public void CanConnect_RequiresApiEmailPassword()
    {
        var controller = new DesktopVpnController(
            tunnels: new VpnTunnelService(
                new WireGuardLocator(_ => false, new[] { "/usr/bin/wg-quick" }, false),
                new VpnTunnelServiceTests_Runner(),
                false));

        controller.CanConnect.Should().BeFalse();
        controller.Api = "http://127.0.0.1:5212/";
        controller.Email = "user@example.com";
        controller.Password = "CorrectHorseBatteryStaple!";
        controller.CanConnect.Should().BeTrue();
        controller.Password.Should().NotBeNullOrEmpty();
        controller.KillSwitchHint.Should().Contain("Block untunneled traffic");
        controller.Status.Should().NotContain("lorem");
    }
}

internal sealed class VpnTunnelServiceTests_Runner : IProcessRunner
{
    public int Run(string fileName, IReadOnlyList<string> arguments, out string standardOutput, out string standardError)
    {
        standardOutput = string.Empty;
        standardError = string.Empty;
        return 0;
    }
}
