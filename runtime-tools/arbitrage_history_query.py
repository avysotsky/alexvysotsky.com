#!/usr/bin/env python3
from __future__ import annotations

import argparse
import collections
import json
from pathlib import Path

import pyarrow.parquet as pq

ROOT = Path("/home/user/VAN-v2/data/arbitrage_orderbook_history")


def normalize_exchange(value):
    exchange = str(value or "").strip().upper().replace("_", "-")
    if exchange in {"BINANCE", "BINANCE-F-PERP", "BINANCE-FUTURES"}:
        return "binance"
    if exchange in {"MEXC", "MEXC-F-PERP", "MEXC-FUTURES"}:
        return "mexc"
    return ""


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--coin", default="")
    parser.add_argument("--limit", type=int, default=720)
    parser.add_argument("--leg1", default="binance-futures")
    parser.add_argument("--leg2", default="mexc-futures")
    parser.add_argument("--latest", action="store_true")
    args = parser.parse_args()

    coin = "".join(ch for ch in args.coin.upper() if ch.isalnum())
    limit = max(1, min(args.limit, 5000))
    leg1 = normalize_exchange(args.leg1)
    leg2 = normalize_exchange(args.leg2)
    if leg1 not in {"binance", "mexc"} or leg2 not in {"binance", "mexc"} or leg1 == leg2:
        print(json.dumps({"coin": coin, "points": [], "rows": []}, separators=(",", ":")))
        return
    files = sorted(ROOT.glob("**/*.parquet"), key=lambda p: p.stat().st_mtime)
    points_by_ts = {}

    def add_rows(rows):
        by_ts = {}
        for row in rows:
            row_coin = str(row.get("coin") or "").upper()
            if coin and row_coin != coin:
                continue
            ts = int(row.get("ts_ms") or 0)
            exch = normalize_exchange(row.get("exchange"))
            mid = row.get("bid_ask_median")
            if not ts or exch not in {"binance", "mexc"} or mid is None:
                continue
            item_key = (ts, row_coin)
            item = by_ts.setdefault(item_key, {"ts_ms": ts, "ts_utc": row.get("ts_utc"), "coin": row_coin})
            item[exch] = float(mid)
            item[exch + "_symbol"] = row.get("symbol")
            bids = []
            asks = []
            for level in range(1, 6):
                bid_px = row.get(f"bid_px_{level}")
                bid_sz = row.get(f"bid_sz_{level}")
                ask_px = row.get(f"ask_px_{level}")
                ask_sz = row.get(f"ask_sz_{level}")
                if bid_px is not None and bid_sz is not None:
                    bids.append({"px": float(bid_px), "sz": float(bid_sz)})
                if ask_px is not None and ask_sz is not None:
                    asks.append({"px": float(ask_px), "sz": float(ask_sz)})
            item[exch + "_book"] = {"bids": bids, "asks": asks}
            funding = row.get("funding_pct")
            if funding is not None:
                item[exch + "_funding"] = funding
        for item in by_ts.values():
            mid1 = item.get(leg1)
            mid2 = item.get(leg2)
            if mid1 and mid2:
                item["mid1"] = mid1
                item["mid2"] = mid2
                item["leg1_exchange"] = leg1
                item["leg2_exchange"] = leg2
                item["leg1_symbol"] = item.get(leg1 + "_symbol")
                item["leg2_symbol"] = item.get(leg2 + "_symbol")
                item["leg1_book"] = item.get(leg1 + "_book")
                item["leg2_book"] = item.get(leg2 + "_book")
                item["fund1"] = item.get(leg1 + "_funding")
                item["fund2"] = item.get(leg2 + "_funding")
                item["diff"] = ((mid1 - mid2) / mid2) * 100
                points_by_ts[(str(item["coin"]), int(item["ts_ms"]))] = item

    columns = [
        "ts_ms", "ts_utc", "coin", "exchange", "symbol", "bid_ask_median", "funding_pct",
        "bid_px_1", "bid_sz_1", "bid_px_2", "bid_sz_2", "bid_px_3", "bid_sz_3", "bid_px_4", "bid_sz_4", "bid_px_5", "bid_sz_5",
        "ask_px_1", "ask_sz_1", "ask_px_2", "ask_sz_2", "ask_px_3", "ask_sz_3", "ask_px_4", "ask_sz_4", "ask_px_5", "ask_sz_5",
    ]
    jsonl_files = sorted(ROOT.glob("**/*.jsonl.inprogress"), key=lambda p: p.stat().st_mtime)
    if args.latest and jsonl_files:
        rows = []
        tail = collections.deque(maxlen=50000)
        with jsonl_files[-1].open("r", encoding="utf-8") as fh:
            for line in fh:
                tail.append(line)
        for line in tail:
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                continue
        add_rows(rows)
        points = sorted(points_by_ts.values(), key=lambda x: x["ts_ms"])
        latest_by_coin = {}
        for point in points:
            latest_by_coin[str(point.get("coin") or "")] = point
        sorted_rows = sorted(latest_by_coin.values(), key=lambda x: abs(float(x.get("diff") or 0)), reverse=True)
        out_rows = sorted_rows[:limit]
        if coin and coin in latest_by_coin and all(str(row.get("coin") or "").upper() != coin for row in out_rows):
            out_rows.append(latest_by_coin[coin])
        print(json.dumps({"leg1": leg1, "leg2": leg2, "rows": out_rows}, separators=(",", ":")))
        return

    for path in files[-12:]:
        read_kwargs = {"columns": columns}
        if coin:
            read_kwargs["filters"] = [("coin", "=", coin)]
        table = pq.read_table(path, **read_kwargs)
        add_rows(table.to_pylist())

    needle = f'"coin":"{coin}"' if coin else ""
    jsonl_files = sorted(ROOT.glob("**/*.jsonl.inprogress"), key=lambda p: p.stat().st_mtime)
    for path in jsonl_files[-2:]:
        rows = []
        with path.open("r", encoding="utf-8") as fh:
            for line in fh:
                if needle and needle not in line:
                    continue
                try:
                    rows.append(json.loads(line))
                except json.JSONDecodeError:
                    continue
        add_rows(rows)

    points = sorted(points_by_ts.values(), key=lambda x: x["ts_ms"])
    if args.latest:
        latest_by_coin = {}
        for point in points:
            latest_by_coin[str(point.get("coin") or "")] = point
        sorted_rows = sorted(latest_by_coin.values(), key=lambda x: abs(float(x.get("diff") or 0)), reverse=True)
        rows = sorted_rows[:limit]
        if coin and coin in latest_by_coin and all(str(row.get("coin") or "").upper() != coin for row in rows):
            rows.append(latest_by_coin[coin])
        out = {"leg1": leg1, "leg2": leg2, "rows": rows}
    else:
        out = {"coin": coin, "points": points[-limit:]}
    print(json.dumps(out, separators=(",", ":")))


if __name__ == "__main__":
    main()
