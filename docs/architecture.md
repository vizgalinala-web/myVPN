# Architecture

## Phase status

- **Phase 1**: Solution skeleton, layered projects, documentation (bootstrap)
- **Phase 2**: Backend API, PostgreSQL, authentication, devices/servers read API
- **Phase 3**: WireGuard client configuration, IP allocation, disconnect, device limits, password change, refresh cleanup, file peer-state adapter
- **Phase 4**: Windows API client/CLI scaffold, iOS Swift API package scaffold, `wg-peer-sync` helper
- **Phase 5**: Peer-sync remove detection + watch mode, Windows client unit tests/CLI expansion, Compose File peer-state wiring, GitHub Actions CI
- **Phase 6**: Safe `wg` apply executor (env-gated, no shell), peer-sync planner tests, CLI `--out` config save
- **Phase 7**: VPN-node ops docs + systemd unit, client tunnel import guide
- **Phase 8**: Privacy-by-design (no client IP persistence, no traffic/DNS history)
- **Phase 9**: `DELETE /api/account` erasure (user, devices, refresh tokens, VPN peers) + full-tunnel/Kill Switch import notes
- **Phase 10**: CLI `connect` / `tunnel-up` / `tunnel-down` via WireGuard for Windows (`/installtunnelservice`) or `wg-quick` (no shell)
- **Phase 11**: local `session.json` (no secrets) plus CLI `status` / `stop`
- **Phase 12**: owner-only local `.conf`/`session.json` (Unix 600), gitignore, `status --json`, `stop --forget`, CLI `change-password`
- **Phase 13**: VPN-node preflight (`vpn-node-check.sh`) and bootstrap (`vpn-node-bootstrap.sh`); apply still env-gated
- **Phase 14** (current): Windows desktop client (`MyVPN.Client.App`) — Connect/Stop UI over the existing SDK and WireGuard helper
- **Later**: iOS Network Extension / in-process Wintun, automated Kill Switch enforcement

See [wireguard.md](wireguard.md), [ops.md](ops.md), [clients-tunnel.md](clients-tunnel.md), and [../clients/README.md](../clients/README.md).

## .NET version

Target framework: **`net8.0`** (.NET 8 LTS).

## Layered structure

| Project | Responsibility |
|---|---|
| `MyVPN.Domain` | Entities (`User`, `Device`, `VpnServer`, `RefreshToken`), enums |
| `MyVPN.Application` | DTOs, validators, application services, abstractions |
| `MyVPN.Infrastructure` | EF Core `MyVpnDbContext`, repositories, Argon2id, JWT/refresh crypto, seed |
| `MyVPN.Api` | Controllers, middleware, JWT auth, rate limiting, health, Swagger (Dev), DI composition |

No generic repository pattern — repositories are purpose-built for current use cases.

## Auth model

Custom user store (not ASP.NET Identity):

- Passwords hashed with **Argon2id**
- Short-lived **JWT** access tokens (`sub`, `jti`, `iat`, `exp`, `iss`, `aud`)
- Opaque **refresh tokens** stored as HMAC/SHA-256 hashes
- Refresh **rotation** + **family revocation** on reuse (`TokenFamilyId`)

## Data

PostgreSQL via Npgsql EF Core provider. Single `MyVpnDbContext`. Schema evolves through EF migrations only (no `EnsureCreated` in Production).

## Deviations

Phase 1 was missing when this work started; the layered backend structure above was created as the Phase 1 foundation and immediately extended with Phase 2 behaviour — no parallel duplicate architecture.
