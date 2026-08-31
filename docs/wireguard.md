# WireGuard provisioning (Phase 3)

## Scope

Phase 3 backend adds **client VPN configuration issuance** and **IP allocation**.

Still out of scope:

- native iOS / Windows apps
- real `wg` host sync on production VPN nodes (in-memory adapter for now)
- Kill Switch / DNS leak protection

## Endpoint

`GET /api/devices/{id}/configuration?serverId={guid}`

Requires Bearer access token. Ownership is enforced in `VpnConfigurationService`.

### Behaviour

1. Validate user + owned active device
2. Load enabled VPN server by id (disabled/missing → 404)
3. Allocate `VpnAddress` from `VpnServer.VpnNetwork` if missing or outside that network (`.1` reserved for server)
4. Upsert peer via `IWireGuardPeerProvisioner`
5. Return configuration **without** any private key

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

## Peer provisioner

`InMemoryWireGuardPeerProvisioner` records peers in process memory and logs upserts/removals. Replace this implementation with an SSH/`wg set` / controller API adapter later — application code depends only on `IWireGuardPeerProvisioner`.

Device deletion removes the peer (by public key) before the DB row is deleted.

## Example

```bash
# after login + device create + servers list
curl -sS "$API/api/devices/$DEVICE_ID/configuration?serverId=$SERVER_ID" \
  -H "Authorization: Bearer $ACCESS"
```
