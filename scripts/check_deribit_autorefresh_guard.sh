#!/usr/bin/env bash
set -euo pipefail
ROOT="${1:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
HTML="$ROOT/webclient/dist/deribit.html"
fail(){ echo "DERIBIT_AUTO_REFRESH_GUARD_FAIL: $*" >&2; exit 1; }
[[ -f "$HTML" ]] || fail "missing $HTML"
grep -q "DERIBIT_AUTO_REFRESH_GUARD" "$HTML" || fail "missing guard marker"
grep -q "function ccStartDeribitAutoRefresh" "$HTML" || fail "missing ccStartDeribitAutoRefresh"
grep -q "function ccRefreshDeribitVisibleData" "$HTML" || fail "missing ccRefreshDeribitVisibleData"
grep -q "setInterval(async()=>{if(document.hidden||ccDeribitAutoRefreshBusy||!ccAccountsCache.length)return" "$HTML" || fail "missing 60s Deribit interval gate"
grep -q "try{await ccRefreshDeribitVisibleData()}finally{ccDeribitAutoRefreshBusy=false}},60000)" "$HTML" || fail "Deribit interval no longer calls visible-data refresh every 60s"
grep -q "async function ccRefreshDeribitVisibleData(){const tasks=\[\];tasks.push(summary())" "$HTML" || fail "visible-data refresh must call summary() directly"
grep -q "function ccStartPnlAutoRefresh" "$HTML" || fail "missing ccStartPnlAutoRefresh"
grep -q "try{await loadCcPnl()}finally{ccPnlRefreshBusy=false}},60000)" "$HTML" || fail "PnL interval no longer calls loadCcPnl every 60s"
echo "DERIBIT_AUTO_REFRESH_GUARD_OK"
