# Client tunnel notes

Native Wintun / Network Extension tunnels are not shipped yet. Until then, import a full-tunnel WireGuard config (`AllowedIPs = 0.0.0.0/0, ::/0`, pinned `DNS`).

## Windows

1. Install [WireGuard for Windows](https://www.wireguard.com/install/).
2. Generate a local config (private key stays on disk only):

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- save-config \
  --api https://api.example.com/ \
  --email user@example.com \
  --password '...' \
  --out %USERPROFILE%\MyVPN\myvpn.conf
```

3. In WireGuard for Windows: **Import tunnel(s) from file** → select `myvpn.conf`.
4. Activate the tunnel, then enable **Block untunneled traffic (kill-switch)** so IPv4/IPv6/DNS cannot bypass the tunnel.
5. To disconnect server-side peer state later:

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- disconnect \
  --api https://api.example.com/ \
  --email user@example.com \
  --password '...' \
  --device-id <guid>
```

Erase the account (devices, tokens, peers):

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- delete-account \
  --api https://api.example.com/ \
  --email user@example.com \
  --password '...'
```

## iOS

Use the `MyVPNApi` Swift package to obtain configuration, then feed a Packet Tunnel provider / WireGuard kit. Key material must stay in Keychain. Set `includeAllNetworks` for Kill Switch when the extension ships. Account erasure: `deleteAccount(password:)`. Network Extension work is still out of scope for this repo snapshot.
