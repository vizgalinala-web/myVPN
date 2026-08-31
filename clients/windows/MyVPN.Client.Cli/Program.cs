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
    Console.WriteLine(quick);
    Console.Error.WriteLine($"# deviceId={device.Id} server={server.Name} address={config.Address}");
    Console.Error.WriteLine("# PrivateKey included only in local output. It was not uploaded.");
    return 0;
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
MyVPN Windows client CLI (Phase 4 scaffold)

Commands:
  keygen
  register --api <url> --email <email> --password <password>
  login-config --api <url> --email <email> --password <password> [--device-name <name>] [--server-id <guid>]

Notes:
  - Private keys are generated locally and never sent to the API.
  - Tunnel bring-up / Kill Switch are not implemented in this CLI yet.
""");
}
