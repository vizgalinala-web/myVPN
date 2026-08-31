# Backend (Phase 2)

## SDK / runtime

- .NET SDK **8.x**
- Target: `net8.0`
- EF Core / Npgsql **8.0.x**
- Tool: `dotnet-ef` 8.0.x

```bash
dotnet --version
dotnet tool install --global dotnet-ef --version 8.0.11
```

## PostgreSQL

```bash
cp .env.example .env
docker compose up -d postgres
docker compose ps
```

Default development connection (placeholders only):

`Host=localhost;Port=5432;Database=myvpn;Username=myvpn;Password=myvpn_dev_password`

Standard configuration key: **`ConnectionStrings:Default`** / env `ConnectionStrings__Default`.

## Run API

```bash
dotnet restore backend/MyVPN.sln
dotnet run --project backend/MyVPN.Api
```

Development auto-applies pending migrations and seeds VPN servers.

Compose:

```bash
docker compose up --build
```

## Migrations

```bash
dotnet ef migrations add <Name> \
  --project backend/MyVPN.Infrastructure \
  --startup-project backend/MyVPN.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project backend/MyVPN.Infrastructure \
  --startup-project backend/MyVPN.Api
```

Initial migration: `InitialCreate`.

Production: apply migrations explicitly. Do not rely on startup auto-migrate outside Development.

## Seed (Development only)

Idempotent seed of four enabled servers:

| Name | Country | City | Hostname |
|---|---|---|---|
| Germany 1 | DE | Frankfurt | de1.example.com |
| Netherlands 1 | NL | Amsterdam | nl1.example.com |
| United Kingdom 1 | GB | London | uk1.example.com |
| USA 1 | US | New York | us1.example.com |

Placeholder public keys only. No production credentials. No default test user with a known password.

## Authentication

### Register `POST /api/auth/register`

- Normalizes email (trim + invariant lower-case)
- Password min 12 / max 128; rejects common weak passwords
- Duplicate email → **409** `EMAIL_CONFLICT` (explicit conflict; success path already reveals ownership of an email)
- **201** with `{ id, email, createdAt }`

### Login `POST /api/auth/login`

- Unknown email and wrong password share **401** `INVALID_CREDENTIALS` + identical detail
- Inactive user → **403** `USER_INACTIVE`
- Returns `{ accessToken, refreshToken, expiresIn, tokenType }`

### Refresh `POST /api/auth/refresh`

1. Hash incoming token
2. Load row; reject if missing/expired
3. If already revoked → revoke **entire token family** (`REFRESH_TOKEN_REUSED`) and reject
4. Otherwise revoke old token, issue new access + refresh, set `ReplacedByTokenId`

### Logout `POST /api/auth/logout`

- Body refresh token; **no Bearer required**
- Idempotent **204**
- Revokes refresh token only
- Access token remains valid until `exp` (no blacklist in Phase 2)

## Endpoints & status codes

| Endpoint | Success | Notes |
|---|---|---|
| POST /api/auth/register | 201 | 400 validation, 409 conflict, 429 |
| POST /api/auth/login | 200 | 401, 403 inactive, 429 |
| POST /api/auth/refresh | 200 | 401, 429 |
| POST /api/auth/logout | 204 | 429 |
| GET /api/me | 200 | 401 |
| GET /api/servers | 200 | **Public**; enabled only |
| GET /api/devices | 200 | 401 |
| POST /api/devices | 201 | 400, 401, 409 |
| DELETE /api/devices/{id} | 204 | 401, 404 (also for foreign devices) |
| GET /health | 200 | liveness |
| GET /health/ready | 200/503 | PostgreSQL |

## Error format (RFC 7807 Problem Details)

```json
{
  "type": "https://api.myvpn.example/errors/invalid-credentials",
  "title": "Invalid credentials",
  "status": 401,
  "detail": "Invalid email or password.",
  "code": "INVALID_CREDENTIALS",
  "traceId": "00-..."
}
```

Validation errors include an `errors` object. Production responses never include stack traces or SQL text.

## Rate limiting

Built-in ASP.NET Core fixed-window policies (configurable under `RateLimiting`):

| Policy | Default | Partition |
|---|---|---|
| register | 5 / 60s | IP |
| login | 10 / 60s | IP (+ optional `X-RateLimit-Email` hint) |
| refresh | 20 / 60s | IP |
| logout | 20 / 60s | IP |

HTTP **429** with Problem Details. Rate limiting is **not** DDoS protection.

## `/api/servers` auth policy

**Public** for MVP — server list is not considered private. Only `Enabled = true` rows are returned. Response omits `PublicKey`, `VpnNetwork`, and admin fields.

## Devices

- Ownership enforced in `DeviceService` (not only controllers)
- Foreign device delete/list → empty / **404**
- WireGuard public key validated as strict Base64 of 32 bytes
- `VpnAddress` remains `null` until Phase 3 provisioning
- Private keys are never accepted or returned

## Tests

```bash
dotnet test backend/MyVPN.sln
```

- Unit tests: in-memory EF + application services
- Integration tests: **Testcontainers PostgreSQL** (requires Docker). If Docker is unavailable, skippable facts are skipped.

## Phase 2 limitations

- No WireGuard config issuance / server provisioning
- No access-token revocation list
- No email verification / password reset
- No admin API for servers (seed/internal only)
- No iOS/Windows clients
