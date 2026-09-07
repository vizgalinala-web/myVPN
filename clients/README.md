# Clients

## Windows (.NET)

Solution: `clients/windows/MyVPN.Clients.sln`

```bash
dotnet build clients/windows/MyVPN.Clients.sln
dotnet test clients/windows/MyVPN.Clients.sln
dotnet run --project clients/windows/MyVPN.Client.App
```

On Windows, install [WireGuard for Windows](https://www.wireguard.com/install/) first. The desktop app writes `myvpn.conf` and calls `wireguard.exe /installtunnelservice` (same as the CLI `connect`). Enable **Block untunneled traffic** for Kill Switch. Password is not written to disk.

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- keygen
dotnet run --project clients/windows/MyVPN.Client.Cli -- login-config \
  --api http://localhost:5212/ \
  --email user@example.com \
  --password 'CorrectHorseBatteryStaple!'
dotnet run --project clients/windows/MyVPN.Client.Cli -- servers --api http://localhost:5212/
dotnet run --project clients/windows/MyVPN.Client.Cli -- devices \
  --api http://localhost:5212/ --email user@example.com --password 'CorrectHorseBatteryStaple!'
```

- `MyVPN.Client` — HTTP API SDK + local WireGuard keygen (NSec)
- `MyVPN.Client.App` — Windows desktop UI (Avalonia): API URL, email, password, Connect / Stop
- `MyVPN.Client.Cli` — register / config / servers / devices / disconnect / refresh / logout / delete-account / connect / tunnel-up / tunnel-down / status / stop / change-password
- `MyVPN.Client.Tests` — keygen + config builder + tunnel planner unit tests
- Private keys stay local; API only receives public keys
- Local tunnel uses WireGuard for Windows (`/installtunnelservice`) or `wg-quick`; in-process Wintun is not implemented
- `session.json` stores last device/config metadata only (no passwords)

For File peer provisioning with Docker Compose, set `Vpn__PeerProvisioner=File` and sync `./peer-state` via `tools/wg-peer-sync`.

Tunnel import steps for WireGuard apps: [docs/clients-tunnel.md](../docs/clients-tunnel.md).
VPN node apply/systemd: [docs/ops.md](../docs/ops.md).

## iOS (Swift Package)

```text
clients/ios/
  Package.swift
  Sources/MyVPNApi/
```

Swift Package with models + `MyVPNApiClient`. Requires Xcode/macOS to build.

Next iOS steps (not in this repo yet):

1. App target + Packet Tunnel Network Extension
2. Generate keys in Keychain
3. Call API, assemble NETunnelProviderProtocol / WireGuard config
4. Kill Switch via `includeAllNetworks` / route enforcement
5. Call `deleteAccount(password:)` for GDPR-style erasure

## wg-peer-sync tool

```bash
dotnet run --project tools/wg-peer-sync -- ./peer-state
dotnet run --project tools/wg-peer-sync -- ./peer-state --watch
MYVPN_WG_SYNC_ALLOW_APPLY=1 dotnet run --project tools/wg-peer-sync -- ./peer-state --apply
```

Reads File provisioner JSON and prints or applies `wg set ...` / `remove`. Uses a local cache to detect deleted peers. Apply mode requires `MYVPN_WG_SYNC_ALLOW_APPLY=1` and runs `wg` without a shell.
