#!/usr/bin/env bash
# Commit daily generated VAN public artifacts after cron refresh.
set -euo pipefail

REPO="/home/user/VAN"
LOG="/home/user/VAN/logs/commit_public_generated_artifacts.log"
LOCK="/tmp/van-public-generated-artifacts-commit.lock"
PATHS=(
  "webclient/dist/downloads/VAN_Combo_1_daily_drawdowns.csv"
  "webclient/dist/downloads/VAN_Combo_1_daily_returns.csv"
  "webclient/dist/trading/og-van-combo-1.jpg"
)

mkdir -p "$(dirname "$LOG")"
exec 9>"$LOCK"
if ! flock -n 9; then
  echo "$(date -u +%FT%TZ) another commit job is running" >> "$LOG"
  exit 0
fi

cd "$REPO"

echo "$(date -u +%FT%TZ) start" >> "$LOG"

if git diff --quiet -- "${PATHS[@]}"; then
  echo "$(date -u +%FT%TZ) no changes in generated artifacts" >> "$LOG"
  exit 0
fi

# Do not mix unrelated work into this automated commit.
git add -f -- "${PATHS[@]}"

if git diff --cached --quiet -- "${PATHS[@]}"; then
  echo "$(date -u +%FT%TZ) no staged generated changes" >> "$LOG"
  exit 0
fi

commit_date="$(date -u +%F)"
git commit -m "Update VAN public generated artifacts ${commit_date}" -- "${PATHS[@]}" >> "$LOG" 2>&1

# Push only after a successful narrow commit. If push fails, leave commit local and log the failure.
if git push >> "$LOG" 2>&1; then
  echo "$(date -u +%FT%TZ) pushed generated artifacts" >> "$LOG"
else
  echo "$(date -u +%FT%TZ) ERROR: git push failed" >> "$LOG"
  exit 1
fi
