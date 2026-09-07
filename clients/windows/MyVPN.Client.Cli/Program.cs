using MyVPN.Client;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

var command = args[0].ToLowerInvariant();
try
{
    return command switch
    {
        "keygen" => Keygen(),
        "register" => await RegisterAsync(args),
        "login-config" => await LoginConfigAsync(args),
        "save-config" => await LoginConfigAsync(args),
        "servers" => await ServersAsync(args),
        "devices" => await DevicesAsync(args),
        "disconnect" => await DisconnectAsync(args),
        "refresh" => await RefreshAsync(args),
        "logout" => await LogoutAsync(args),
        "delete-account" => await DeleteAccountAsync(args),
        "connect" => await ConnectAsync(args),
        "tunnel-up" => TunnelUp(args),
        "tunnel-down" => TunnelDown(args),
        "status" => Status(args),
        "stop" => await StopAsync(args),
        "change-password" => await ChangePasswordAsync(args),
        _ => Fail($"Unknown command: {command}")
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static int Keygen()
{
    var (priv, pub) = WireGuardKeyPair.Generate();
    Console.WriteLine($"PrivateKey={priv}");
    Console.WriteLine($"PublicKey={pub}");
    Console.WriteLine("# Keep PrivateKey on device only. Send PublicKey to POST /api/devices.");
    return 0;
}

static async Task<int> RegisterAsync(string[] args)
{
    var api = Require(args, "--api");
    var email = Require(args, "--email");
    var password = Require(args, "--password");
    using var client = new MyVpnApiClient(new Uri(api));
    var result = await client.RegisterAsync(email, password);
    Console.WriteLine($"Registered {result.Email} id={result.Id}");
    return 0;
}

static async Task<int> LoginConfigAsync(string[] args)
{
    var (_, code) = await WriteConfigAsync(args);
    return code;
}

static async Task<(ClientSession? Session, int Code)> WriteConfigAsync(string[] args)
{
    var api = Require(args, "--api");
    var email = Require(args, "--email");
    var password = Require(args, "--password");
    var deviceName = Get(args, "--device-name") ?? "Windows CLI";
    var serverIdText = Get(args, "--server-id");
    var outPath = Get(args, "--out");

    var (priv, pub) = WireGuardKeyPair.Generate();
    using var client = new MyVpnApiClient(new Uri(api));
    await client.LoginAsync(email, password);
    var device = await client.CreateDeviceAsync(deviceName, "Windows", pub);
    var servers = await client.GetServersAsync();
    if (servers.Servers.Count == 0)
    {
        return (null, Fail("No enabled servers returned by API."));
    }

    var server = serverIdText is null
        ? servers.Servers[0]
        : servers.Servers.First(s => s.Id == Guid.Parse(serverIdText));

    var config = await client.GetConfigurationAsync(device.Id, server.Id);
    var quick = MyVpnApiClient.BuildLocalQuickConfig(config, priv);
    ClientSession? session = null;
        if (outPath is not null)
        {
            var full = Path.GetFullPath(outPath);
            LocalSecretFile.WriteAllText(full, quick);
            Console.WriteLine(full);
        session = new ClientSession(
            api,
            email,
            device.Id,
            server.Id,
            server.Name,
            full,
            DateTimeOffset.UtcNow);
        ClientSessionStore.Save(session);
    }
    else
    {
        Console.WriteLine(quick);
    }

    Console.Error.WriteLine($"# deviceId={device.Id} server={server.Name} address={config.Address}");
    Console.Error.WriteLine("# PrivateKey included only in local output/file. It was not uploaded.");
    return (session, 0);
}

static async Task<int> ServersAsync(string[] args)
{
    var api = Require(args, "--api");
    using var client = new MyVpnApiClient(new Uri(api));
    var servers = await client.GetServersAsync();
    foreach (var s in servers.Servers)
    {
        Console.WriteLine($"{s.Id}\t{s.Name}\t{s.Country}/{s.City}\t{s.Endpoint}");
    }

    return 0;
}

static async Task<int> DevicesAsync(string[] args)
{
    using var client = await LoginClientAsync(args);
    var devices = await client.GetDevicesAsync();
    foreach (var d in devices.Devices)
    {
        var state = d.IsConnected ? "connected" : "idle";
        Console.WriteLine($"{d.Id}\t{d.Name}\t{d.Platform}\t{d.VpnAddress ?? "-"}\t{state}");
    }

    return 0;
}

static async Task<int> DisconnectAsync(string[] args)
{
    var deviceId = Guid.Parse(Require(args, "--device-id"));
    using var client = await LoginClientAsync(args);
    await client.DisconnectAsync(deviceId);
    Console.WriteLine($"Disconnected {deviceId}");
    return 0;
}

static async Task<int> RefreshAsync(string[] args)
{
    var api = Require(args, "--api");
    var refreshToken = Require(args, "--refresh-token");
    using var client = new MyVpnApiClient(new Uri(api));
    var tokens = await client.RefreshAsync(refreshToken);
    Console.WriteLine($"AccessToken={tokens.AccessToken}");
    Console.WriteLine($"RefreshToken={tokens.RefreshToken}");
    Console.WriteLine($"ExpiresIn={tokens.ExpiresIn}");
    return 0;
}

static async Task<int> LogoutAsync(string[] args)
{
    var api = Require(args, "--api");
    var refreshToken = Require(args, "--refresh-token");
    using var client = new MyVpnApiClient(new Uri(api));
    await client.LogoutAsync(refreshToken);
    Console.WriteLine("Logged out.");
    return 0;
}

static async Task<int> DeleteAccountAsync(string[] args)
{
    var password = Require(args, "--password");
    using var client = await LoginClientAsync(args);
    await client.DeleteAccountAsync(password);
    Console.WriteLine("Account deleted.");
    return 0;
}

static async Task<int> ChangePasswordAsync(string[] args)
{
    var current = Require(args, "--password");
    var next = Require(args, "--new-password");
    using var client = await LoginClientAsync(args);
    await client.ChangePasswordAsync(current, next);
    Console.WriteLine("Password changed. Existing refresh tokens were revoked.");
    return 0;
}

static async Task<int> ConnectAsync(string[] args)
{
    var outPath = Get(args, "--out") ?? WireGuardTunnelPlanner.DefaultConfigPath();
    var loginArgs = WithOut(args, outPath);
    var (session, written) = await WriteConfigAsync(loginArgs);
    if (written != 0)
    {
        return written;
    }

    if (session is not null)
    {
        Console.Error.WriteLine($"# session={ClientSessionStore.DefaultPath()}");
    }

    if (Has(args, "--skip-tunnel"))
    {
        Console.Error.WriteLine("# Config written. Skipping local tunnel (--skip-tunnel).");
        return 0;
    }

    return ApplyTunnel(outPath, up: true, dryRun: Has(args, "--dry-run"));
}

static int TunnelUp(string[] args)
{
    var conf = ResolveConf(args);
    return ApplyTunnel(conf, up: true, dryRun: Has(args, "--dry-run"));
}

static int TunnelDown(string[] args)
{
    var conf = ResolveConf(args);
    return ApplyTunnel(conf, up: false, dryRun: Has(args, "--dry-run"));
}

static int Status(string[] args)
{
    var sessionPath = Get(args, "--session") ?? ClientSessionStore.DefaultPath();
    var session = ClientSessionStore.Load(sessionPath);
    var windows = OperatingSystem.IsWindows();
    var install = new WireGuardLocator(windows: windows).Find();
    var wireguard = install is null ? "missing" : install.Kind.ToString();

    if (Has(args, "--json"))
    {
        var payload = new
        {
            session = session is null ? null : new
            {
                session.Api,
                session.Email,
                session.DeviceId,
                session.ServerId,
                session.ServerName,
                session.ConfigPath,
                session.SavedAt,
                configExists = File.Exists(session.ConfigPath)
            },
            wireguard,
            sessionPath = Path.GetFullPath(sessionPath)
        };
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        }));
        return 0;
    }

    if (session is null)
    {
        Console.WriteLine("session=none");
        Console.WriteLine($"wireguard={wireguard}");
        return 0;
    }

    Console.WriteLine($"api={session.Api}");
    Console.WriteLine($"email={session.Email}");
    Console.WriteLine($"deviceId={session.DeviceId}");
    Console.WriteLine($"serverId={session.ServerId}");
    Console.WriteLine($"server={session.ServerName}");
    Console.WriteLine($"config={session.ConfigPath}");
    Console.WriteLine($"configExists={File.Exists(session.ConfigPath)}");
    Console.WriteLine($"savedAt={session.SavedAt:O}");
    Console.WriteLine($"wireguard={wireguard}");
    Console.WriteLine($"session={Path.GetFullPath(sessionPath)}");
    return 0;
}

