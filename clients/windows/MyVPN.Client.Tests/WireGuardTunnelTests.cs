using FluentAssertions;
using MyVPN.Client;

namespace MyVPN.Client.Tests;

public sealed class WireGuardTunnelTests
{
    [Fact]
    public void Locator_FindsWindowsHelper_WhenFileExists()
    {
        var exe = @"C:\Program Files\WireGuard\wireguard.exe";
        var locator = new WireGuardLocator(
            fileExists: p => p == exe,
            searchPaths: new[] { exe },
            windows: true);

        var found = locator.Find();
        found.Should().NotBeNull();
        found!.Kind.Should().Be(WireGuardKind.WindowsService);
        found.Executable.Should().Be(exe);
    }

    [Fact]
    public void Locator_FindsWgQuick_OnUnix()
    {
        var locator = new WireGuardLocator(
            fileExists: p => p == "/usr/bin/wg-quick",
            searchPaths: new[] { "/usr/bin/wg-quick" },
            windows: false);

        var found = locator.Find();
        found.Should().NotBeNull();
        found!.Kind.Should().Be(WireGuardKind.WgQuick);
    }

    [Fact]
    public void Locator_ReturnsNull_WhenMissing()
    {
        var locator = new WireGuardLocator(
            fileExists: _ => false,
            searchPaths: new[] { "/usr/bin/wg-quick" },
            windows: false);

        locator.Find().Should().BeNull();
    }

    [Fact]
    public void PlanUp_Windows_InstallsServiceFromConfigPath()
    {
        var install = new WireGuardInstallation(WireGuardKind.WindowsService, @"C:\Program Files\WireGuard\wireguard.exe");
        var conf = Path.Combine(Path.GetTempPath(), "myvpn.conf");
        var cmd = WireGuardTunnelPlanner.PlanUp(install, conf);

        cmd.TunnelName.Should().Be("myvpn");
        cmd.Arguments.Should().Equal("/installtunnelservice", Path.GetFullPath(conf));
        cmd.Display.Should().Contain("/installtunnelservice");
        cmd.Display.Should().NotContain("cmd.exe");
    }

    [Fact]
    public void PlanDown_Windows_UninstallsByTunnelName()
    {
        var install = new WireGuardInstallation(WireGuardKind.WindowsService, @"C:\Program Files\WireGuard\wireguard.exe");
        var conf = Path.Combine(Path.GetTempPath(), "myvpn.conf");
        var cmd = WireGuardTunnelPlanner.PlanDown(install, conf);

        cmd.Arguments.Should().Equal("/uninstalltunnelservice", "myvpn");
    }

    [Fact]
    public void PlanUp_WgQuick_UsesUpAndConfigPath()
    {
        var install = new WireGuardInstallation(WireGuardKind.WgQuick, "/usr/bin/wg-quick");
        var conf = Path.Combine(Path.GetTempPath(), "myvpn.conf");
        var cmd = WireGuardTunnelPlanner.PlanUp(install, conf);

        cmd.Arguments.Should().Equal("up", Path.GetFullPath(conf));
    }

    [Fact]
    public void PlanDown_WgQuick_UsesDownAndConfigPath()
    {
        var install = new WireGuardInstallation(WireGuardKind.WgQuick, "/usr/bin/wg-quick");
        var conf = Path.Combine(Path.GetTempPath(), "myvpn.conf");
        var cmd = WireGuardTunnelPlanner.PlanDown(install, conf);

        cmd.Arguments.Should().Equal("down", Path.GetFullPath(conf));
    }

    [Theory]
    [InlineData("bad name.conf")]
    [InlineData("myvpn$.conf")]
    public void TunnelName_RejectsUnsafeFileNames(string fileName)
    {
        var act = () => WireGuardTunnelPlanner.TunnelNameFromConfig(fileName);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MissingToolMessage_DoesNotIncludeSecrets()
    {
        WireGuardTunnelPlanner.MissingToolMessage(windows: true).Should().Contain("wireguard.com/install");
        WireGuardTunnelPlanner.MissingToolMessage(windows: false).Should().Contain("wg-quick");
    }
}
