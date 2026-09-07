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
        return Fail("No enabled servers returned by API.");
    }

    var server = serverIdText is null
        ? servers.Servers[0]
        : servers.Servers.First(s => s.Id == Guid.Parse(serverIdText));

    var config = await client.GetConfigurationAsync(device.Id, server.Id);
    var quick = MyVpnApiClient.BuildLocalQuickConfig(config, priv);
    if (outPath is not null)
    {
        var full = Path.GetFullPath(outPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        await File.WriteAllTextAsync(full, quick);
        Console.WriteLine(full);
    }
    else
    {
        Console.WriteLine(quick);
    }

    Console.Error.WriteLine($"# deviceId={device.Id} server={server.Name} address={config.Address}");
    Console.Error.WriteLine("# PrivateKey included only in local output/file. It was not uploaded.");
    return 0;
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

Notes:
  - Private keys are generated locally and never sent to the API.
  - Native Wintun bring-up is not in this CLI. Import the .conf into WireGuard for Windows
    and enable "Block untunneled traffic" for Kill Switch / leak blocking.
""");
}
