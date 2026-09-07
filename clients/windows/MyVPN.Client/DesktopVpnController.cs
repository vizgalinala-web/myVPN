using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MyVPN.Client;

/// <summary>
/// UI-facing Windows client: login, write local config, bring tunnel up/down.
/// Password is kept only in memory.
/// </summary>
public sealed class DesktopVpnController : INotifyPropertyChanged
{
    private readonly VpnTunnelService _tunnels;
    private readonly Func<Uri, MyVpnApiClient> _apiFactory;
    private string _api = "http://127.0.0.1:5212/";
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _status = "Disconnected. Enter API URL, email and password, then Connect.";
    private bool _busy;
    private bool _connected;

    public DesktopVpnController(VpnTunnelService? tunnels = null, Func<Uri, MyVpnApiClient>? apiFactory = null)
    {
        _tunnels = tunnels ?? new VpnTunnelService();
        _apiFactory = apiFactory ?? (uri => new MyVpnApiClient(uri));
    }

    public string Api
    {
        get => _api;
        set { _api = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); }
    }

    public string Email
    {
        get => _email;
        set { _email = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); }
    }

    public string Password
    {
        get => _password;
        set { _password = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public bool Busy
    {
        get => _busy;
        private set { _busy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConnect)); OnPropertyChanged(nameof(CanStop)); }
    }

    public bool Connected
    {
        get => _connected;
        private set { _connected = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanStop)); }
    }

    public bool CanConnect => !Busy && !string.IsNullOrWhiteSpace(Api) && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

    public bool CanStop => !Busy && (Connected || ClientSessionStore.Load() is not null);

    public string KillSwitchHint { get; } =
        "After Connect on Windows, open WireGuard and enable Block untunneled traffic (Kill Switch). In-process Wintun is not in this build.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConnect)
        {
            Status = "Fill API URL, email and password.";
            return;
        }

        Busy = true;
        try
        {
            var outPath = WireGuardTunnelPlanner.DefaultConfigPath();
            var (priv, pub) = WireGuardKeyPair.Generate();
            using var client = _apiFactory(new Uri(Api.Trim()));
            await client.LoginAsync(Email.Trim(), Password, cancellationToken);
            var device = await client.CreateDeviceAsync("Windows App", "Windows", pub, cancellationToken);
            var servers = await client.GetServersAsync(cancellationToken);
            if (servers.Servers.Count == 0)
            {
                Status = "No enabled VPN servers from the API.";
                return;
            }

            var server = servers.Servers[0];
            var config = await client.GetConfigurationAsync(device.Id, server.Id, cancellationToken);
            var quick = MyVpnApiClient.BuildLocalQuickConfig(config, priv);
            LocalSecretFile.WriteAllText(outPath, quick);
            ClientSessionStore.Save(new ClientSession(
                Api.Trim(),
                Email.Trim(),
                device.Id,
                server.Id,
                server.Name,
                Path.GetFullPath(outPath),
                DateTimeOffset.UtcNow));

            var tunnel = _tunnels.Apply(outPath, up: true, dryRun: false);
            if (tunnel.ExitCode == 0)
            {
                Connected = true;
                Status = $"Connected via {server.Name} ({config.Address}). {KillSwitchHint}";
            }
            else if (tunnel.WireGuardMissing)
            {
                Connected = false;
                Status = "Config saved, but WireGuard is not installed. " + tunnel.StandardError;
            }
            else
            {
                Connected = false;
                Status = string.IsNullOrWhiteSpace(tunnel.StandardError)
                    ? $"Tunnel failed (exit {tunnel.ExitCode})."
                    : tunnel.StandardError;
            }
        }
        catch (Exception ex)
        {
            Connected = false;
            Status = ex is MyVpnApiException apiEx
                ? $"API error {apiEx.StatusCode}."
                : "Could not connect. Check API URL and credentials.";
        }
        finally
        {
            Busy = false;
        }
    }

    public async Task StopAsync(bool disconnectApi = true, CancellationToken cancellationToken = default)
    {
        Busy = true;
        try
        {
            var session = ClientSessionStore.Load();
            var conf = session?.ConfigPath ?? WireGuardTunnelPlanner.DefaultConfigPath();
            var tunnel = _tunnels.Apply(conf, up: false, dryRun: false);
            if (disconnectApi && session is not null && !string.IsNullOrEmpty(Password))
            {
                using var client = _apiFactory(new Uri(session.Api));
                await client.LoginAsync(session.Email, Password, cancellationToken);
                await client.DisconnectAsync(session.DeviceId, cancellationToken);
            }

            Connected = false;
            Status = tunnel.ExitCode == 0 || tunnel.WireGuardMissing
                ? "Disconnected."
                : $"Local tunnel stop exit {tunnel.ExitCode}. {tunnel.StandardError}";
        }
        catch (Exception)
        {
            Connected = false;
            Status = "Disconnected locally. API disconnect failed.";
        }
        finally
        {
            Busy = false;
        }
    }

    public void RefreshFromSession()
    {
        var session = ClientSessionStore.Load();
        if (session is null)
        {
            Status = "No saved session. Connect to create one.";
            return;
        }

        Api = session.Api;
        Email = session.Email;
        Status = $"Last session: {session.ServerName} / {session.DeviceId}. Password is not stored; enter it to Connect or Stop.";
        OnPropertyChanged(nameof(CanStop));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
