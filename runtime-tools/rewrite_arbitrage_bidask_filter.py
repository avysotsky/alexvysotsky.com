#!/usr/bin/env python3
from __future__ import annotations

import argparse
import shutil
from collections import defaultdict
from pathlib import Path

import pyarrow as pa
import pyarrow.parquet as pq

WINDOW = 20
MAX_REL_DEV = 0.01


def avg(values):
    return sum(values) / len(values) if values else None


def filtered(entry, side, raw):
    try:
        value = float(raw)
    except Exception:
        return None
    if value <= 0:
        return None
    vals = entry["values"]
    if len(vals) < WINDOW:
        vals.append(value)
        if len(vals) == WINDOW:
            entry["last"] = value
            return value
        return None
    average = avg(vals)
    vals.append(value)
    if len(vals) > WINDOW:
        del vals[:-WINDOW]
    max_dev = max(abs(average) * MAX_REL_DEV, 1e-8)
    outlier = value < average - max_dev if side == "bid" else value > average + max_dev
    if outlier:
        return entry["last"] if entry["last"] is not None else average
    entry["last"] = value
    return value


def rewrite_file(path: Path, backup_dir: Path):
    table = pq.read_table(path)
    rows = table.to_pylist()
    rows.sort(key=lambda r: (str(r.get("exchange") or ""), str(r.get("symbol") or ""), int(r.get("ts_ms") or 0)))
    state = defaultdict(lambda: {"bid": {"values": [], "last": None}, "ask": {"values": [], "last": None}})
    out = []
    dropped = 0
    adjusted = 0
    for row in rows:
        key = (row.get("exchange"), row.get("symbol"))
        st = state[key]
        bid_raw = row.get("bid_px_1")
        ask_raw = row.get("ask_px_1")
        bid = filtered(st["bid"], "bid", bid_raw)
        ask = filtered(st["ask"], "ask", ask_raw)
        new_row = dict(row)
        if bid is None or ask is None:
            bid = bid_raw
            ask = ask_raw
            dropped += 1
        if bid != bid_raw:
            new_row["bid_px_1"] = bid
            adjusted += 1
        if ask != ask_raw:
            new_row["ask_px_1"] = ask
            adjusted += 1
        try:
            new_row["bid_ask_median"] = (float(bid) + float(ask)) / 2
        except Exception:
            new_row["bid_ask_median"] = row.get("bid_ask_median")
        out.append(new_row)
    backup_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, backup_dir / path.name)
    tmp = path.with_suffix(path.suffix + ".rewrite_tmp")
    pq.write_table(pa.Table.from_pylist(out, schema=table.schema), tmp, compression="zstd")
    tmp.replace(path)
    return {"file": path.name, "in": len(rows), "out": len(out), "dropped": dropped, "adjusted": adjusted}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("folder")
    parser.add_argument("--backup-dir", required=True)
    args = parser.parse_args()
    folder = Path(args.folder)
    backup_dir = Path(args.backup_dir)
    files = sorted(folder.glob("*.parquet"))
    for path in files:
        result = rewrite_file(path, backup_dir)
        print(result, flush=True)


if __name__ == "__main__":
    main()
