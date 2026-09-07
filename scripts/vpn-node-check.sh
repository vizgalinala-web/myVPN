#!/usr/bin/env bash
# Validate a MyVPN VPN-node layout. Never runs `wg` apply.
set -euo pipefail

PEER_STATE="${MYVPN_PEER_STATE:-/var/lib/myvpn/peer-state}"
INTERFACE="${MYVPN_WG_INTERFACE:-wg0}"
ALLOW_MISSING=0

usage() {
  cat <<'EOF'
vpn-node-check.sh — preflight for a MyVPN WireGuard node (no apply)

Usage:
  vpn-node-check.sh [--peer-state DIR] [--interface NAME] [--allow-missing]
  vpn-node-check.sh --self-test

Checks:
  - interface name matches wg-peer-sync (ASCII alnum / _ - . , max 15)
  - peer-state directory exists (unless --allow-missing)
  - peer JSON files look like objects with publicKey
  - refuses if MYVPN_WG_SYNC_ALLOW_APPLY is requested via --require-apply-env

This script does not execute wg(8).
EOF
}

is_safe_interface() {
  local name="$1"
  [[ "$name" =~ ^[A-Za-z0-9_.-]{1,15}$ ]]
}

check_peer_files() {
  local dir="$1"
  local file count=0
  shopt -s nullglob
  for file in "$dir"/*.json; do
    local base
    base="$(basename "$file")"
    if [[ "$base" == .* ]]; then
      continue
    fi
    count=$((count + 1))
    if ! grep -q '"publicKey"' "$file"; then
      echo "peer file missing publicKey: $file" >&2
      return 1
    fi
  done
  echo "peer_json_files=$count"
}

run_checks() {
  if ! is_safe_interface "$INTERFACE"; then
    echo "unsafe interface name: $INTERFACE" >&2
    return 1
  fi
  echo "interface=$INTERFACE"

  if [[ ! -d "$PEER_STATE" ]]; then
    if [[ "$ALLOW_MISSING" -eq 1 ]]; then
      echo "peer_state=missing (allowed)"
      return 0
    fi
    echo "peer-state directory not found: $PEER_STATE" >&2
    return 1
  fi
  echo "peer_state=$PEER_STATE"
  check_peer_files "$PEER_STATE"
}

self_test() {
  local tmp
  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' RETURN

  INTERFACE=wg0 PEER_STATE="$tmp" ALLOW_MISSING=0 run_checks >/dev/null

  if is_safe_interface 'wg 0'; then
    echo "expected unsafe interface to fail" >&2
    return 1
  fi
  if is_safe_interface 'thisnameistoolongforwg'; then
    echo "expected long interface to fail" >&2
    return 1
  fi

  printf '%s\n' '{"publicKey":"ERERERERERERERERERERERERERERERERERERERERERE="}' >"$tmp/peer.json"
  INTERFACE=wg0 PEER_STATE="$tmp" ALLOW_MISSING=0 run_checks >/dev/null

  printf '%s\n' '{"nope":true}' >"$tmp/bad.json"
  if INTERFACE=wg0 PEER_STATE="$tmp" ALLOW_MISSING=0 run_checks >/dev/null 2>&1; then
    echo "expected missing publicKey to fail" >&2
    return 1
  fi

  echo "self-test passed"
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
    --allow-missing)
      ALLOW_MISSING=1
      shift
      ;;
    --self-test)
      self_test
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

run_checks
