#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

forbidden_files="$({
  find . -type f \
    \( -name '.env' -o -name '.env.*' -o -name 'appsettings.json' \
       -o -name '*.pem' -o -name '*.key' -o -name '*.p12' -o -name '*.pfx' \
       -o -name '*.db' -o -name '*.sqlite' -o -name '*.dump' -o -name '*.sql' \
       -o -name '*.csv' -o -name '*.parquet' -o -name '*.ndjson' -o -name '*.log' \) \
    -not -path './.git/*'
} || true)"

if [[ -n "$forbidden_files" ]]; then
  echo "Forbidden files detected:"
  echo "$forbidden_files"
  exit 1
fi

if grep -RIlE \
  --exclude-dir=.git \
  --exclude='verify-public-snapshot.sh' \
  'BEGIN (RSA |EC |OPENSSH |DSA )?PRIVATE KEY|BEGIN PGP PRIVATE KEY|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}' \
  . | grep -q .; then
  echo "Credential-like material detected."
  exit 1
fi

if command -v gitleaks >/dev/null 2>&1; then
  gitleaks dir . --redact --no-banner
else
  echo "Warning: gitleaks is unavailable; built-in checks only."
fi

echo "Snapshot safety checks passed."
