namespace WgPeerSync;

public sealed record PeerState(string PublicKey, string? AllowedIps);

public sealed record WgCommand(string Interface, string PublicKey, string? AllowedIps, bool Remove)
{
    public IReadOnlyList<string> ToArgumentList()
    {
        if (Remove)
        {
            return ["set", Interface, "peer", PublicKey, "remove"];
        }

        var allowed = string.IsNullOrWhiteSpace(AllowedIps) ? "0.0.0.0/32" : AllowedIps!;
        return ["set", Interface, "peer", PublicKey, "allowed-ips", allowed];
    }

    public override string ToString()
        => "wg " + string.Join(' ', ToArgumentList());
}

public static class PeerSyncPlanner
{
    public static IReadOnlyList<WgCommand> Plan(
        string iface,
        IReadOnlyDictionary<string, PeerState> desired,
        IReadOnlySet<string> previous)
    {
        if (!IsSafeInterfaceName(iface))
        {
            throw new ArgumentException($"Unsafe WireGuard interface name: {iface}", nameof(iface));
        }

        var commands = new List<WgCommand>();

        foreach (var peer in desired.Values.OrderBy(p => p.PublicKey, StringComparer.Ordinal))
        {
            if (!IsValidPublicKey(peer.PublicKey))
            {
                throw new ArgumentException($"Invalid peer public key: {peer.PublicKey}");
            }

            commands.Add(new WgCommand(iface, peer.PublicKey, peer.AllowedIps, Remove: false));
        }

        foreach (var stale in previous.Except(desired.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!IsValidPublicKey(stale))
            {
                continue;
            }

            commands.Add(new WgCommand(iface, stale, AllowedIps: null, Remove: true));
        }

        return commands;
    }

    public static bool IsSafeInterfaceName(string iface)
        => !string.IsNullOrWhiteSpace(iface)
           && iface.Length <= 15
           && iface.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    public static bool IsValidPublicKey(string publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || publicKey.Length != 44 || publicKey.Any(char.IsWhiteSpace))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[64];
        return Convert.TryFromBase64String(publicKey, buffer, out var written) && written == 32;
    }
}
