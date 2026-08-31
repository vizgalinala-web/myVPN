# Client tunnel notes (Phase 7)

Native Wintun / Network Extension tunnels are not shipped yet. Until then:

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
4. Activate the tunnel. To disconnect server-side peer state later:

```bash
dotnet run --project clients/windows/MyVPN.Client.Cli -- disconnect \
  --api https://api.example.com/ \
  --email user@example.com \
  --password '...' \
  --device-id <guid>
```

Kill Switch / DNS leak protection are not automated by MyVPN yet; WireGuard for Windows "Block untunneled traffic" is an interim option.

## iOS

Use the `MyVPNApi` Swift package to obtain configuration, then feed a Packet Tunnel provider / WireGuard kit. Key material must stay in Keychain. Network Extension work is still out of scope for this repo snapshot.
