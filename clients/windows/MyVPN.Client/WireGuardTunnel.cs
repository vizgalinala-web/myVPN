using System.Text.RegularExpressions;

namespace MyVPN.Client;

public enum WireGuardKind
{
    WindowsService,
    WgQuick
}

public sealed record WireGuardInstallation(WireGuardKind Kind, string Executable);

public sealed record WireGuardCommand(string FileName, IReadOnlyList<string> Arguments, string TunnelName)
{
    public string Display =>
        Arguments.Count == 0
            ? Quote(FileName)
            : $"{Quote(FileName)} {string.Join(' ', Arguments.Select(Quote))}";

    private static string Quote(string value)
        => value.Contains(' ') ? $"\"{value}\"" : value;
}

/// <summary>
/// Locates a local WireGuard control tool (Windows service helper or wg-quick).
/// Does not spawn a shell.
/// </summary>
public sealed class WireGuardLocator
{
    private readonly Func<string, bool> _fileExists;
    private readonly IReadOnlyList<string> _searchPaths;
    private readonly bool _windows;

    public WireGuardLocator(
        Func<string, bool>? fileExists = null,
        IEnumerable<string>? searchPaths = null,
        bool? windows = null)
    {
        _fileExists = fileExists ?? File.Exists;
        _windows = windows ?? OperatingSystem.IsWindows();
        _searchPaths = (searchPaths ?? DefaultSearchPaths(_windows)).ToList();
    }

    public WireGuardInstallation? Find()
    {
        foreach (var path in _searchPaths)
        {
            if (string.IsNullOrWhiteSpace(path) || !_fileExists(path))
            {
                continue;
            }

            var kind = _windows || LooksLikeWindowsHelper(path)
                ? WireGuardKind.WindowsService
                : WireGuardKind.WgQuick;
            if (LooksLikeWgQuick(path))
            {
                kind = WireGuardKind.WgQuick;
            }

            return new WireGuardInstallation(kind, path);
        }

        return null;
    }

    public static IReadOnlyList<string> DefaultSearchPaths(bool windows)
    {
        var paths = new List<string>();
        if (windows)
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(pf))
            {
                paths.Add(Path.Combine(pf, "WireGuard", "wireguard.exe"));
            }

            if (!string.IsNullOrWhiteSpace(pf86))
            {
                paths.Add(Path.Combine(pf86, "WireGuard", "wireguard.exe"));
            }
        }
        else
        {
            paths.Add("/usr/bin/wg-quick");
            paths.Add("/usr/local/bin/wg-quick");
            paths.Add("/opt/homebrew/bin/wg-quick");
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            paths.Add(Path.Combine(dir, windows ? "wireguard.exe" : "wg-quick"));
        }

        return paths;
    }

    private static bool LooksLikeWindowsHelper(string path)
        => path.EndsWith("wireguard.exe", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeWgQuick(string path)
        => Path.GetFileName(path).Equals("wg-quick", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Builds argv for bringing a saved .conf up/down. Never uses a shell.
/// </summary>
public static class WireGuardTunnelPlanner
{
    private static readonly Regex TunnelNamePattern = new("^[A-Za-z0-9_=+.-]+$", RegexOptions.Compiled);

    public static string DefaultConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(home, "MyVPN", "myvpn.conf");
        }

        return Path.Combine(home, ".myvpn", "myvpn.conf");
    }

    public static string TunnelNameFromConfig(string configPath)
    {
        var name = Path.GetFileNameWithoutExtension(configPath);
        if (string.IsNullOrWhiteSpace(name) || !TunnelNamePattern.IsMatch(name))
        {
            throw new ArgumentException(
                "Tunnel name (config file name without extension) must match [A-Za-z0-9_=+.-]+.",
                nameof(configPath));
        }

        return name;
    }

    public static WireGuardCommand PlanUp(WireGuardInstallation install, string configPath)
    {
        var full = Path.GetFullPath(configPath);
        var name = TunnelNameFromConfig(full);
        return install.Kind switch
        {
            WireGuardKind.WindowsService => new WireGuardCommand(
                install.Executable,
                new[] { "/installtunnelservice", full },
                name),
            WireGuardKind.WgQuick => new WireGuardCommand(
                install.Executable,
                new[] { "up", full },
                name),
            _ => throw new ArgumentOutOfRangeException(nameof(install))
        };
    }

    public static WireGuardCommand PlanDown(WireGuardInstallation install, string configPath)
    {
        var full = Path.GetFullPath(configPath);
        var name = TunnelNameFromConfig(full);
        return install.Kind switch
        {
            WireGuardKind.WindowsService => new WireGuardCommand(
                install.Executable,
                new[] { "/uninstalltunnelservice", name },
                name),
            WireGuardKind.WgQuick => new WireGuardCommand(
                install.Executable,
                new[] { "down", full },
                name),
            _ => throw new ArgumentOutOfRangeException(nameof(install))
        };
    }

    public static string MissingToolMessage(bool windows)
        => windows
            ? "WireGuard for Windows was not found. Install it from https://www.wireguard.com/install/ then re-run. Until then, import the .conf in the WireGuard app and enable Block untunneled traffic."
            : "wg-quick was not found. Install wireguard-tools, then re-run: wg-quick up <file.conf>.";
}
