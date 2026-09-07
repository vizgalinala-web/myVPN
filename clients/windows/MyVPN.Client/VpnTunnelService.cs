using System.Diagnostics;

namespace MyVPN.Client;

public interface IProcessRunner
{
    int Run(string fileName, IReadOnlyList<string> arguments, out string standardOutput, out string standardError);
}

public sealed class ProcessRunner : IProcessRunner
{
    public int Run(string fileName, IReadOnlyList<string> arguments, out string standardOutput, out string standardError)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            standardOutput = string.Empty;
            standardError = $"Failed to start {fileName}";
            return 1;
        }

        standardOutput = process.StandardOutput.ReadToEnd();
        standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}

public sealed record TunnelApplyResult(int ExitCode, string DisplayCommand, string StandardOutput, string StandardError, bool WireGuardMissing);

public sealed class VpnTunnelService
{
    private readonly WireGuardLocator _locator;
    private readonly IProcessRunner _runner;
    private readonly bool _windows;

    public VpnTunnelService(WireGuardLocator? locator = null, IProcessRunner? runner = null, bool? windows = null)
    {
        _windows = windows ?? OperatingSystem.IsWindows();
        _locator = locator ?? new WireGuardLocator(windows: _windows);
        _runner = runner ?? new ProcessRunner();
    }

    public TunnelApplyResult Apply(string configPath, bool up, bool dryRun)
    {
        var full = Path.GetFullPath(configPath);
        if (up && !File.Exists(full))
        {
            return new TunnelApplyResult(1, string.Empty, string.Empty, $"Config not found: {full}. Run connect or save-config first.", false);
        }

        var install = _locator.Find();
        if (install is null)
        {
            return new TunnelApplyResult(
                2,
                string.Empty,
                string.Empty,
                WireGuardTunnelPlanner.MissingToolMessage(_windows) + $"{Environment.NewLine}# config={full}",
                true);
        }

        var command = up
            ? WireGuardTunnelPlanner.PlanUp(install, full)
            : WireGuardTunnelPlanner.PlanDown(install, full);

        if (dryRun)
        {
            return new TunnelApplyResult(0, command.Display, command.Display, string.Empty, false);
        }

        var code = _runner.Run(command.FileName, command.Arguments, out var stdout, out var stderr);
        if (code == 0 && up && _windows)
        {
            stderr = string.IsNullOrWhiteSpace(stderr)
                ? "# Enable Kill Switch in WireGuard for Windows: Block untunneled traffic."
                : stderr + Environment.NewLine + "# Enable Kill Switch in WireGuard for Windows: Block untunneled traffic.";
        }

        return new TunnelApplyResult(code, command.Display, stdout, stderr, false);
    }
}
