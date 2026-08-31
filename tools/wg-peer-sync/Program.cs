using System.Text.Json;

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

cachePath ??= Path.Combine(dir, ".wg-peer-sync.cache.json");

if (apply && Environment.GetEnvironmentVariable("MYVPN_WG_SYNC_ALLOW_APPLY") != "1")
{
    Console.Error.WriteLine("Refusing --apply. Set MYVPN_WG_SYNC_ALLOW_APPLY=1 to enable (dangerous).");
    return 2;
}

if (apply)
{
    Console.Error.WriteLine("# --apply execution is not implemented. Pipe dry-run output to a secure operator script.");
    return 3;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

if (!watch)
{
    return await SyncOnceAsync(dir, iface, cachePath, cts.Token);
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
        // Debounce bursts of file events.
        await Task.Delay(250, cts.Token);
        await SyncOnceAsync(dir, iface, cachePath, cts.Token);
    }
    catch (OperationCanceledException)
    {
        // shutting down
    }
    finally
    {
        gate.Release();
    }
}

await SyncOnceAsync(dir, iface, cachePath, cts.Token);

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

static async Task<int> SyncOnceAsync(string dir, string iface, string cachePath, CancellationToken ct)
{
    var desired = await ReadDesiredPeersAsync(dir, ct);
    var previous = await LoadCacheAsync(cachePath, ct);

    foreach (var peer in desired.Values.OrderBy(p => p.PublicKey, StringComparer.Ordinal))
    {
        var allowed = string.IsNullOrWhiteSpace(peer.AllowedIps) ? "0.0.0.0/32" : peer.AllowedIps!;
        Console.WriteLine($"wg set {iface} peer {peer.PublicKey} allowed-ips {allowed}");
    }

    foreach (var stale in previous.Except(desired.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
    {
        Console.WriteLine($"wg set {iface} peer {stale} remove");
    }

    await SaveCacheAsync(cachePath, desired.Keys.ToHashSet(StringComparer.Ordinal), ct);
    var removed = previous.Count(k => !desired.ContainsKey(k));
    Console.Error.WriteLine($"# sync desired={desired.Count} removed={removed} cache={cachePath}");
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
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir))
    {
        Directory.CreateDirectory(dir);
    }

    var tmp = path + ".tmp";
    await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(keys.OrderBy(k => k, StringComparer.Ordinal).ToArray()), ct);
    File.Move(tmp, path, overwrite: true);
}

static void PrintHelp()
{
    Console.WriteLine("""
wg-peer-sync — reads MyVPN peer-state JSON and emits wg commands (Phase 5 helper)

Usage:
  wg-peer-sync <peer-state-dir> [--interface wg0] [--cache <path>] [--watch] [--apply]

Default is dry-run (prints commands). Tracks previously seen public keys in a cache
file so deleted peer JSON files emit `wg set ... remove`.

  --watch   re-run on directory changes until Ctrl+C
  --cache   override cache path (default: <dir>/.wg-peer-sync.cache.json)
  --apply   refused unless MYVPN_WG_SYNC_ALLOW_APPLY=1 (still not executed)

Does not call wg(8) directly in this scaffold.
""");
}

internal sealed record PeerState(string PublicKey, string? AllowedIps);
