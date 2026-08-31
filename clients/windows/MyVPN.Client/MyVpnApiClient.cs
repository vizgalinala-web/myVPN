using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVPN.Client;

public sealed class MyVpnApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private string? _accessToken;

    public MyVpnApiClient(Uri baseAddress, HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _http = new HttpClient { BaseAddress = baseAddress };
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
            _http.BaseAddress ??= baseAddress;
            _ownsHttp = false;
        }
    }

    public void SetAccessToken(string? accessToken)
    {
        _accessToken = accessToken;
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public Task<RegisterResponse> RegisterAsync(string email, string password, CancellationToken ct = default)
        => PostAsync<RegisterResponse>("api/auth/register", new { email, password }, ct);

    public async Task<TokenResponse> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var tokens = await PostAsync<TokenResponse>("api/auth/login", new { email, password }, ct);
        SetAccessToken(tokens.AccessToken);
        return tokens;
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var tokens = await PostAsync<TokenResponse>("api/auth/refresh", new { refreshToken }, ct);
        SetAccessToken(tokens.AccessToken);
        return tokens;
    }

    public Task LogoutAsync(string refreshToken, CancellationToken ct = default)
        => PostNoContentAsync("api/auth/logout", new { refreshToken }, ct);

    public Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
        => PostNoContentAsync("api/auth/change-password", new { currentPassword, newPassword }, ct);

    public Task<CurrentUserResponse> GetMeAsync(CancellationToken ct = default)
        => GetAsync<CurrentUserResponse>("api/me", ct);

    public Task<ServersResponse> GetServersAsync(CancellationToken ct = default)
        => GetAsync<ServersResponse>("api/servers", ct);

    public Task<DevicesResponse> GetDevicesAsync(CancellationToken ct = default)
        => GetAsync<DevicesResponse>("api/devices", ct);

    public Task<DeviceResponse> GetDeviceAsync(Guid id, CancellationToken ct = default)
        => GetAsync<DeviceResponse>($"api/devices/{id}", ct);

    public Task<DeviceResponse> CreateDeviceAsync(string name, string platform, string publicKey, CancellationToken ct = default)
        => PostAsync<DeviceResponse>("api/devices", new { name, platform, publicKey }, ct);

    public Task DeleteDeviceAsync(Guid id, CancellationToken ct = default)
        => SendNoContentAsync(HttpMethod.Delete, $"api/devices/{id}", ct);

    public Task<DeviceVpnConfigurationResponse> GetConfigurationAsync(Guid deviceId, Guid serverId, CancellationToken ct = default)
        => GetAsync<DeviceVpnConfigurationResponse>($"api/devices/{deviceId}/configuration?serverId={serverId}", ct);

    public Task DisconnectAsync(Guid deviceId, CancellationToken ct = default)
        => PostNoContentAsync($"api/devices/{deviceId}/disconnect", new { }, ct);

    /// <summary>
    /// Builds a wg-quick config by inserting the local private key into the API template.
    /// </summary>
    public static string BuildLocalQuickConfig(DeviceVpnConfigurationResponse config, string privateKey)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {privateKey}");
        sb.AppendLine($"Address = {config.Address}");
        sb.AppendLine($"DNS = {config.Dns}");
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {config.Peer.PublicKey}");
        sb.AppendLine($"Endpoint = {config.Peer.Endpoint}");
        sb.AppendLine($"AllowedIPs = {config.Peer.AllowedIps}");
        sb.AppendLine($"PersistentKeepalive = {config.Peer.PersistentKeepalive}");
        return sb.ToString().Replace("\r\n", "\n");
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        await EnsureSuccessAsync(response, ct);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return value ?? throw new MyVpnApiException(500, "Empty response body.");
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
        var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return value ?? throw new MyVpnApiException(500, "Empty response body.");
    }

    private async Task PostNoContentAsync(string path, object body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(path, body, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private async Task SendNoContentAsync(HttpMethod method, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new MyVpnApiException((int)response.StatusCode, body);
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}

public sealed class MyVpnApiException : Exception
{
    public int StatusCode { get; }

    public MyVpnApiException(int statusCode, string body)
        : base($"MyVPN API error {statusCode}: {body}")
    {
        StatusCode = statusCode;
    }
}

public sealed record RegisterResponse(Guid Id, string Email, DateTimeOffset CreatedAt);
public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType);
public sealed record CurrentUserResponse(Guid Id, string Email, DateTimeOffset CreatedAt);
public sealed record VpnServerDto(Guid Id, string Name, string Country, string City, string Hostname, string Endpoint);
public sealed record ServersResponse(IReadOnlyList<VpnServerDto> Servers);
public sealed record DeviceResponse(
    Guid Id,
    string Name,
    string Platform,
    string PublicKey,
    string? VpnAddress,
    Guid? LastConnectedServerId,
    DateTimeOffset? ConnectedAt,
    bool IsConnected,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    bool IsActive);
public sealed record DevicesResponse(IReadOnlyList<DeviceResponse> Devices);
public sealed record WireGuardPeerDto(string PublicKey, string Endpoint, string AllowedIps, int PersistentKeepalive);
public sealed record DeviceVpnConfigurationResponse(
    Guid DeviceId,
    Guid ServerId,
    string ServerName,
    string Address,
    string Dns,
    WireGuardPeerDto Peer,
    string WireGuardQuickConfig);
