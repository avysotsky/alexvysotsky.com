#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import json
from pathlib import Path

import pyarrow.parquet as pq

ROOT = Path("/home/user/VAN-v2/data/arbitrage_orderbook_history")

COLUMNS = [
    "ts_ms", "ts_utc", "coin", "exchange", "symbol", "bid_ask_median", "funding_rate", "funding_pct",
    "bid_px_1", "bid_sz_1", "bid_px_2", "bid_sz_2", "bid_px_3", "bid_sz_3", "bid_px_4", "bid_sz_4", "bid_px_5", "bid_sz_5",
    "ask_px_1", "ask_sz_1", "ask_px_2", "ask_sz_2", "ask_px_3", "ask_sz_3", "ask_px_4", "ask_sz_4", "ask_px_5", "ask_sz_5",
]


def parse_ts(value: str) -> dt.datetime:
    raw = str(value or "").strip()
    if not raw:
        raise ValueError("ts is required")
    if raw.isdigit():
        n = int(raw)
        if n > 10_000_000_000:
            return dt.datetime.fromtimestamp(n / 1000, tz=dt.timezone.utc)
        return dt.datetime.fromtimestamp(n, tz=dt.timezone.utc)
    if raw.endswith("Z"):
        raw = raw[:-1] + "+00:00"
    parsed = dt.datetime.fromisoformat(raw)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=dt.timezone.utc)
    return parsed.astimezone(dt.timezone.utc)


def hour_key(ts: dt.datetime) -> str:
    return ts.strftime("%Y%m%dT%H0000Z")


def candidate_paths(ts: dt.datetime):
    seen = set()
    for delta_hours in (-1, 0, 1):
        hour = ts + dt.timedelta(hours=delta_hours)
        folder = ROOT / hour.strftime("%Y") / hour.strftime("%m") / hour.strftime("%d")
        key = hour_key(hour)
        for suffix in (".parquet", ".jsonl.inprogress"):
            path = folder / f"arbitrage_orderbook_{key}{suffix}"
            if path.exists() and path not in seen:
                seen.add(path)
                yield path


def read_parquet_rows(path: Path, start_ms: int, end_ms: int):
    table = pq.read_table(path, columns=COLUMNS, filters=[("ts_ms", ">=", start_ms), ("ts_ms", "<=", end_ms)])
    return table.to_pylist()


def read_jsonl_rows(path: Path, start_ms: int, end_ms: int):
    rows = []
    with path.open("r", encoding="utf-8") as fh:
        for line in fh:
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue
            ts_ms = int(row.get("ts_ms") or 0)
            if start_ms <= ts_ms <= end_ms:
                rows.append(row)
    return rows


def normalize_row(row):
    out = {key: row.get(key) for key in COLUMNS}
    levels = []
    for level in range(1, 6):
        levels.append({
            "level": level,
            "bid_px": row.get(f"bid_px_{level}"),
            "bid_sz": row.get(f"bid_sz_{level}"),
            "ask_px": row.get(f"ask_px_{level}"),
            "ask_sz": row.get(f"ask_sz_{level}"),
        })
    out["levels"] = levels
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--ts", required=True)
    parser.add_argument("--tolerance-ms", type=int, default=6000)
    args = parser.parse_args()

    target = parse_ts(args.ts)
    target_ms = int(target.timestamp() * 1000)
    tolerance_ms = max(0, min(int(args.tolerance_ms), 300_000))
    start_ms = target_ms - tolerance_ms
    end_ms = target_ms + tolerance_ms

    rows = []
    files = []
    for path in candidate_paths(target):
        try:
            if path.suffix == ".parquet":
                part = read_parquet_rows(path, start_ms, end_ms)
            else:
                part = read_jsonl_rows(path, start_ms, end_ms)
        except Exception:
            continue
        if part:
            files.append(str(path))
            rows.extend(part)

    if not rows:
        print(json.dumps({
            "ok": True,
            "target_ts_utc": target.isoformat(timespec="milliseconds").replace("+00:00", "Z"),
            "matched_ts_utc": None,
            "delta_ms": None,
            "rows": [],
            "files": files,
        }, separators=(",", ":")))
        return

    by_ts = {}
    for row in rows:
        ts_ms = int(row.get("ts_ms") or 0)
        by_ts.setdefault(ts_ms, []).append(row)
    matched_ms = min(by_ts, key=lambda ts: abs(ts - target_ms))
    matched_rows = sorted(by_ts[matched_ms], key=lambda r: (str(r.get("exchange") or ""), str(r.get("coin") or "")))
    matched_dt = dt.datetime.fromtimestamp(matched_ms / 1000, tz=dt.timezone.utc)
    exchanges = {}
    for row in matched_rows:
        exchange = str(row.get("exchange") or "")
        coin = str(row.get("coin") or "")
        exchanges.setdefault(exchange, []).append(coin)

    print(json.dumps({
        "ok": True,
        "target_ts_utc": target.isoformat(timespec="milliseconds").replace("+00:00", "Z"),
        "matched_ts_utc": matched_dt.isoformat(timespec="milliseconds").replace("+00:00", "Z"),
        "delta_ms": matched_ms - target_ms,
        "row_count": len(matched_rows),
        "exchange_count": len(exchanges),
        "exchanges": {k: sorted(v) for k, v in sorted(exchanges.items())},
        "rows": [normalize_row(row) for row in matched_rows],
        "files": files,
    }, separators=(",", ":")))


if __name__ == "__main__":
    main()