static async Task<int> StopAsync(string[] args)
{
    var session = ClientSessionStore.Load(Get(args, "--session"));
    var conf = Get(args, "--conf") ?? Get(args, "--out") ?? session?.ConfigPath ?? WireGuardTunnelPlanner.DefaultConfigPath();
    var local = ApplyTunnel(conf, up: false, dryRun: Has(args, "--dry-run"));
    var code = local;

    if (!Has(args, "--local-only") && !Has(args, "--dry-run"))
    {
        var password = Get(args, "--password");
        if (string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("# Local tunnel stop attempted. Pass --password to also disconnect the API peer, or --local-only to skip.");
        }
        else if (session is null)
        {
            return Fail("No session.json; pass --api --email --device-id as with disconnect, or run connect first.");
        }
        else
        {
            using var client = new MyVpnApiClient(new Uri(session.Api));
            await client.LoginAsync(session.Email, password);
            await client.DisconnectAsync(session.DeviceId);
            Console.WriteLine($"Disconnected {session.DeviceId}");
            if (local != 0)
            {
                code = local;
            }
        }
    }

    if (Has(args, "--forget") && !Has(args, "--dry-run"))
    {
        ClientSessionStore.Delete(Get(args, "--session"));
        Console.WriteLine("Session forgotten.");
    }

    return code;
}

