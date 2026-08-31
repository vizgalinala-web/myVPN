using System.Text.Json;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("""
wg-peer-sync — reads MyVPN peer-state JSON and emits wg commands (Phase 4 helper)

Usage:
  wg-peer-sync <peer-state-dir> [--apply] [--interface wg0]

Default is dry-run (prints commands). --apply is reserved and currently refused
unless MYVPN_WG_SYNC_ALLOW_APPLY=1 is set (safety).
""");
    return 0;
}

var dir = args[0];
var iface = "wg0";
var apply = args.Contains("--apply");
for (var i = 1; i < args.Length - 1; i++)
{
    if (args[i] == "--interface")
    {
        iface = args[i + 1];
    }
}

if (!Directory.Exists(dir))
{
    Console.Error.WriteLine($"Directory not found: {dir}");
    return 1;
}

var files = Directory.GetFiles(dir, "*.json").OrderBy(f => f).ToArray();
if (files.Length == 0)
{
    Console.WriteLine("# No peer state files.");
    return 0;
}

foreach (var file in files)
{
    using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
    var root = doc.RootElement;
    var publicKey = root.GetProperty("publicKey").GetString();
    var allowedIps = root.TryGetProperty("allowedIps", out var a) ? a.GetString() : null;
    if (string.IsNullOrWhiteSpace(publicKey))
    {
        Console.Error.WriteLine($"# skip {file}: missing publicKey");
        continue;
    }

    var allowed = string.IsNullOrWhiteSpace(allowedIps) ? "0.0.0.0/32" : allowedIps;
    var cmd = $"wg set {iface} peer {publicKey} allowed-ips {allowed}";
    Console.WriteLine(cmd);
}

if (apply)
{
    if (Environment.GetEnvironmentVariable("MYVPN_WG_SYNC_ALLOW_APPLY") != "1")
    {
        Console.Error.WriteLine("Refusing --apply. Set MYVPN_WG_SYNC_ALLOW_APPLY=1 to enable (dangerous).");
        return 2;
    }

    Console.Error.WriteLine("# --apply execution is not implemented in this scaffold. Pipe dry-run output to a secure operator script.");
    return 3;
}

return 0;
