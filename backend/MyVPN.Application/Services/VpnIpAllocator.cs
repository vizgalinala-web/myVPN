using System.Net;
using System.Net.Sockets;
using MyVPN.Application.Abstractions;
using MyVPN.Application.Common;

namespace MyVPN.Application.Services;

public sealed class VpnIpAllocator : IVpnIpAllocator
{
    public string Allocate(string vpnNetworkCidr, IReadOnlyCollection<string> usedAddressesCidrOrIp, int reservedServerHostOffset = 1)
    {
        if (!TryParseCidr(vpnNetworkCidr, out var network, out var prefixLength))
        {
            throw new AppException(
                ErrorCodes.ValidationFailed,
                "Invalid VPN network",
                "Server VpnNetwork is not a valid IPv4 CIDR.",
                500);
        }

        if (network.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new AppException(
                ErrorCodes.ValidationFailed,
                "Invalid VPN network",
                "Only IPv4 VpnNetwork is supported in Phase 3.",
                500);
        }

        if (prefixLength is < 8 or > 30)
        {
            throw new AppException(
                ErrorCodes.ValidationFailed,
                "Invalid VPN network",
                "VpnNetwork prefix must be between /8 and /30.",
                500);
        }

        var used = new HashSet<uint>();
        foreach (var entry in usedAddressesCidrOrIp)
        {
            if (TryParseHost(entry, out var host))
            {
                used.Add(host);
            }
        }

        var networkUint = ToUInt(network);
        var hostBits = 32 - prefixLength;
        var hostCount = 1u << hostBits;
        var broadcast = networkUint + hostCount - 1;
        var reservedServer = networkUint + (uint)Math.Max(1, reservedServerHostOffset);

        for (uint offset = 1; offset < hostCount - 1; offset++)
        {
            var candidate = networkUint + offset;
            if (candidate == networkUint || candidate == broadcast || candidate == reservedServer)
            {
                continue;
            }

            if (used.Contains(candidate))
            {
                continue;
            }

            var ip = ToAddress(candidate);
            return $"{ip}/32";
        }

        throw new AppException(
            ErrorCodes.Conflict,
            "Address pool exhausted",
            "No free VPN addresses remain in this server network.",
            409);
    }

    private static bool TryParseCidr(string cidr, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;
        var parts = cidr.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0], out network!) || !int.TryParse(parts[1], out prefixLength))
        {
            return false;
        }

        return true;
    }

    private static bool TryParseHost(string value, out uint host)
    {
        host = 0;
        var ipPart = value.Split('/', 2)[0].Trim();
        if (!IPAddress.TryParse(ipPart, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        host = ToUInt(address);
        return true;
    }

    private static uint ToUInt(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToUInt32(bytes, 0);
    }

    private static IPAddress ToAddress(uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return new IPAddress(bytes);
    }
}
