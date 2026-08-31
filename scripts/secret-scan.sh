#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

scan() {
  local label="$1"
  local pattern="$2"
  if rg -n -e "$pattern" \
    --glob '!.git/**' \
    --glob '!**/bin/**' \
    --glob '!**/obj/**' \
    --glob '!docs/privacy-testing.md' \
    --glob '!clients/windows/**' \
    --glob '!**/*Tests/**' \
    .; then
    echo "Secret scan failed: $label"
    exit 1
  fi
}

scan 'private key block' '-----BEGIN (RSA |OPENSSH |EC )?PRIVATE KEY-----'
scan 'github classic pat' 'ghp_[A-Za-z0-9]{20,}'
scan 'github fine-grained pat' 'github_pat_[A-Za-z0-9_]{20,}'
scan 'aws access key' 'AKIA[0-9A-Z]{16}'
scan 'wireguard private key assignment' 'PrivateKey\s*=\s*[A-Za-z0-9+/]{43}='

echo 'Secret scan passed.'
