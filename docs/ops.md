# MyVPN WireGuard peer sync (Phase 7 ops)

Watches File provisioner peer-state and applies `wg set` on this VPN node.

## Safety

- Default dry-run is preferred for first bring-up.
- Apply mode requires `MYVPN_WG_SYNC_ALLOW_APPLY=1`.
- The tool runs `wg` via Process (no shell) and validates interface / public keys.

## Node layout

1. API writes peer JSON when `Vpn__PeerProvisioner=File` into a shared directory.
2. This host mounts/syncs that directory (NFS, rsync, local disk, etc.).
3. `wg-peer-sync --watch --apply` keeps WireGuard peers aligned.

## Manual dry-run

```bash
dotnet run --project tools/wg-peer-sync -- /var/lib/myvpn/peer-state
```

## Apply once

```bash
export MYVPN_WG_SYNC_ALLOW_APPLY=1
# optional: export MYVPN_WG_BIN=/usr/bin/wg
dotnet run --project tools/wg-peer-sync -- /var/lib/myvpn/peer-state --interface wg0 --apply
```

## systemd

See `deploy/systemd/myvpn-wg-peer-sync.service`.

```bash
sudo install -d /var/lib/myvpn/peer-state
sudo cp deploy/systemd/myvpn-wg-peer-sync.service /etc/systemd/system/
# edit WorkingDirectory / paths / User as needed
sudo systemctl daemon-reload
sudo systemctl enable --now myvpn-wg-peer-sync.service
journalctl -u myvpn-wg-peer-sync -f
```

Run as a user that can call `wg` (typically root or a capability-bounded service account).
