# Client tunnel notes

The Windows CLI can bring a saved `.conf` up and down through the official WireGuard tools (no shell, no in-process Wintun). iOS Network Extension is not shipped yet.

## Windows

1. Install [WireGuard for Windows](https://www.wireguard.com/install/).
2. One-shot: fetch config and start the tunnel service:

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- connect \
  --api https://api.example.com/ \
  --email user@example.com \
  --password '...' \
  --out %USERPROFILE%\MyVPN\myvpn.conf
```

`connect` writes the `.conf` (private key stays on disk) then runs  
`wireguard.exe /installtunnelservice <file.conf>`.  
If WireGuard is not installed, the command exits **2** and prints the install URL.

Dry-run (print argv only):

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- tunnel-up \
  --conf %USERPROFILE%\MyVPN\myvpn.conf --dry-run
```

3. In WireGuard for Windows, enable **Block untunneled traffic (kill-switch)** so IPv4/IPv6/DNS cannot bypass the tunnel. The CLI cannot flip that checkbox.
4. Stop the local tunnel:

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- tunnel-down \
  --conf %USERPROFILE%\MyVPN\myvpn.conf
```

5. Disconnect server-side peer state (API) if needed:

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- disconnect \
  --api https://api.example.com/ \
  --email user@example.com \
  --password '...' \
  --device-id <guid>
```

Manual import still works: **Import tunnel(s) from file** in the WireGuard app.

Erase the account (devices, tokens, peers):

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- delete-account \
  --api https://api.example.com/ \
  --email user@example.com \
  --password '...'
```

## Linux / macOS (wg-quick)

If `wg-quick` is on `PATH`, the same `tunnel-up` / `tunnel-down` commands call `wg-quick up|down <file.conf>`.

## iOS

Use the `MyVPNApi` Swift package to obtain configuration, then feed a Packet Tunnel provider / WireGuard kit. Key material must stay in Keychain. Set `includeAllNetworks` for Kill Switch when the extension ships. Account erasure: `deleteAccount(password:)`. Network Extension work is still out of scope for this repo snapshot.
