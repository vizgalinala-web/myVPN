# WireGuard provisioning (Phase 3)

## Scope

Phase 3 backend adds **client VPN configuration issuance** and **IP allocation**.

Still out of scope:

- in-process Wintun / iOS Network Extension
- automated OS Kill Switch (see [clients-tunnel.md](clients-tunnel.md) for `connect` + Block untunneled traffic)

The Windows CLI (`connect` / `tunnel-up` / `tunnel-down`) drives the official WireGuard helper or `wg-quick` without a shell.

Phase 4 adds API client scaffolds under `clients/` and a dry-run peer sync helper under `tools/wg-peer-sync`.

## Endpoint

`GET /api/devices/{id}/configuration?serverId={guid}`

Requires Bearer access token. Ownership is enforced in `VpnConfigurationService`.

### Disconnect

`POST /api/devices/{id}/disconnect`

- Bearer required
- Idempotent **204**
- Removes peer via provisioner
- Clears `ConnectedAt` / `LastConnectedServerId`
- Keeps allocated `VpnAddress` for reuse on next connect

### Behaviour

1. Validate user + owned active device
2. Load enabled VPN server by id (disabled/missing → 404)
3. Allocate `VpnAddress` from `VpnServer.VpnNetwork` if missing or outside that network (`.1` reserved for server)
4. Upsert peer via `IWireGuardPeerProvisioner`
5. Persist connection markers (`LastConnectedServerId`, `ConnectedAt`)
6. Return configuration **without** any private key

### Response shape

```json
{
  "deviceId": "...",
  "serverId": "...",
  "serverName": "Germany 1",
  "address": "10.8.0.2/32",
  "dns": "1.1.1.1",
  "peer": {
    "publicKey": "...",
    "endpoint": "de1.example.com:51820",
    "allowedIps": "0.0.0.0/0, ::/0",
    "persistentKeepalive": 25
  },
  "wireGuardQuickConfig": "[Interface]\nAddress = ...\n..."
}
```

`wireGuardQuickConfig` includes a comment that `PrivateKey` must be set on the client. The API never stores or returns device private keys or server private keys.

## Configuration

| Key | Default | Purpose |
|---|---|---|
| `Vpn__DnsServers` | `1.1.1.1` | DNS written into client config |
| `Vpn__AllowedIps` | `0.0.0.0/0, ::/0` | Full-tunnel MVP |
| `Vpn__PersistentKeepaliveSeconds` | `25` | NAT keepalive |
| `Vpn__ReservedServerHostOffset` | `1` | Host reserved for WG server |
| `Vpn__MaxDevicesPerUser` | `5` | Max registered devices per account |
| `Vpn__PeerProvisioner` | `InMemory` | `InMemory` or `File` |
| `Vpn__PeerStateDirectory` | `peer-state` | Peer JSON dir for File mode |

Rate limits (configurable): `DeviceConfiguration` and `DeviceDisconnect` (default 30/min per user id).

Background job `RefreshTokenCleanup` deletes expired/revoked refresh tokens.

## Peer provisioner

`InMemoryWireGuardPeerProvisioner` records peers in process memory and logs upserts/removals.

`FileSystemWireGuardPeerProvisioner` (when `Vpn__PeerProvisioner=File`) writes peer desired-state JSON for an external sync agent. It does **not** call `wg` directly. Application code depends only on `IWireGuardPeerProvisioner`.

Device deletion removes the peer (by public key) before the DB row is deleted.

## Peer sync helper (Phase 6)

```bash
dotnet run --project tools/wg-peer-sync -- ./peer-state
dotnet run --project tools/wg-peer-sync -- ./peer-state --watch
MYVPN_WG_SYNC_ALLOW_APPLY=1 dotnet run --project tools/wg-peer-sync -- ./peer-state --apply
```

Reads File provisioner JSON and prints or applies `wg set ...` / `wg set ... remove`.

- Tracks previously seen public keys in `<dir>/.wg-peer-sync.cache.json` so deleted peer files emit removals
- `--watch` re-runs on directory changes until Ctrl+C
- `--apply` executes `wg` via `Process` (no shell) only when `MYVPN_WG_SYNC_ALLOW_APPLY=1`
- Optional `MYVPN_WG_BIN` overrides the `wg` binary path
- Interface names and public keys are validated before execution

## Example

```bash
# after login + device create + servers list
curl -sS "$API/api/devices/$DEVICE_ID/configuration?serverId=$SERVER_ID" \
  -H "Authorization: Bearer $ACCESS"
```
