# Native clients (Phase 4+)

This folder will hold iOS and Windows VPN clients. Backend API through Phase 3 is ready for them.

## Planned apps

| Client | Tech | Notes |
|---|---|---|
| iOS | Swift + Network Extension (Packet Tunnel) | WireGuard kit / NEPacketTunnelProvider |
| Windows | C# / WinUI + Wintun/WireGuardNT | Use config from `/api/devices/{id}/configuration` |

## Client responsibilities

1. Generate WireGuard keypair locally — **never send private key to API**
2. Register device: `POST /api/devices` with public key
3. Fetch config: `GET /api/devices/{id}/configuration?serverId=...`
4. Insert local private key into `[Interface]`
5. Bring tunnel up; call `POST /api/devices/{id}/disconnect` on stop
6. Implement Kill Switch / DNS leak protection on device OS APIs (not backend)

## Auth flow

`register` → `login` → store refresh token securely (Keychain / Credential Locker) → refresh before access expiry.

## Status

Scaffold only — no native VPN UI or Network Extension code in this commit.
