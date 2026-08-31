using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVPN.Application.Abstractions;
using MyVPN.Application.Options;
using MyVPN.Domain.Entities;

namespace MyVPN.Infrastructure.Vpn;

/// <summary>
/// Writes peer desired-state JSON files for an external WireGuard sync agent.
/// Does not execute wg(8) directly.
/// </summary>
public sealed class FileSystemWireGuardPeerProvisioner : IWireGuardPeerProvisioner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _directory;
    private readonly ILogger<FileSystemWireGuardPeerProvisioner> _logger;

    public FileSystemWireGuardPeerProvisioner(
        IOptions<VpnOptions> options,
        ILogger<FileSystemWireGuardPeerProvisioner> logger)
    {
        _directory = string.IsNullOrWhiteSpace(options.Value.PeerStateDirectory)
            ? "peer-state"
            : options.Value.PeerStateDirectory;
        _logger = logger;
        Directory.CreateDirectory(_directory);
    }

    public async Task AddOrUpdatePeerAsync(VpnServer server, Device device, CancellationToken cancellationToken = default)
    {
        var path = PeerPath(device.PublicKey);
        var payload = new
        {
            serverId = server.Id,
            serverHostname = server.Hostname,
            serverEndpoint = server.Endpoint,
            deviceId = device.Id,
            publicKey = device.PublicKey,
            allowedIps = device.VpnAddress,
            updatedAt = DateTimeOffset.UtcNow
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        _logger.LogInformation(
            "WireGuard peer state written. Path={Path} DeviceId={DeviceId} Server={Server}",
            path,
            device.Id,
            server.Hostname);
    }

    public Task RemovePeerAsync(string clientPublicKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = PeerPath(clientPublicKey);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("WireGuard peer state removed. Path={Path}", path);
        }

        return Task.CompletedTask;
    }

    private string PeerPath(string publicKey)
    {
        var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(publicKey)))[..32];
        return Path.Combine(_directory, $"{safe}.json");
    }
}

/// <summary>Fans out peer changes to multiple provisioners (e.g. in-memory + file).</summary>
public sealed class CompositeWireGuardPeerProvisioner : IWireGuardPeerProvisioner
{
    private readonly IReadOnlyList<IWireGuardPeerProvisioner> _inner;

    public CompositeWireGuardPeerProvisioner(IEnumerable<IWireGuardPeerProvisioner> inner)
    {
        _inner = inner.ToList();
    }

    public async Task AddOrUpdatePeerAsync(VpnServer server, Device device, CancellationToken cancellationToken = default)
    {
        foreach (var provisioner in _inner)
        {
            await provisioner.AddOrUpdatePeerAsync(server, device, cancellationToken);
        }
    }

    public async Task RemovePeerAsync(string clientPublicKey, CancellationToken cancellationToken = default)
    {
        foreach (var provisioner in _inner)
        {
            await provisioner.RemovePeerAsync(clientPublicKey, cancellationToken);
        }
    }
}
