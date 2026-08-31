using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVPN.Application.Abstractions;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Application.Options;
using MyVPN.Domain.Entities;
using System.Text;

namespace MyVPN.Application.Services;

public sealed class VpnConfigurationService
{
    private readonly IDeviceRepository _devices;
    private readonly IUserRepository _users;
    private readonly IVpnServerRepository _servers;
    private readonly IVpnIpAllocator _ipAllocator;
    private readonly IWireGuardPeerProvisioner _provisioner;
    private readonly IClock _clock;
    private readonly VpnOptions _options;
    private readonly ILogger<VpnConfigurationService> _logger;

    public VpnConfigurationService(
        IDeviceRepository devices,
        IUserRepository users,
        IVpnServerRepository servers,
        IVpnIpAllocator ipAllocator,
        IWireGuardPeerProvisioner provisioner,
        IClock clock,
        IOptions<VpnOptions> options,
        ILogger<VpnConfigurationService> logger)
    {
        _devices = devices;
        _users = users;
        _servers = servers;
        _ipAllocator = ipAllocator;
        _provisioner = provisioner;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DeviceVpnConfigurationResponse> GetConfigurationAsync(
        Guid userId,
        Guid deviceId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new AppException(ErrorCodes.Unauthorized, "Unauthorized", "Authentication is required.", 401);
        }

        var device = await _devices.FindByIdForUserAsync(deviceId, userId, cancellationToken);
        if (device is null || !device.IsActive)
        {
            throw new AppException(ErrorCodes.NotFound, "Not found", "Device not found.", 404);
        }

        var server = await _servers.FindEnabledByIdAsync(serverId, cancellationToken);
        if (server is null)
        {
            throw new AppException(ErrorCodes.NotFound, "Not found", "Server not found.", 404);
        }

        if (string.IsNullOrWhiteSpace(device.VpnAddress) || !AddressBelongsToNetwork(device.VpnAddress, server.VpnNetwork))
        {
            var used = await _devices.ListAssignedVpnAddressesAsync(cancellationToken);
            // Keep the current device address out of the "used" set when reallocating after server switch.
            used = used.Where(a => !string.Equals(NormalizeIp(a), NormalizeIp(device.VpnAddress), StringComparison.Ordinal)).ToList();
            device.VpnAddress = _ipAllocator.Allocate(server.VpnNetwork, used, _options.ReservedServerHostOffset);
        }

        device.LastSeenAt = _clock.UtcNow;
        device.LastConnectedServerId = server.Id;
        device.ConnectedAt = _clock.UtcNow;
        await _provisioner.AddOrUpdatePeerAsync(server, device, cancellationToken);
        await _devices.SaveChangesAsync(cancellationToken);

        var peer = new WireGuardPeerDto(
            server.PublicKey,
            server.Endpoint,
            _options.AllowedIps,
            _options.PersistentKeepaliveSeconds);

        var quickConfig = BuildQuickConfig(device.VpnAddress!, _options.DnsServers, peer);

        _logger.LogInformation(
            "VPN configuration issued. UserId={UserId} DeviceId={DeviceId} ServerId={ServerId}",
            userId,
            deviceId,
            serverId);

        return new DeviceVpnConfigurationResponse(
            device.Id,
            server.Id,
            server.Name,
            device.VpnAddress!,
            _options.DnsServers,
            peer,
            quickConfig);
    }

    public async Task DeprovisionDeviceAsync(Device device, CancellationToken cancellationToken = default)
    {
        await _provisioner.RemovePeerAsync(device.PublicKey, cancellationToken);
    }

    public async Task DisconnectAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new AppException(ErrorCodes.Unauthorized, "Unauthorized", "Authentication is required.", 401);
        }

        var device = await _devices.FindByIdForUserAsync(deviceId, userId, cancellationToken);
        if (device is null)
        {
            throw new AppException(ErrorCodes.NotFound, "Not found", "Device not found.", 404);
        }

        // Idempotent: already disconnected still succeeds.
        await _provisioner.RemovePeerAsync(device.PublicKey, cancellationToken);
        device.ConnectedAt = null;
        device.LastConnectedServerId = null;
        device.LastSeenAt = _clock.UtcNow;
        await _devices.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Device disconnected. UserId={UserId} DeviceId={DeviceId}", userId, deviceId);
    }

    private static string BuildQuickConfig(string address, string dns, WireGuardPeerDto peer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"Address = {address}");
        sb.AppendLine($"DNS = {dns}");
        sb.AppendLine("# PrivateKey is never provided by the API. Set it on the client only.");
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {peer.PublicKey}");
        sb.AppendLine($"Endpoint = {peer.Endpoint}");
        sb.AppendLine($"AllowedIPs = {peer.AllowedIps}");
        sb.AppendLine($"PersistentKeepalive = {peer.PersistentKeepalive}");
        return sb.ToString().Replace("\r\n", "\n");
    }

    private static bool AddressBelongsToNetwork(string addressCidr, string networkCidr)
    {
        try
        {
            var host = NormalizeIp(addressCidr);
            if (!System.Net.IPAddress.TryParse(host, out var hostIp))
            {
                return false;
            }

            var parts = networkCidr.Split('/');
            if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var netIp) || !int.TryParse(parts[1], out var prefix))
            {
                return false;
            }

            var hostBytes = hostIp.GetAddressBytes();
            var netBytes = netIp.GetAddressBytes();
            if (hostBytes.Length != netBytes.Length)
            {
                return false;
            }

            var fullBytes = prefix / 8;
            var remBits = prefix % 8;
            for (var i = 0; i < fullBytes; i++)
            {
                if (hostBytes[i] != netBytes[i])
                {
                    return false;
                }
            }

            if (remBits == 0)
            {
                return true;
            }

            var mask = (byte)(0xFF << (8 - remBits));
            return (hostBytes[fullBytes] & mask) == (netBytes[fullBytes] & mask);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeIp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Split('/', 2)[0].Trim();
    }
}
