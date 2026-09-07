# Security

## Privacy by Design

MyVPN follows **data minimization**: only data required to operate the VPN account and connection is stored.

- No browsing/DNS/URL/traffic history tables
- No persistent client IP storage (`CreatedByIp` / `RevokedByIp` removed from refresh tokens)
- No user analytics / RUM / advertising SDKs in MVP backend
- Device limit enforced server-side (`409 DEVICE_LIMIT_REACHED`, atomic under PostgreSQL)
- Registered device = occupies one of 5 slots until **deleted** (`IsActive=false` does not free a slot)
- WireGuard **private keys** never stored on backend

See [privacy-testing.md](privacy-testing.md) for the release checklist.

## Phase 2 decisions

| Topic | Decision |
|---|---|
| Identity | Custom `User` entity — **not** ASP.NET Identity (simpler MVP surface) |
| Passwords | **Argon2id** via Konscious; never stored or logged in plaintext |
| Access tokens | JWT HS256, ~15 minutes; claims limited to `sub/jti/iat/exp/iss/aud` |
| Refresh tokens | Opaque random 32 bytes; only hash stored (HMAC-SHA256 with pepper when configured) |
| Rotation | Every refresh revokes previous token and links `ReplacedByTokenId` |
| Replay | Reuse of revoked refresh token revokes entire `TokenFamilyId` chain |
| Logout | Revokes refresh token; access token valid until expiry (documented) |
| Email enumeration (login) | Neutral `INVALID_CREDENTIALS` |
| Email conflict (register) | Explicit `409 EMAIL_CONFLICT` |
| Device limit | `409 DEVICE_LIMIT_REACHED` (not 403) |
| Ownership | Filtered by `UserId` in application services |
| Secrets | Env vars / user secrets; placeholders in examples only |
| Swagger | Development only |
| CORS | Configured origins only; empty list disables CORS middleware |
| Production JWT | Startup fails if signing key missing, &lt; 32 chars, or placeholder-like |
| Logging | Structured; no passwords, tokens, Authorization headers, or private keys |

## Rate limiting disclaimer

Application rate limits reduce credential stuffing noise. They are **not** a substitute for edge DDoS protection, WAF, or network controls. Rate limiting may use client IP ephemerally in memory — it is **not** persisted.

## Security TODO (later phases)

- [ ] Secret management (vault / cloud secret store)
- [ ] HTTPS certificates (managed TLS termination)
- [ ] Database backup and restore drills
- [ ] Monitoring (RED/USE metrics, dependency health) — **no per-user profiling**
- [ ] Alerting on auth anomalies and readiness failures
- [ ] JWT / refresh pepper key rotation procedure
- [ ] Incident response runbooks
- [ ] Abuse prevention beyond basic rate limits
- [ ] Audit logging for admin and auth-sensitive events (minimal, no traffic content)
- [ ] DDoS protection at the edge
- [ ] Account recovery flows
- [ ] Email verification
- [ ] Password reset
- [ ] Refresh token cleanup job (expired/revoked rows) — **implemented** (`RefreshTokenCleanup` hosted service)
- [ ] Administrative access control for server management
- [ ] Access-token denylist or short-lived tokens + introspection (if required)
- [ ] WireGuard key lifecycle and server private-key storage outside DB
- [x] `DELETE /api/account` — account erasure (see [backend.md](backend.md))
