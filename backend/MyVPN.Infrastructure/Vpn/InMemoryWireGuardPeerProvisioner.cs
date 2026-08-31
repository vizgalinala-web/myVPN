using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MyVPN.Application.Abstractions;
using MyVPN.Domain.Entities;

namespace MyVPN.Infrastructure.Vpn;

/// <summary>
/// Development/stub peer registry. Does not talk to a real WireGuard interface.
/// Replace with an SSH/wg-api adapter in a later phase without changing application services.
/// </summary>
public sealed class InMemoryWireGuardPeerProvisioner : IWireGuardPeerProvisioner
{
    private readonly ConcurrentDictionary<string, PeerRecord> _peers = new(StringComparer.Ordinal);
    private readonly ILogger<InMemoryWireGuardPeerProvisioner> _logger;

    public InMemoryWireGuardPeerProvisioner(ILogger<InMemoryWireGuardPeerProvisioner> logger)
    {
        _logger = logger;
    }

    public Task AddOrUpdatePeerAsync(VpnServer server, Device device, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = new PeerRecord(server.Id, server.Hostname, device.Id, device.PublicKey, device.VpnAddress);
        _peers[device.PublicKey] = record;
        _logger.LogInformation(
            "WireGuard peer upserted (in-memory). Server={Server} DeviceId={DeviceId}",
            server.Hostname,
            device.Id);
        return Task.CompletedTask;
    }

    public Task RemovePeerAsync(string clientPublicKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_peers.TryRemove(clientPublicKey, out var removed))
        {
            _logger.LogInformation(
                "WireGuard peer removed (in-memory). DeviceId={DeviceId} Server={Server}",
                removed.DeviceId,
                removed.ServerHostname);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyCollection<PeerRecord> Snapshot() => _peers.Values.ToList();

    public sealed record PeerRecord(Guid ServerId, string ServerHostname, Guid DeviceId, string PublicKey, string? VpnAddress);
}
