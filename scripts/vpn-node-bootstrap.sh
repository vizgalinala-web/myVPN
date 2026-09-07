#!/usr/bin/env bash
# Prepare a MyVPN VPN node: directories, optional systemd unit, dry-run peer sync.
# Does not apply `wg set` unless MYVPN_WG_SYNC_ALLOW_APPLY=1 and --apply is passed.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PEER_STATE="${MYVPN_PEER_STATE:-/var/lib/myvpn/peer-state}"
INTERFACE="${MYVPN_WG_INTERFACE:-wg0}"
PREFIX="${MYVPN_PREFIX:-/opt/myvpn}"
APPLY=0
INSTALL_SYSTEMD=0

usage() {
  cat <<'EOF'
vpn-node-bootstrap.sh — bring-up helper for a MyVPN WireGuard node

Usage:
  vpn-node-bootstrap.sh [--peer-state DIR] [--interface NAME] [--prefix DIR]
                        [--install-systemd] [--apply]

Default: create peer-state dir (if writable), run vpn-node-check, then
wg-peer-sync dry-run when the tool can be built.

  --apply   run wg-peer-sync --apply (requires MYVPN_WG_SYNC_ALLOW_APPLY=1)
  --install-systemd  copy deploy/systemd/myvpn-wg-peer-sync.service to
                     /etc/systemd/system/ (requires root)

Never uses a shell to invoke wg: wg-peer-sync starts wg via Process.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      usage
      exit 0
      ;;
    --peer-state)
      PEER_STATE="$2"
      shift 2
      ;;
    --interface)
      INTERFACE="$2"
      shift 2
      ;;
    --prefix)
      PREFIX="$2"
      shift 2
      ;;
    --apply)
      APPLY=1
      shift
      ;;
    --install-systemd)
      INSTALL_SYSTEMD=1
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

bash "$ROOT/scripts/vpn-node-check.sh" --interface "$INTERFACE" --peer-state "$PEER_STATE" --allow-missing

if mkdir -p "$PEER_STATE" 2>/dev/null; then
  chmod 700 "$PEER_STATE" 2>/dev/null || true
  echo "peer_state_ready=$PEER_STATE"
else
  echo "cannot create $PEER_STATE (need write access or create it as root)" >&2
  exit 1
fi

bash "$ROOT/scripts/vpn-node-check.sh" --interface "$INTERFACE" --peer-state "$PEER_STATE"

if [[ "$INSTALL_SYSTEMD" -eq 1 ]]; then
  if [[ "$(id -u)" -ne 0 ]]; then
    echo "--install-systemd requires root" >&2
    exit 1
  fi
  install -d "$PREFIX"
  cp "$ROOT/deploy/systemd/myvpn-wg-peer-sync.service" /etc/systemd/system/myvpn-wg-peer-sync.service
  echo "systemd_unit=/etc/systemd/system/myvpn-wg-peer-sync.service"
  echo "edit WorkingDirectory/ExecStart to match $PREFIX then: systemctl daemon-reload && systemctl enable --now myvpn-wg-peer-sync"
fi

if [[ "$APPLY" -eq 1 ]]; then
  if [[ "${MYVPN_WG_SYNC_ALLOW_APPLY:-}" != "1" ]]; then
    echo "Refusing --apply. Set MYVPN_WG_SYNC_ALLOW_APPLY=1" >&2
    exit 2
  fi
fi

if command -v dotnet >/dev/null 2>&1; then
  extra=()
  if [[ "$APPLY" -eq 1 ]]; then
    extra+=(--apply)
  fi
  dotnet run --project "$ROOT/tools/wg-peer-sync" -- "$PEER_STATE" --interface "$INTERFACE" "${extra[@]+"${extra[@]}"}"
else
  echo "dotnet not found; skip wg-peer-sync. Install .NET 8 or copy a published build to $PREFIX." >&2
  if [[ "$APPLY" -eq 1 ]]; then
    exit 1
  fi
fi
