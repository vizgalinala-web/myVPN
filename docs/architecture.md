# Architecture

## Phase status

- **Phase 1**: Solution skeleton, layered projects, documentation (bootstrap)
- **Phase 2** (current): Backend API, PostgreSQL, authentication, devices/servers read API
- **Phase 3+**: WireGuard provisioning, native clients, Kill Switch, DNS leak protection (not started)

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
