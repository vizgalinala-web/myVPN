# MyVPN

Privacy-focused VPN product. This repository currently delivers **Phase 3**: Backend API, PostgreSQL, authentication, and WireGuard client configuration issuance.

## Current stack

- **.NET 8** (`net8.0`)
- ASP.NET Core Web API
- Entity Framework Core + PostgreSQL (Npgsql)
- JWT access tokens + opaque refresh tokens (hashed at rest)
- Argon2id password hashing (not ASP.NET Identity)

## Repository layout

```
backend/
├── MyVPN.Api/              # HTTP API, auth middleware, composition root
├── MyVPN.Application/      # Use cases, DTOs, validation
├── MyVPN.Domain/           # Entities and enums
├── MyVPN.Infrastructure/   # EF Core, PostgreSQL, crypto, seed
└── tests/
    ├── MyVPN.UnitTests/
    └── MyVPN.IntegrationTests/
docs/
├── architecture.md
├── backend.md
└── security.md
docker-compose.yml
.env.example
```

## Quick start

### Prerequisites

- .NET SDK 8.x
- Docker (for PostgreSQL and integration tests)

### Restore / build / test

```bash
dotnet restore backend/MyVPN.sln
dotnet build backend/MyVPN.sln
dotnet test backend/MyVPN.sln
```

### PostgreSQL (Development)

```bash
cp .env.example .env
docker compose up -d postgres
```

### Migrations

```bash
dotnet ef database update \
  --project backend/MyVPN.Infrastructure \
  --startup-project backend/MyVPN.Api
```

Create a new migration (when the model changes):

```bash
dotnet ef migrations add <Name> \
  --project backend/MyVPN.Infrastructure \
  --startup-project backend/MyVPN.Api \
  --output-dir Persistence/Migrations
```

In **Development**, the API also applies pending migrations on startup. Production must apply migrations explicitly via ops — no automatic destructive migrations.

### Run API

```bash
dotnet run --project backend/MyVPN.Api
```

Or full stack:

```bash
docker compose up --build
```

- API: `http://localhost:8080` (compose) or the URL printed by `dotnet run`
- Swagger UI: Development only (`/swagger`)
- Health: `GET /health`, readiness: `GET /health/ready`

### Configuration

Copy placeholders:

- `.env.example` → `.env`
- `backend/MyVPN.Api/appsettings.Development.json.example` → local overrides as needed

Primary environment variables:

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string |
| `Jwt__Issuer` / `Jwt__Audience` / `Jwt__SigningKey` | JWT settings |
| `Jwt__AccessTokenLifetimeMinutes` | Access token lifetime (default 15) |
| `RefreshTokens__LifetimeDays` | Refresh token lifetime |
| `Cors__AllowedOrigins__0` | Allowed browser origin (optional) |

Do **not** commit production secrets.

## Phase 2 endpoints

| Method | Path | Auth |
|---|---|---|
| POST | `/api/auth/register` | Public |
| POST | `/api/auth/login` | Public |
| POST | `/api/auth/refresh` | Public (refresh token body) |
| POST | `/api/auth/logout` | Public (refresh token body) |
| GET | `/api/me` | Bearer |
| GET | `/api/servers` | Public (enabled only) |
| GET | `/api/devices` | Bearer |
| GET | `/api/devices/{id}` | Bearer |
| GET | `/api/devices/{id}/connections` | Bearer |
| POST | `/api/devices` | Bearer |
| GET | `/api/devices/{id}/configuration?serverId=` | Bearer |
| POST | `/api/devices/{id}/disconnect` | Bearer |
| DELETE | `/api/devices/{id}` | Bearer |

See [docs/backend.md](docs/backend.md) and [docs/security.md](docs/security.md).

## Out of scope (current)

- iOS / Windows VPN clients
- Production WireGuard node automation (`wg set` over SSH/API)
- Kill Switch / DNS leak protection
- Payments, admin UI, traffic collection

## Documentation

- [Architecture](docs/architecture.md)
- [Backend guide](docs/backend.md)
- [WireGuard provisioning](docs/wireguard.md)
- [Security](docs/security.md)
