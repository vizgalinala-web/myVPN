using System.Text.Json;
using WgPeerSync;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

var dir = args[0];
var iface = "wg0";
var apply = false;
var watch = false;
string? cachePath = null;

for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--apply":
            apply = true;
            break;
        case "--watch":
            watch = true;
            break;
        case "--interface" when i + 1 < args.Length:
            iface = args[++i];
            break;
        case "--cache" when i + 1 < args.Length:
            cachePath = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

if (!Directory.Exists(dir))
{
    Console.Error.WriteLine($"Directory not found: {dir}");
    return 1;
}

if (!PeerSyncPlanner.IsSafeInterfaceName(iface))
{
    Console.Error.WriteLine($"Unsafe interface name: {iface}");
    return 1;
}

cachePath ??= Path.Combine(dir, ".wg-peer-sync.cache.json");

IWgCommandExecutor executor;
if (apply)
{
    if (Environment.GetEnvironmentVariable("MYVPN_WG_SYNC_ALLOW_APPLY") != "1")
    {
        Console.Error.WriteLine("Refusing --apply. Set MYVPN_WG_SYNC_ALLOW_APPLY=1 to enable (dangerous).");
        return 2;
    }

    executor = new ProcessWgCommandExecutor();
    Console.Error.WriteLine("# APPLY MODE: executing wg via Process (no shell)");
}
else
{
    executor = new PrintingWgCommandExecutor();
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (!watch)
{
    return await SyncOnceAsync(dir, iface, cachePath, executor, cts.Token);
}

Console.Error.WriteLine($"# watching {dir} (Ctrl+C to stop)");
var gate = new SemaphoreSlim(1, 1);
async Task TriggerAsync()
{
    if (!await gate.WaitAsync(0))
    {
        return;
    }

    try
    {
        await Task.Delay(250, cts.Token);
        await SyncOnceAsync(dir, iface, cachePath, executor, cts.Token);
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        gate.Release();
    }
}

await SyncOnceAsync(dir, iface, cachePath, executor, cts.Token);

using var watcher = new FileSystemWatcher(dir)
{
    Filter = "*.json",
    IncludeSubdirectories = false,
    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
};
watcher.Created += (_, _) => _ = TriggerAsync();
watcher.Changed += (_, _) => _ = TriggerAsync();
watcher.Deleted += (_, _) => _ = TriggerAsync();
watcher.Renamed += (_, _) => _ = TriggerAsync();
watcher.EnableRaisingEvents = true;

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
}

return 0;

static async Task<int> SyncOnceAsync(
    string dir,
    string iface,
    string cachePath,
    IWgCommandExecutor executor,
    CancellationToken ct)
{
    var desired = await ReadDesiredPeersAsync(dir, ct);
    var previous = await LoadCacheAsync(cachePath, ct);
    IReadOnlyList<WgCommand> commands;
    try
    {
        commands = PeerSyncPlanner.Plan(iface, desired, previous);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"# plan error: {ex.Message}");
        return 1;
    }

    foreach (var command in commands)
    {
        try
        {
            await executor.ExecuteAsync(command, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"# apply failed: {ex.Message}");
            return 4;
        }
    }

    await SaveCacheAsync(cachePath, desired.Keys.ToHashSet(StringComparer.Ordinal), ct);
    var removed = previous.Count(k => !desired.ContainsKey(k));
    Console.Error.WriteLine($"# sync desired={desired.Count} removed={removed} commands={commands.Count} cache={cachePath}");
    return 0;
}

static async Task<Dictionary<string, PeerState>> ReadDesiredPeersAsync(string dir, CancellationToken ct)
{
    var result = new Dictionary<string, PeerState>(StringComparer.Ordinal);
    foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
    {
        if (Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal))
        {
            continue;
        }

        try
        {
            await using var stream = File.OpenRead(file);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var publicKey = root.TryGetProperty("publicKey", out var pk) ? pk.GetString() : null;
            if (string.IsNullOrWhiteSpace(publicKey))
            {
                Console.Error.WriteLine($"# skip {file}: missing publicKey");
                continue;
            }

            if (!PeerSyncPlanner.IsValidPublicKey(publicKey))
            {
                Console.Error.WriteLine($"# skip {file}: invalid publicKey");
                continue;
            }

            var allowedIps = root.TryGetProperty("allowedIps", out var a) ? a.GetString() : null;
            result[publicKey] = new PeerState(publicKey, allowedIps);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"# skip {file}: {ex.Message}");
        }
    }

    return result;
}

static async Task<HashSet<string>> LoadCacheAsync(string path, CancellationToken ct)
{
    if (!File.Exists(path))
    {
        return new HashSet<string>(StringComparer.Ordinal);
    }

    try
    {
        await using var stream = File.OpenRead(path);
        var keys = await JsonSerializer.DeserializeAsync<string[]>(stream, cancellationToken: ct) ?? [];
        return keys.Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet(StringComparer.Ordinal);
    }
    catch (Exception ex) when (ex is IOException or JsonException)
    {
        Console.Error.WriteLine($"# cache unreadable ({ex.Message}); treating as empty");
        return new HashSet<string>(StringComparer.Ordinal);
    }
}

static async Task SaveCacheAsync(string path, HashSet<string> keys, CancellationToken ct)
{
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var tmp = path + ".tmp";
    await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(keys.OrderBy(k => k, StringComparer.Ordinal).ToArray()), ct);
    File.Move(tmp, path, overwrite: true);
}

static void PrintHelp()
{
    Console.WriteLine("""
wg-peer-sync — reads MyVPN peer-state JSON and syncs WireGuard peers (Phase 6)

Usage:
  wg-peer-sync <peer-state-dir> [--interface wg0] [--cache <path>] [--watch] [--apply]

Default is dry-run (prints commands). Tracks previously seen public keys in a cache
file so deleted peer JSON files emit `wg set ... remove`.

  --watch   re-run on directory changes until Ctrl+C
  --cache   override cache path (default: <dir>/.wg-peer-sync.cache.json)
  --apply   execute wg via Process (no shell). Requires MYVPN_WG_SYNC_ALLOW_APPLY=1
            Optional MYVPN_WG_BIN overrides the wg binary path.
""");
}