static string ResolveConf(string[] args)
    => Get(args, "--conf")
       ?? Get(args, "--out")
       ?? ClientSessionStore.Load()?.ConfigPath
       ?? WireGuardTunnelPlanner.DefaultConfigPath();

static string[] WithOut(string[] args, string outPath)
{
    var list = args.ToList();
    var idx = list.IndexOf("--out");
    if (idx >= 0 && idx < list.Count - 1)
    {
        list[idx + 1] = outPath;
        return list.ToArray();
    }

    list.Add("--out");
    list.Add(outPath);
    return list.ToArray();
}

static int ApplyTunnel(string configPath, bool up, bool dryRun)
{
    var full = Path.GetFullPath(configPath);
    if (up && !File.Exists(full))
    {
        return Fail($"Config not found: {full}. Run connect or save-config --out <file.conf> first.");
    }

    var windows = OperatingSystem.IsWindows();
    var install = new WireGuardLocator(windows: windows).Find();
    if (install is null)
    {
        Console.Error.WriteLine(WireGuardTunnelPlanner.MissingToolMessage(windows));
        Console.Error.WriteLine($"# config={full}");
        return 2;
    }

    var command = up
        ? WireGuardTunnelPlanner.PlanUp(install, full)
        : WireGuardTunnelPlanner.PlanDown(install, full);

    Console.Error.WriteLine($"# {command.Display}");
    if (dryRun)
    {
        Console.WriteLine(command.Display);
        return 0;
    }

    var code = RunNoShell(command);
    if (code == 0 && up && windows)
    {
        Console.Error.WriteLine("# Enable Kill Switch in WireGuard for Windows: Block untunneled traffic.");
    }

    return code;
}

static int RunNoShell(WireGuardCommand command)
{
    var start = new System.Diagnostics.ProcessStartInfo
    {
        FileName = command.FileName,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach (var argument in command.Arguments)
    {
        start.ArgumentList.Add(argument);
    }

    using var process = System.Diagnostics.Process.Start(start);
    if (process is null)
    {
        return Fail($"Failed to start {command.FileName}");
    }

    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (!string.IsNullOrWhiteSpace(stdout))
    {
        Console.Write(stdout);
    }

    if (!string.IsNullOrWhiteSpace(stderr))
    {
        Console.Error.Write(stderr);
    }

    return process.ExitCode;
}

static bool Has(string[] args, string name)
    => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

static async Task<MyVpnApiClient> LoginClientAsync(string[] args)
{
    var api = Require(args, "--api");
    var email = Require(args, "--email");
    var password = Require(args, "--password");
    var client = new MyVpnApiClient(new Uri(api));
    await client.LoginAsync(email, password);
    return client;
}

static string Require(string[] args, string name)
    => Get(args, name) ?? throw new ArgumentException($"Missing {name}");

static string? Get(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == name)
        {
            return args[i + 1];
        }
    }

    return null;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
MyVPN Windows client CLI

Commands:
  keygen
  register --api <url> --email <email> --password <password>
  login-config --api <url> --email <email> --password <password> [--device-name <name>] [--server-id <guid>] [--out <file>]
  save-config  (alias of login-config; prefer --out <file.conf>)
  servers --api <url>
  devices --api <url> --email <email> --password <password>
  disconnect --api <url> --email <email> --password <password> --device-id <guid>
  refresh --api <url> --refresh-token <token>
  logout --api <url> --refresh-token <token>
  delete-account --api <url> --email <email> --password <password>
  connect --api <url> --email <email> --password <password> [--out <file.conf>] [--server-id <guid>] [--dry-run] [--skip-tunnel]
  tunnel-up [--conf <file.conf>] [--dry-run]
  tunnel-down [--conf <file.conf>] [--dry-run]
  status [--json]
  stop [--password <password>] [--local-only] [--forget] [--dry-run]
  change-password --api <url> --email <email> --password <current> --new-password <new>

Notes:
  - Private keys are generated locally and never sent to the API.
  - connect writes a .conf then brings the tunnel up via WireGuard for Windows
    (/installtunnelservice) or wg-quick. No shell. Exit 2 if WireGuard is not installed.
  - connect also writes session.json (api, email, device id, config path) — never a password.
    On Unix the .conf and session.json are mode 600.
  - stop brings the local tunnel down; with --password it also calls the API disconnect.
    --forget deletes session.json.
  - After connect on Windows, enable "Block untunneled traffic" for Kill Switch.
""");
}
