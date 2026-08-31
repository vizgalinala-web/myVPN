# Clients

## Windows (.NET)

Solution: `clients/windows/MyVPN.Clients.sln`

```bash
dotnet build clients/windows/MyVPN.Clients.sln
dotnet test clients/windows/MyVPN.Clients.sln
dotnet run --project clients/windows/MyVPN.Client.Cli -- keygen
dotnet run --project clients/windows/MyVPN.Client.Cli -- login-config \
  --api http://localhost:5212/ \
  --email user@example.com \
  --password 'CorrectHorseBatteryStaple!'
```

- `MyVPN.Client` — HTTP API SDK + local WireGuard keygen (NSec)
- `MyVPN.Client.Cli` — register / fetch config helpers
- `MyVPN.Client.Tests` — keygen + config builder unit tests
- Private keys stay local; API only receives public keys
- Tunnel / Kill Switch / Wintun not implemented yet

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

## wg-peer-sync tool

```bash
dotnet run --project tools/wg-peer-sync -- ./peer-state
dotnet run --project tools/wg-peer-sync -- ./peer-state --watch
```

Reads File provisioner JSON and prints `wg set ...` / `remove` commands (dry-run). Uses a local cache to detect deleted peers. Does not execute `wg` unless a future apply path is enabled.
