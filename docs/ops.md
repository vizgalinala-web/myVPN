# MyVPN WireGuard node ops

Keeps File-provisioner peer JSON aligned with `wg set` on a VPN node. Default is **dry-run**. Apply is env-gated and never uses a shell.

## Safety

- Dry-run first.
- `--apply` requires `MYVPN_WG_SYNC_ALLOW_APPLY=1`.
- `wg-peer-sync` starts `wg` via Process (no `sh -c`).
- Interface names: ASCII alnum / `_` `-` `.`, max 15 characters (same as the planner).

## One-shot bootstrap

From the repo root (does **not** apply until you pass `--apply`):

```bash
export MYVPN_PEER_STATE=/var/lib/myvpn/peer-state
export MYVPN_WG_INTERFACE=wg0
sudo install -d -m 700 "$MYVPN_PEER_STATE"
bash scripts/vpn-node-check.sh --peer-state "$MYVPN_PEER_STATE" --interface "$MYVPN_WG_INTERFACE"
bash scripts/vpn-node-bootstrap.sh --peer-state "$MYVPN_PEER_STATE" --interface "$MYVPN_WG_INTERFACE"
```

Apply once (dangerous — only on the node that owns `wg0`):

```bash
export MYVPN_WG_SYNC_ALLOW_APPLY=1
bash scripts/vpn-node-bootstrap.sh --peer-state "$MYVPN_PEER_STATE" --interface "$MYVPN_WG_INTERFACE" --apply
```

## Node layout

1. API uses `Vpn__PeerProvisioner=File` and writes JSON into a shared directory.
2. This host mounts that directory (`/var/lib/myvpn/peer-state`).
3. `wg-peer-sync --watch --apply` keeps peers aligned.

## Manual dry-run

```bash
dotnet run --project tools/wg-peer-sync -- /var/lib/myvpn/peer-state
```

## Apply once (tool only)

```bash
export MYVPN_WG_SYNC_ALLOW_APPLY=1
# optional: export MYVPN_WG_BIN=/usr/bin/wg
dotnet run --project tools/wg-peer-sync -- /var/lib/myvpn/peer-state --interface wg0 --apply
```

## systemd

Unit: `deploy/systemd/myvpn-wg-peer-sync.service`.

```bash
sudo bash scripts/vpn-node-bootstrap.sh --install-systemd --prefix /opt/myvpn
# then edit ExecStart/WorkingDirectory if the published build path differs
sudo systemctl daemon-reload
sudo systemctl enable --now myvpn-wg-peer-sync.service
journalctl -u myvpn-wg-peer-sync -f
```

Run as a user that can call `wg` (root or `CAP_NET_ADMIN`). The unit sets `ProtectHome=true` and `ReadWritePaths=/var/lib/myvpn/peer-state`.

## Preflight in CI

```bash
bash scripts/vpn-node-check.sh --self-test
```
