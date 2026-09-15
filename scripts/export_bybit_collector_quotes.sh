#!/usr/bin/env bash
set -euo pipefail

START_DATE="${1:-2026-05-29}"
END_DATE="${2:-$(date -u +%F)}"
DEST_DIR="${3:-/mnt/d/QuotesArchive_Futures/Bybit}"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-van-postgres}"
POSTGRES_USER="${POSTGRES_USER:-van_app}"
POSTGRES_DB="${POSTGRES_DB:-van_db}"
POSTGRES_PORT="${POSTGRES_PORT:-5434}"
PYARROW_VENV="${PYARROW_VENV:-/home/user/.cache/openclaw-coincall-parquet-venv}"
HISTORICAL_CUTOFF="2026-05-28"

if [[ "$START_DATE" < "$HISTORICAL_CUTOFF" || "$START_DATE" == "$HISTORICAL_CUTOFF" ]]; then
  echo "Refusing to export collector files for ${START_DATE}: historical public backfill owns dates through ${HISTORICAL_CUTOFF} inclusive." >&2
  exit 2
fi

if [[ ! -d "$DEST_DIR" ]]; then
  echo "Destination is not mounted or does not exist: ${DEST_DIR}" >&2
  echo "No files written. Mount the Windows D:\\QuotesArchive_Futures\\Bybit path and rerun." >&2
  exit 3
fi

ensure_pyarrow() {
  if [[ -x "$PYARROW_VENV/bin/python" ]] && "$PYARROW_VENV/bin/python" - <<'PY' >/dev/null 2>&1
import pyarrow
PY
  then
    return 0
  fi
  python3 -m venv "$PYARROW_VENV"
  "$PYARROW_VENV/bin/python" -m pip install --quiet --upgrade pip
  "$PYARROW_VENV/bin/python" -m pip install --quiet pyarrow
}

ensure_pyarrow
TMP_DIR="$(mktemp -d /tmp/bybit-collector-quotes-XXXXXX)"
trap 'rm -rf "$TMP_DIR"' EXIT

day="$START_DATE"
while [[ "$day" < "$END_DATE" || "$day" == "$END_DATE" ]]; do
  row_count="$(docker exec "$POSTGRES_CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -p "$POSTGRES_PORT" -Atc "select count(*) from public.van_bybit_quote_minute where minute_utc >= '${day}'::date and minute_utc < ('${day}'::date + interval '1 day');")"
  if [[ "$row_count" == "0" ]]; then
    echo "${day}: no collector rows; no file written"
  else
    out_dir="${DEST_DIR}/data/futures/${day}"
    out="${out_dir}/futures_parsed.parquet"
    csv_file="${TMP_DIR}/${day}.csv"
    mkdir -p "$out_dir"
    docker exec "$POSTGRES_CONTAINER" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -p "$POSTGRES_PORT" -c "\\copy (
select
  instrument_name,
  minute_utc as snapshot_minute_utc,
  last_price,
  mark_price,
  bid_price,
  ask_price,
  index_price,
  open,
  high,
  low,
  close,
  volume,
  volume_usd,
  quote_volume,
  current_funding,
  null::numeric as interest_8h,
  display_name,
  ticker_id,
  base_currency,
  quote_currency,
  product_type,
  exchange,
  category as _category,
  symbol as _symbol
from public.van_bybit_quote_minute
where minute_utc >= '${day}'::date
  and minute_utc < ('${day}'::date + interval '1 day')
order by minute_utc, market, category, symbol
) to stdout with csv header" > "$csv_file"
    "$PYARROW_VENV/bin/python" - <<'PY' "$csv_file" "$out" "$day"
import csv, datetime as dt, json, pathlib, sys, time, urllib.parse, urllib.request
import pyarrow as pa
import pyarrow.parquet as pq

