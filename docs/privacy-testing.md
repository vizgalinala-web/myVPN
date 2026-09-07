# Privacy testing (MyVPN)

Privacy by Design / data minimization checklist for releases.  
Record pass/fail evidence in this document or linked CI artifacts.

## Backend data model

| Check | Expected | How to verify |
|---|---|---|
| User fields minimal | `Id`, `Email`, `PasswordHash`, `CreatedAt`, `UpdatedAt`, `IsActive` only | Inspect `User` entity |
| No client IP persistence | No `LastLoginIp`, `CreatedByIp`, `RevokedByIp` | DB schema + migrations |
| No traffic/DNS/URL tables | No `traffic_logs`, `dns_logs`, `connection_history` | Schema review |
| Refresh token fields | Hash + rotation metadata only | `RefreshToken` entity |
| Device public key unique | DB unique index on `devices.public_key` | Migration review |
| Device limit | Max 5, `409` + `DEVICE_LIMIT_REACHED` | API + concurrent integration test |
| Disabled device slot rule | `IsActive=false` still counts until delete | Unit test |
| Account deletion | `DELETE /api/account` removes user/devices/tokens/peers | API + unit + integration tests |

## Logging

| Check | Expected |
|---|---|
| No Authorization/JWT/refresh/password in logs | Middleware + services |
| No VPN private keys in logs | API + clients |
| No DNS/URL/destination IP history | No collectors |
| Technical logs only | startup/health/errors with `traceId` |

## VPN nodes

| Check | Expected |
|---|---|
| No packet/DNS/HTTP proxy logging | `docs/ops.md` |
| No DPI / traffic profiling | Architecture review |
| `wg-peer-sync` dry-run default | Tool docs |

## Clients

| Check | Expected |
|---|---|
| Device ID = random UUID (app-generated) | Future app targets |
| WireGuard keys local only | CLI/SDK behaviour |
| Local .conf / session.json owner-only | Unix mode 600; gitignored |
| No analytics/tracking SDKs | Dependency review |
| iOS: no extra sensitive permissions | `Info.plist` / entitlements when app ships |
| iOS privacy manifest accurate | `clients/ios/Sources/MyVPNApi/PrivacyInfo.xcprivacy` |
| No device fingerprinting | Code review |

## Automated checks (CI)

```bash
dotnet test backend/MyVPN.sln
bash scripts/secret-scan.sh
```

Manual grep review before release (see requirement #28 in product spec):

`password`, `token`, `secret`, `privateKey`, `analytics`, `tracking`, `Sentry`, `Firebase`, `IDFA`, `location`, `contacts`, …

## Network leak tests (pre-production)

| Test | Pass criteria |
|---|---|
| IPv4 leak | Public IP = VPN server IP when connected |
| IPv6 leak | No bypass if IPv6 unsupported |
| DNS leak | Resolver = configured VPN DNS |
| WebRTC leak | Browser tests where applicable |
| Disconnect | Normal routing restored |
| Unexpected disconnect + Kill Switch | Policy behaviour (when implemented) |

## iOS pre-release

- Settings → Privacy & Security → App Privacy Report
- Privacy manifest matches behaviour
- Third-party SDK privacy manifests reviewed
- No unexpected outbound domains

## Definition of Done

Privacy requirements are **not** done from code review alone. Required:

- Code + dependency review
- Static + secret scan
- DB inspection (no IP/history tables)
- Log inspection
- Concurrent device-limit integration test (PostgreSQL)
- Leak tests before production
- iOS App Privacy Report (when app ships)
