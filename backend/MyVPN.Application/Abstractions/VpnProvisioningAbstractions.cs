using MyVPN.Domain.Entities;

namespace MyVPN.Application.Abstractions;

public interface IVpnIpAllocator
{
    /// <summary>
    /// Picks the next free host address inside <paramref name="vpnNetworkCidr"/> (e.g. 10.8.0.0/24),
    /// skipping network/broadcast and the reserved server host (.1 by default).
    /// Returns value like "10.8.0.2/32".
    /// </summary>
    string Allocate(string vpnNetworkCidr, IReadOnlyCollection<string> usedAddressesCidrOrIp, int reservedServerHostOffset = 1);
}

/// <summary>
/// Registers/removes WireGuard peers on VPN servers.
/// Phase 3 ships an in-memory/dev adapter — real wg sync is plugged in later without API changes.
/// </summary>
public interface IWireGuardPeerProvisioner
{
    Task AddOrUpdatePeerAsync(VpnServer server, Device device, CancellationToken cancellationToken = default);
    Task RemovePeerAsync(string clientPublicKey, CancellationToken cancellationToken = default);
}