UTC = dt.timezone.utc
src = pathlib.Path(sys.argv[1])
dst = pathlib.Path(sys.argv[2])
day = sys.argv[3]
start = dt.datetime.fromisoformat(day).replace(tzinfo=UTC)
start_ms = int(start.timestamp() * 1000)
end_ms = int((start + dt.timedelta(days=1)).timestamp() * 1000)

def to_float(value):
    if value in (None, ""):
        return None
    try:
        return float(value)
    except Exception:
        return None

def bybit_get(path, **params):
    url = "https://api.bybit.com" + path + "?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0 (compatible; VAN quote archive exporter)"})
    with urllib.request.urlopen(req, timeout=30) as res:
        return json.loads(res.read().decode("utf-8"))

def funding_history(category, symbol):
    out = []
    data = bybit_get("/v5/market/funding/history", category=category, symbol=symbol, startTime=start_ms - 7 * 24 * 60 * 60 * 1000, endTime=end_ms - 1, limit=200)
    for row in (data.get("result") or {}).get("list") or []:
        ts = int(row.get("fundingRateTimestamp") or 0)
        val = to_float(row.get("fundingRate"))
        if val is not None:
            out.append((ts, val))
    return sorted(out)

raw_rows = list(csv.DictReader(src.open()))
by_symbol = {}
for row in raw_rows:
    if "Perpetual" in (row.get("product_type") or ""):
        by_symbol.setdefault((row.get("_category"), row.get("_symbol")), []).append(row)

for (category, symbol), rows in by_symbol.items():
    settled = funding_history(category, symbol)
    idx = 0
    last = None
    rows.sort(key=lambda r: r["snapshot_minute_utc"])
    for row in rows:
        ts = dt.datetime.fromisoformat(row["snapshot_minute_utc"].replace(" ", "T")).astimezone(UTC)
        ts_ms = int(ts.timestamp() * 1000)
        while idx < len(settled) and settled[idx][0] <= ts_ms:
            last = settled[idx][1]
            idx += 1
        row["interest_8h"] = "" if last is None else str(last)
    time.sleep(0.08)

cols = [
    ("instrument_name", pa.string()), ("snapshot_minute_utc", pa.timestamp("ms", tz="UTC")),
    ("last_price", pa.float64()), ("mark_price", pa.float64()), ("bid_price", pa.float64()),
    ("ask_price", pa.float64()), ("index_price", pa.float64()), ("open", pa.float64()),
    ("high", pa.float64()), ("low", pa.float64()), ("close", pa.float64()),
    ("volume", pa.float64()), ("volume_usd", pa.float64()), ("quote_volume", pa.float64()),
    ("current_funding", pa.float64()), ("interest_8h", pa.float64()), ("display_name", pa.string()),
    ("ticker_id", pa.string()), ("base_currency", pa.string()), ("quote_currency", pa.string()),
    ("product_type", pa.string()), ("exchange", pa.string()),
]
out_rows = []
for row in raw_rows:
    item = {}
    for name, typ in cols:
        if name == "snapshot_minute_utc":
            item[name] = dt.datetime.fromisoformat(row[name].replace(" ", "T")).astimezone(UTC).replace(second=0, microsecond=0)
        elif pa.types.is_floating(typ):
            item[name] = to_float(row.get(name))
        else:
            item[name] = row.get(name) or None
    out_rows.append(item)
if not out_rows:
    raise SystemExit(f"ERROR: no rows exported from {src}")
pq.write_table(pa.Table.from_pylist(out_rows, schema=pa.schema(cols)), dst, compression="zstd")
PY
    printf 'collector_source=van_bybit_quote_minute\ninterest_8h_source=Bybit /v5/market/funding/history step-held by minute\nexported_at_utc=%s\nrows=%s\n' "$(date -u +%FT%TZ)" "$row_count" > "${out}.source"
    echo "${day}: wrote ${out} rows=${row_count}"
  fi
  day="$(date -u -d "${day} + 1 day" +%F)"
done
