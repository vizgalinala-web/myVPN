namespace MyVPN.Application.DTOs;

public sealed record RegisterRequest(string Email, string Password);

public sealed record RegisterResponse(Guid Id, string Email, DateTimeOffset CreatedAt);

public sealed record LoginRequest(string Email, string Password);

public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    string TokenType = "Bearer");

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Confirms account erasure. Password is verified and never logged.</summary>
public sealed record DeleteAccountRequest(string Password);

public sealed record CurrentUserResponse(Guid Id, string Email, DateTimeOffset CreatedAt);

public sealed record VpnServerDto(
    Guid Id,
    string Name,
    string Country,
    string City,
    string Hostname,
    string Endpoint);

public sealed record ServersResponse(IReadOnlyList<VpnServerDto> Servers);

public sealed record CreateDeviceRequest(string Name, string Platform, string PublicKey);

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

public sealed record WireGuardPeerDto(
    string PublicKey,
    string Endpoint,
    string AllowedIps,
    int PersistentKeepalive);

/// <summary>
/// Client connection material. Private keys are never included — the client inserts its own.
/// </summary>
public sealed record DeviceVpnConfigurationResponse(
    Guid DeviceId,
    Guid ServerId,
    string ServerName,
    string Address,
    string Dns,
    WireGuardPeerDto Peer,
    string WireGuardQuickConfig);
