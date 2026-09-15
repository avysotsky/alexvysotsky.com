"""
Backtester API — ubuntu101:38034
FastAPI service for minute-level backtesting of options/futures strategies.
"""

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel
from typing import Optional, Dict, Any, List
import pandas as pd
import numpy as np
import pyarrow.parquet as pq
import glob
import os
import re
import math
from datetime import datetime, date, timedelta, timezone

app = FastAPI(title="Backtester API", version="2.0.0")
app.add_middleware(
    CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"],
)

DATA_DIR = "/home/user/deribit-md-collector/data"
FUTURES_SPREAD_MEAN_WINDOW = 360

# ─── Helpers ──────────────────────────────────────────────────────────────────

def safe_float(val, default=0.0) -> float:
    try:
        f = float(val)
        return default if (math.isnan(f) or math.isinf(f)) else f
    except (TypeError, ValueError):
        return default

def _sanitize(obj):
    if isinstance(obj, dict):   return {k: _sanitize(v) for k, v in obj.items()}
    if isinstance(obj, list):   return [_sanitize(v) for v in obj]
    if isinstance(obj, float):  return None if (math.isnan(obj) or math.isinf(obj)) else obj
    return obj

def parse_expiry(s: str) -> Optional[date]:
    for fmt in ("%d%b%y", "%d%b%Y"):
        try: return datetime.strptime(s, fmt).date()
        except: pass
    return None

def parse_instrument(name: str) -> Optional[Dict]:
    m = re.match(r'^(BTC|ETH)(?:_USDC)?-(\d+[A-Z]+\d{2,4})-(\d+)-([CP])$', name)
    if not m: return None
    asset, exp_str, strike, opt_type = m.groups()
    exp = parse_expiry(exp_str)
    return {"asset": asset, "expiry_str": exp_str, "expiry": exp,
            "strike": float(strike), "type": "put" if opt_type == "P" else "call"}

def norm_cdf(x: float) -> float:
    return 0.5 * (1.0 + math.erf(x / math.sqrt(2.0)))

def bs_put_price_eth(S: float, K: float, T: float, r: float, sigma: float) -> float:
    S = max(safe_float(S, 0.0), 1e-12)
    K = max(safe_float(K, 0.0), 0.0)
    T = max(safe_float(T, 0.0), 0.0)
    sigma = max(safe_float(sigma, 0.0), 0.0)
    if T <= 0 or sigma <= 0:
        return max(0.0, K - S) / S
    sqrt_t = math.sqrt(T)
    d1 = (math.log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * sqrt_t)
    d2 = d1 - sigma * sqrt_t
    price_usd = K * math.exp(-r * T) * norm_cdf(-d2) - S * norm_cdf(-d1)
    return max(0.0, price_usd / S)

def bs_call_price_eth(S: float, K: float, T: float, r: float, sigma: float) -> float:
    S = max(safe_float(S, 0.0), 1e-12)
    K = max(safe_float(K, 0.0), 0.0)
    T = max(safe_float(T, 0.0), 0.0)
    sigma = max(safe_float(sigma, 0.0), 0.0)
    if T <= 0 or sigma <= 0:
        return max(0.0, S - K) / S
    sqrt_t = math.sqrt(T)
    d1 = (math.log(S / K) + (r + 0.5 * sigma * sigma) * T) / (sigma * sqrt_t)
    d2 = d1 - sigma * sqrt_t
    price_usd = S * norm_cdf(d1) - K * math.exp(-r * T) * norm_cdf(d2)
    return max(0.0, price_usd / S)

def get_available_dates() -> List[str]:
    paths = sorted(glob.glob(os.path.join(DATA_DIR, "options/*/options_parsed.parquet")))
    return [p.split("/")[-2] for p in paths]

def _parse_dt_param(value: Optional[str], fallback: date) -> date:
    if not value:
        return fallback
    return datetime.strptime(value, "%Y-%m-%d").date()

def _iter_days(date_from: date, date_to: date):
    day = date_from
    while day <= date_to:
        yield day
        day += timedelta(days=1)

def _futures_ticker_from_leg(leg: str) -> str:
    if leg == "SPOT":
        return "ETH-SPOT"
    if leg == "PERP":
        return "ETH-PERPETUAL"
    return f"ETH-{leg}"

def _spread_leg_from_ticker(ticker: str) -> str:
    ticker = (ticker or "").strip().upper()
    if ticker in {"ETH-SPOT", "SPOT"}:
        return "SPOT"
    if ticker in {"ETH-PERPETUAL", "ETH-PERP", "PERP"}:
        return "PERP"
    if ticker.startswith("ETH-"):
        return ticker.split("-", 1)[1]
    return ticker

def _leg_sort_key(leg: str):
    if leg == "SPOT":
        return (date.min, 0)
    if leg == "PERP":
        return (date.min, 1)
    exp = parse_expiry(leg)
    return (exp or date.max, 2)

def _series_value(row, ticker: str) -> Optional[float]:
    if ticker == "ETH-SPOT":
        v = safe_float(row.get("index_price"), None)
        if v and v > 0:
            return v
    for col in ("mark_price", "last_price", "bid_price", "ask_price"):
        v = safe_float(row.get(col), None)
        if v and v > 0:
            return v
    return None

def _ohlc_col(schema_names: List[str], column: str) -> Optional[str]:
    return column if column in schema_names else None

def _row_has_ohlc(row, o_col: str, h_col: str, l_col: str, c_col: str) -> bool:
    for col in (o_col, h_col, l_col, c_col):
        v = safe_float(row.get(col), None)
        if v is None or v <= 0:
            return False
    return True

def _spread_instrument_name(short_ticker: str, long_ticker: str) -> str:
    short_leg = _spread_leg_from_ticker(short_ticker)
    long_leg = _spread_leg_from_ticker(long_ticker)
    return f"ETH-FS-{long_leg}_{short_leg}"

def _available_eth_futures_instruments(date_from: Optional[date] = None, date_to: Optional[date] = None) -> List[Dict[str, str]]:
    dates = get_available_dates()
    if not dates:
        return []
    start = date_from or date.fromisoformat(dates[-1])
    end = date_to or start
    legs = {"SPOT", "PERP"}
    for day in _iter_days(start, end):
        path = os.path.join(DATA_DIR, f"futures/{day.isoformat()}/futures_parsed.parquet")
        if not os.path.exists(path):
            continue
        table = pq.read_table(path, columns=["instrument_name"])
        for name in table.column("instrument_name").to_pylist():
            if not isinstance(name, str) or not name.startswith("ETH-"):
                continue
            if name == "ETH-PERPETUAL":
                legs.add("PERP")
            else:
                legs.add(name.split("-", 1)[1])
    out = []
    for leg in sorted(legs, key=_leg_sort_key):
        ticker = _futures_ticker_from_leg(leg)
        out.append({"ticker": ticker, "label": ticker})
    return out

def _load_futures_price_series(date_from: date, date_to: date, ticker: str) -> List[Dict[str, Any]]:
    ticker = (ticker or "").strip().upper()
    source_ticker = "ETH-PERPETUAL" if ticker == "ETH-SPOT" else ticker
    points = []
    for day in _iter_days(date_from, date_to):
        path = os.path.join(DATA_DIR, f"futures/{day.isoformat()}/futures_parsed.parquet")
        if not os.path.exists(path):
            continue
        schema_names = pq.read_schema(path).names
        o_col = _ohlc_col(schema_names, "open")
        h_col = _ohlc_col(schema_names, "high")
        l_col = _ohlc_col(schema_names, "low")
        c_col = _ohlc_col(schema_names, "close")
        cols = [
            "instrument_name", "snapshot_minute_utc", "mark_price", "last_price",
            "bid_price", "ask_price",
        ]
        cols.extend(c for c in (o_col, h_col, l_col, c_col) if c)
        if "index_price" in schema_names:
            cols.append("index_price")
        table = pq.read_table(path, columns=[c for c in cols if c in schema_names])
        df = table.to_pandas()
        df = df[df["instrument_name"] == source_ticker]
        if df.empty:
            continue
        for _, row in df.sort_values("snapshot_minute_utc").iterrows():
            v = _series_value(row, ticker)
            if v is not None:
                ts = row["snapshot_minute_utc"]
                has_ohlc = bool(o_col and h_col and l_col and c_col and _row_has_ohlc(row, o_col, h_col, l_col, c_col))
                points.append({
                    "t": ts.isoformat(),
                    "price": v,
                    "open": safe_float(row.get(o_col), None) if has_ohlc else None,
                    "high": safe_float(row.get(h_col), None) if has_ohlc else None,
                    "low": safe_float(row.get(l_col), None) if has_ohlc else None,
                    "close": safe_float(row.get(c_col), None) if has_ohlc else None,
                    "ohlc_available": has_ohlc,
                })
    return points

def _load_futures_spread_algo_rows(date_from: date, date_to: date, short_ticker: str, long_ticker: str) -> List[Dict[str, Any]]:
    short_ticker = (short_ticker or "ETH-25DEC26").strip().upper()
    long_ticker = (long_ticker or "ETH-PERPETUAL").strip().upper()
    short_source = "ETH-PERPETUAL" if short_ticker == "ETH-SPOT" else short_ticker
    long_source = "ETH-PERPETUAL" if long_ticker == "ETH-SPOT" else long_ticker
    by_ts: Dict[pd.Timestamp, Dict[str, Any]] = {}

    for day in _iter_days(date_from, date_to):
        path = os.path.join(DATA_DIR, f"futures/{day.isoformat()}/futures_parsed.parquet")
        if not os.path.exists(path):
            continue
        schema_names = pq.read_schema(path).names
        h_col = _ohlc_col(schema_names, "high")
        l_col = _ohlc_col(schema_names, "low")
        wanted = [
            "instrument_name", "snapshot_minute_utc", "mark_price", "last_price",
            "bid_price", "ask_price", "index_price",
        ]
        wanted.extend(c for c in (h_col, l_col) if c)
        cols = [c for c in wanted if c in schema_names]
        if not h_col or not l_col:
            continue
        table = pq.read_table(path, columns=cols)
        df = table.to_pandas()
        df = df[df["instrument_name"].isin([short_source, long_source])]
        if df.empty:
            continue

        for _, row in df.sort_values("snapshot_minute_utc").iterrows():
            ticker = short_ticker if row["instrument_name"] == short_source else long_ticker
            side = "short" if ticker == short_ticker else "long"
            price = _series_value(row, ticker)
            if price is None:
                continue
            high = safe_float(row.get(h_col), None)
            low = safe_float(row.get(l_col), None)
            if high is None or high <= 0 or low is None or low <= 0:
                continue
            ts = row["snapshot_minute_utc"]
            item = by_ts.setdefault(ts, {"ts": ts})
            item[f"{side}_price"] = price
            item[f"{side}_high"] = high
            item[f"{side}_low"] = low

    rows = []
    for ts in sorted(by_ts.keys()):
        item = by_ts[ts]
        if item.get("short_price") is None or item.get("long_price") is None:
            continue
        item["spread"] = item["short_price"] - item["long_price"]
        rows.append(item)
    return rows

def run_futures_spread_algo1_strategy(
    date_from: date,
    date_to: date,
    short_ticker: str = "ETH-25DEC26",
    long_ticker: str = "ETH-PERPETUAL",
    envelope_step_pct: float = 0.1,
    order_size_usd: float = 5.0,
    initial_capital: float = 1000.0,
    fee_pct: float = 0.0,
) -> Dict[str, Any]:
    """
    Futures Spread Algo 1 state machine.

    Execution checks use real Deribit one-minute OHLC from futures_parsed.parquet:
    high for the Short leg and low for the Long leg.
    """
    short_ticker = (short_ticker or "ETH-25DEC26").strip().upper()
    long_ticker = (long_ticker or "ETH-PERPETUAL").strip().upper()
    envelope_step_pct = envelope_step_pct if envelope_step_pct > 0 else 0.1
    order_size_usd = order_size_usd if order_size_usd > 0 else 5.0
    fee_pct = fee_pct if fee_pct > 0 else 0.0
    rows = _load_futures_spread_algo_rows(date_from, date_to, short_ticker, long_ticker)

    trades: List[Dict[str, Any]] = []
    order_limit_events: List[Dict[str, Any]] = []
    equity_curve: List[Dict[str, Any]] = []
    payoff_snapshots: List[Dict[str, Any]] = []
    ratio_window: List[float] = []
    mode = "Idle"
    run_number = 1
    spread_value: Optional[float] = None
    sell_limit_price: Optional[float] = None
    short_entry_price: Optional[float] = None
    long_buy_limit_price: Optional[float] = None
    buy_limit_placed = False
    completed_count = 0
    commission_rate = fee_pct / 100.0
    commission_total = 0.0
    realized_pnl = 0.0
    spread_short_qty = 0.0
    spread_long_qty = 0.0
    spread_short_entry_value = 0.0
    spread_long_entry_value = 0.0
    pending_short_qty = 0.0
    pending_short_entry_value = 0.0

    for item in rows:
        ts = item["ts"]
        ts_str = ts.strftime("%Y-%m-%dT%H:%M")
        date_str = ts.strftime("%Y-%m-%d")
        time_utc_str = ts.strftime("%H:%M UTC")
        short_price = float(item["short_price"])
        long_price = float(item["long_price"])
        spread = short_price - long_price
        ratio = spread / long_price if abs(long_price) > 1e-12 else None

        upper1 = None
        mean_spread = None
        if ratio is not None and math.isfinite(ratio):
            ratio_window.append(ratio)
            if len(ratio_window) > FUTURES_SPREAD_MEAN_WINDOW:
                ratio_window.pop(0)
            if len(ratio_window) >= 2:
                mean_ratio = sum(ratio_window) / len(ratio_window)
                mean_spread = mean_ratio * long_price
                upper1 = (mean_ratio + envelope_step_pct / 100.0) * long_price

        event = None
        if mode == "Idle":
            if upper1 is not None and spread > upper1:
                spread_value = spread
                sell_limit_price = short_price
                short_entry_price = None
                long_buy_limit_price = None
                buy_limit_placed = False
                mode = "SellShortLeg"
                event = "trigger_sell_short_limit"
                order_limit_events.append({
                    "ts": ts_str,
                    "date": date_str,
                    "time_utc": time_utc_str,
                    "action": "sell_short_limit_set",
                    "order_type": "short_sell_limit",
                    "ticker": short_ticker,
                    "price": round(sell_limit_price, 2),
                })
                trades.append({
                    "ts": ts_str,
                    "date": date_str,
                    "time_utc": time_utc_str,
                    "action": "sell_short_limit_set",
                    "mode": mode,
                    "short_ticker": short_ticker,
                    "long_ticker": long_ticker,
                    "spread_value": round(spread_value, 6),
                    "sell_limit_price": round(sell_limit_price, 2),
                    "spread_pct": round(spread / long_price * 100, 6) if long_price else None,
                    "upper_envelope": round(upper1, 6),
                })
        elif mode == "SellShortLeg":
            if sell_limit_price is not None and item.get("short_high") is not None and float(item["short_high"]) > sell_limit_price:
                short_entry_price = sell_limit_price
                notional_usd = max(order_size_usd, 0.0)
                pending_short_qty = notional_usd / short_entry_price if short_entry_price and short_entry_price > 0 else 0.0
                pending_short_entry_value = pending_short_qty * short_entry_price
                mode = "BuyLongLeg"
                buy_limit_placed = False
                event = "short_sell_filled"
                order_limit_events.append({
                    "ts": ts_str,
                    "date": date_str,
                    "time_utc": time_utc_str,
                    "action": "sell_short_limit_filled",
                    "order_type": "short_sell_limit",
                    "ticker": short_ticker,
                    "price": round(short_entry_price, 2),
                    "fill_price": round(short_entry_price, 2),
                    "short_high": round(float(item["short_high"]), 2),
                })
                trades.append({
                    "ts": ts_str,
                    "date": date_str,
                    "time_utc": time_utc_str,
                    "action": "sell_short_fill",
                    "mode": mode,
                    "short_ticker": short_ticker,
                    "sell_price": round(short_entry_price, 2),
                    "short_high": round(float(item["short_high"]), 2),
                    "spread_value": round(spread_value or 0.0, 6),
                })
            else:
                sell_limit_price = short_price
                event = "sell_short_limit_moved"
                order_limit_events.append({
                    "ts": ts_str,
                    "date": date_str,
                    "time_utc": time_utc_str,
                    "action": "sell_short_limit_moved",
                    "order_type": "short_sell_limit",
                    "ticker": short_ticker,
                    "price": round(sell_limit_price, 2),
                })
        elif mode == "BuyLongLeg":
            if not buy_limit_placed:
                if short_entry_price is not None and spread_value is not None:
                    long_buy_limit_price = short_entry_price - spread_value
                    buy_limit_placed = True
                    event = "buy_long_limit_set"
                    order_limit_events.append({
                        "ts": ts_str,
                        "date": date_str,
                        "time_utc": time_utc_str,
                        "action": "buy_long_limit_set",
                        "order_type": "long_buy_limit",
                        "ticker": long_ticker,
                        "price": round(long_buy_limit_price, 2),
                    })
                    trades.append({
                        "ts": ts_str,
                        "date": date_str,
                        "time_utc": time_utc_str,
                        "action": "buy_long_limit_set",
                        "mode": mode,
                        "long_ticker": long_ticker,
                        "buy_limit_price": round(long_buy_limit_price, 2),
                    })
            elif long_buy_limit_price is not None and item.get("long_low") is not None and float(item["long_low"]) < long_buy_limit_price:
                long_entry_price = long_buy_limit_price
                actual_spread_usd = (short_entry_price or 0.0) - long_entry_price
                notional_usd = max(order_size_usd, 0.0)
                short_qty = notional_usd / short_entry_price if short_entry_price and short_entry_price > 0 else 0.0
                long_qty = notional_usd / long_entry_price if long_entry_price > 0 else 0.0
                commission = commission_rate * (notional_usd + notional_usd)
                commission_total += commission
                completed_count += 1
                trade_pnl = -commission
                realized_pnl += trade_pnl
                spread_short_qty += short_qty
                spread_long_qty += long_qty
                spread_short_entry_value += (short_entry_price or 0.0) * short_qty
                spread_long_entry_value += long_entry_price * long_qty
                pending_short_qty = 0.0
                pending_short_entry_value = 0.0
                event = "spread_sale_completed"
                order_limit_events.append({
                    "ts": ts_str,
                    "date": date_str,
                    "time_utc": time_utc_str,
                    "action": "buy_long_limit_filled",
                    "order_type": "long_buy_limit",
                    "ticker": long_ticker,
                    "price": round(long_buy_limit_price, 2),
                    "fill_price": round(long_entry_price, 2),
                    "long_low": round(float(item["long_low"]), 2),
                })
                trades.append({
                    "ts": ts_str,
                    "date": date_str,
                    "time_utc": time_utc_str,
                    "action": "spread_sale",
                    "mode": "Completed",
                    "short_ticker": short_ticker,
                    "long_ticker": long_ticker,
                    "short_sell_price": round(short_entry_price or 0.0, 2),
                    "long_buy_price": round(long_entry_price, 2),
                    "spread_usd": round(actual_spread_usd, 6),
                    "spread_pct": round(actual_spread_usd / long_entry_price * 100, 6) if long_entry_price else None,
                    "spread_size_usd": round(actual_spread_usd, 6),
                    "spread_size_pct": round(actual_spread_usd / long_entry_price * 100, 6) if long_entry_price else None,
                    "spread_value_trigger": round(spread_value or 0.0, 6),
                    "short_qty": round(short_qty, 8),
                    "long_qty": round(long_qty, 8),
                    "commission_usd": round(commission, 6),
                    "total_commission_usd": round(commission_total, 6),
                    "pnl_usd": round(trade_pnl, 6),
                    "equity": round(equity, 2),
                })
                mode = "Idle"
                spread_value = None
                sell_limit_price = None
                short_entry_price = None
                long_buy_limit_price = None
                buy_limit_placed = False

        total_short_qty = spread_short_qty + pending_short_qty
        total_short_entry_value = spread_short_entry_value + pending_short_entry_value
        unrealized_pnl = (
            total_short_entry_value
            - short_price * total_short_qty
            + long_price * spread_long_qty
            - spread_long_entry_value
        )
        total_mark_pnl = realized_pnl + unrealized_pnl
        equity = initial_capital + total_mark_pnl
        open_longs = 1 if mode in {"BuyLongLeg"} else 0
        active_limit_order_type = None
        active_limit_order_price = None
        active_limit_order_ticker = None
        if mode == "SellShortLeg" and sell_limit_price is not None:
            active_limit_order_type = "short_sell_limit"
            active_limit_order_price = sell_limit_price
            active_limit_order_ticker = short_ticker
        elif mode == "BuyLongLeg" and buy_limit_placed and long_buy_limit_price is not None:
            active_limit_order_type = "long_buy_limit"
            active_limit_order_price = long_buy_limit_price
            active_limit_order_ticker = long_ticker
        snap = {
            "viewer_type": "futures_spread_algo1",
            "ts": ts_str,
            "spot": round(long_price, 2),
            "run": run_number,
            "mode": mode,
            "open_longs": open_longs,
            "short_ticker": short_ticker,
            "long_ticker": long_ticker,
            "short_price": round(short_price, 2),
            "long_price": round(long_price, 2),
            "spread": round(spread, 6),
            "spread_pct": round(spread / long_price * 100, 6) if long_price else None,
            "upper_envelope": round(upper1, 6) if upper1 is not None else None,
            "mean_spread": round(mean_spread, 6) if mean_spread is not None else None,
            "spread_value": round(spread_value, 6) if spread_value is not None else None,
            "sell_limit_price": round(sell_limit_price, 2) if sell_limit_price is not None else None,
            "long_buy_limit_price": round(long_buy_limit_price, 2) if long_buy_limit_price is not None else None,
            "active_limit_order_type": active_limit_order_type,
            "active_limit_order_price": round(active_limit_order_price, 2) if active_limit_order_price is not None else None,
            "active_limit_order_ticker": active_limit_order_ticker,
            "event": event,
            "realized_pnl_usd": round(realized_pnl, 6),
            "unrealized_pnl_usd": round(unrealized_pnl, 6),
            "total_mark_pnl_usd": round(total_mark_pnl, 6),
            "spread_short_qty": round(total_short_qty, 10),
            "spread_long_qty": round(spread_long_qty, 10),
            "spread_short_entry_value": round(total_short_entry_value, 6),
            "spread_long_entry_value": round(spread_long_entry_value, 6),
            "pending_short_qty": round(pending_short_qty, 10),
            "equity": round(equity, 2),
        }
        payoff_snapshots.append(snap)
        equity_curve.append({
            "ts": ts_str,
            "equity": round(equity, 2),
            "spot": round(long_price, 2),
            "realized_pnl": round(realized_pnl, 6),
            "unrealized_pnl": round(unrealized_pnl, 6),
            "total_mark_pnl_usd": round(total_mark_pnl, 6),
            "open_longs": open_longs,
            "run": run_number,
        })

    order_limit_segments: List[Dict[str, Any]] = []
    for idx, snap in enumerate(payoff_snapshots):
        order_type = snap.get("active_limit_order_type")
        price = snap.get("active_limit_order_price")
        if not order_type or price is None:
            continue
        end_idx = min(idx + 1, len(payoff_snapshots) - 1)
        order_limit_segments.append({
            "order_type": order_type,
            "ticker": snap.get("active_limit_order_ticker"),
            "price": price,
            "start_ts": snap.get("ts"),
            "end_ts": payoff_snapshots[end_idx].get("ts") if payoff_snapshots else snap.get("ts"),
            "start_idx": idx,
            "end_idx": end_idx,
        })

    final_total_mark_pnl = equity_curve[-1].get("total_mark_pnl_usd", realized_pnl) if equity_curve else realized_pnl
    total_return_pct = final_total_mark_pnl / initial_capital * 100 if initial_capital else 0.0
    return {
        "strategy": "futures_spread_algo1",
        "params": {
            "short_ticker": short_ticker,
            "long_ticker": long_ticker,
            "envelope_step_pct": envelope_step_pct,
            "order_size_usd": order_size_usd,
            "initial_capital": initial_capital,
            "fee_pct": fee_pct,
            "commission_rate": commission_rate,
            "high_low_source": "open/high/low/close from Deribit public/get_tradingview_chart_data",
            "ohlc_source": "deribit_tradingview_resolution_1",
        },
        "date_from": date_from.isoformat(),
        "date_to": date_to.isoformat(),
        "stats": {
            "total_pnl_usd": round(final_total_mark_pnl, 6),
            "total_return_pct": round(total_return_pct, 6),
            "annualized_return_pct": 0.0,
            "max_drawdown_pct": 0.0,
            "n_trades": completed_count,
            "ohlc_available": bool(rows),
            "ohlc_unavailable_reason": None if rows else "Real Deribit minute OHLC columns are unavailable for the selected futures range/instruments",
            "win_rate_pct": 0.0,
            "avg_win_usd": 0.0,
            "avg_loss_usd": round(realized_pnl / completed_count, 6) if completed_count else 0.0,
            "final_equity": round(initial_capital + final_total_mark_pnl, 2),
            "realized_pnl_usd": round(realized_pnl, 6),
            "unrealized_pnl_usd": round(final_total_mark_pnl - realized_pnl, 6),
            "total_mark_pnl_usd": round(final_total_mark_pnl, 6),
            "total_commission_usd": round(commission_total, 6),
        },
        "equity_curve": equity_curve,
        "trades": trades,
        "order_limit_events": order_limit_events,
        "order_limit_segments": order_limit_segments,
        "payoff_snapshots": payoff_snapshots,
    }

def _load_native_spread_series(date_from: date, date_to: date, instrument_name: str) -> List[Dict[str, Any]]:
    points = []
    for day in _iter_days(date_from, date_to):
        path = os.path.join(DATA_DIR, f"spreads/{day.isoformat()}/spreads_parsed.parquet")
        if not os.path.exists(path):
            continue
        cols = ["instrument_name", "snapshot_minute_utc", "mark_price", "last_price", "bid_price", "ask_price"]
        table = pq.read_table(path, columns=cols)
        df = table.to_pandas()
        df = df[df["instrument_name"] == instrument_name]
        if df.empty:
            continue
        for _, row in df.sort_values("snapshot_minute_utc").iterrows():
            mark = safe_float(row.get("mark_price"), None)
            if mark is None:
                continue
            ts = row["snapshot_minute_utc"]
            points.append({
                "t": ts.isoformat(),
                "mark": mark,
                "last": safe_float(row.get("last_price"), None),
                "bid": safe_float(row.get("bid_price"), None),
                "ask": safe_float(row.get("ask_price"), None),
            })
    return points

# ─── Per-day data loader ───────────────────────────────────────────────────────

class DayData:
    """Holds one day's options + futures data, indexed for fast minute lookups."""

    def __init__(self, day: date):
        self.day = day
        self.opts_by_min: Dict[pd.Timestamp, pd.DataFrame] = {}  # ts -> df of all options
        self.fut_by_min:  Dict[pd.Timestamp, pd.DataFrame] = {}  # ts -> df of futures
        self._load()

    def _load(self):
        d_str = self.day.isoformat()
        opt_path = os.path.join(DATA_DIR, f"options/{d_str}/options_parsed.parquet")
        fut_path = os.path.join(DATA_DIR, f"futures/{d_str}/futures_parsed.parquet")

        if os.path.exists(opt_path):
            df = pq.read_table(opt_path).to_pandas()
            # Keep only columns we need to save memory
            cols = ['instrument_name', 'snapshot_minute_utc',
                    'mark_price', 'bid_price', 'ask_price',
                    'mark_iv', 'underlying_price', 'delta']
            df = df[[c for c in cols if c in df.columns]]
            for ts, grp in df.groupby('snapshot_minute_utc'):
                self.opts_by_min[ts] = grp.set_index('instrument_name')

        if os.path.exists(fut_path):
            df = pq.read_table(fut_path).to_pandas()
            cols = ['instrument_name', 'snapshot_minute_utc', 'mark_price', 'bid_price', 'ask_price']
            df = df[[c for c in cols if c in df.columns]]
            for ts, grp in df.groupby('snapshot_minute_utc'):
                self.fut_by_min[ts] = grp.set_index('instrument_name')

    def minutes(self) -> List[pd.Timestamp]:
        return sorted(self.opts_by_min.keys())

    def get_spot(self, ts: pd.Timestamp, asset: str = "ETH") -> Optional[float]:
        grp = self.fut_by_min.get(ts)
        if grp is None: return None
        perp = f"{asset}-PERPETUAL"
        if perp in grp.index:
            v = safe_float(grp.loc[perp, 'mark_price'])
            if v > 0: return v
        # fallback: any asset future
        rows = grp[grp.index.str.startswith(f"{asset}-")]
        if not rows.empty:
            v = safe_float(rows.iloc[0]['mark_price'])
            if v > 0: return v
        return None

    def get_option(self, ts: pd.Timestamp, instrument: str) -> Optional[Dict]:
        grp = self.opts_by_min.get(ts)
        if grp is None or instrument not in grp.index: return None
        row = grp.loc[instrument]
        if isinstance(row, pd.DataFrame): row = row.iloc[0]  # dupe rows guard
        return {
            "mark_price": safe_float(row.get('mark_price')),
            "bid_price":  safe_float(row.get('bid_price')),
            "ask_price":  safe_float(row.get('ask_price')),
            "mark_iv":    safe_float(row.get('mark_iv')),
            "underlying": safe_float(row.get('underlying_price')),
            "delta":      safe_float(row.get('delta')),
        }

    def get_eth_puts_at(self, ts: pd.Timestamp) -> pd.DataFrame:
        """Return all ETH put options at given minute."""
        grp = self.opts_by_min.get(ts)
        if grp is None: return pd.DataFrame()
        mask = grp.index.str.match(r'^ETH-\d+[A-Z]+\d+-\d+-P$')
        return grp[mask].copy()

    def get_eth_calls_at(self, ts: pd.Timestamp) -> pd.DataFrame:
        """Return all ETH call options at given minute."""
        grp = self.opts_by_min.get(ts)
        if grp is None: return pd.DataFrame()
        mask = grp.index.str.match(r'^ETH-\d+[A-Z]+\d+-\d+-C$')
        return grp[mask].copy()

# ─── Strategy: Short ETH Put (minute-level) ───────────────────────────────────

def run_short_eth_put_strategy(
    date_from: date,
    date_to: date,
    strike_offset_pct: float = 2.5,
    initial_capital: float = 10000.0,
    lots_per_trade: int = 1,
    use_bid: bool = True,
) -> Dict[str, Any]:
    """
    Each day at 08:00 UTC (after Deribit clearing):
      - Sell 1 lot of ETH put: nearest strike to spot*(1-offset%), next-day expiry, sell at bid
    Each subsequent minute until next 08:00:
      - Track mark_price + spot from live data → running P&L
      - Build payoff snapshots (minute-level) for profile animation
    At next 08:00:
      - Close: expired worthless (OTM) or settle at intrinsic (ITM)
    """
    LOT_SIZE = 0.1
    CLEARING_HOUR = 8
    CLEARING_MIN  = 0
    R = 0.05  # risk-free rate for BS

    available = set(get_available_dates())
    trades = []
    equity_curve = []       # [{date_str, minute_str, equity, spot}]
    payoff_snapshots = []   # [{minute_str, instrument, strike, sell_price_eth, spot, tte_frac, iv, equity, cum_pnl}]

    equity = initial_capital
    open_position = None    # filled after open trade

    # Iterate day by day, but within each day iterate minute by minute
    day = date_from
    prev_day_data: Optional[DayData] = None

    while day <= date_to:
        d_str = day.isoformat()
        if d_str not in available:
            day += timedelta(days=1)
            continue

        day_data = DayData(day)
        minutes = day_data.minutes()
        if not minutes:
            day += timedelta(days=1)
            continue

        for ts in minutes:
            ts_str = ts.strftime("%Y-%m-%dT%H:%M")
            is_clearing = (ts.hour == CLEARING_HOUR and ts.minute == CLEARING_MIN)

            spot = day_data.get_spot(ts)
            if spot is None:
                # Try underlying_price from options — ETH instruments only to avoid BTC underlying
                opt_grp = day_data.opts_by_min.get(ts)
                if opt_grp is not None and not opt_grp.empty and 'underlying_price' in opt_grp.columns:
                    eth_rows = opt_grp[opt_grp.index.str.match(r'^ETH-')]
                    vals = eth_rows['underlying_price'].dropna() if not eth_rows.empty else pd.Series()
                    if not vals.empty:
                        spot = safe_float(vals.iloc[0])

            # ── At clearing: close yesterday + open new ──
            if is_clearing:

                # Close previous position
                if open_position is not None:
                    pos = open_position
                    close_spot = spot or pos['underlying']
                    exp_date = pos['expiry_date']

                    if exp_date <= day:
                        # Expired
                        if close_spot > pos['strike']:
                            close_price_eth = 0.0
                            close_type = "expired_worthless"
                        else:
                            # ITM: intrinsic in ETH
                            close_price_eth = max(0.0, (pos['strike'] - close_spot) / close_spot)
                            close_type = "expired_itm"
                    else:
                        # Buy back at ask
                        opt = day_data.get_option(ts, pos['instrument'])
                        if opt:
                            close_price_eth = opt['ask_price'] or opt['mark_price']
                            close_type = "bought_back"
                        else:
                            close_price_eth = 0.0
                            close_type = "not_found_assume_expired"

                    pnl_eth = (pos['sell_price_eth'] - close_price_eth) * lots_per_trade * LOT_SIZE
                    pnl_usd = pnl_eth * close_spot
                    equity += pnl_usd

                    trades.append({
                        "date": d_str, "ts": ts_str, "action": "close",
                        "instrument": pos['instrument'], "strike": pos['strike'],
                        "expiry": pos['expiry_str'],
                        "sell_price": round(pos['sell_price_eth'], 6),
                        "close_price": round(close_price_eth, 6),
                        "underlying_open": round(pos['underlying'], 2),
                        "underlying_close": round(close_spot, 2),
                        "pnl_eth": round(pnl_eth, 6), "pnl_usd": round(pnl_usd, 2),
                        "close_type": close_type, "equity": round(equity, 2),
                    })
                    open_position = None

                # Open new position
                if spot:
                    target_strike = spot * (1 - strike_offset_pct / 100)
                    eth_puts = day_data.get_eth_puts_at(ts)

                    if not eth_puts.empty:
                        eth_puts = eth_puts.copy()
                        eth_puts['_parsed'] = eth_puts.index.map(parse_instrument)
                        eth_puts = eth_puts[eth_puts['_parsed'].notna()].copy()
                        eth_puts['_expiry'] = eth_puts['_parsed'].apply(lambda x: x['expiry'])
                        eth_puts['_strike'] = eth_puts['_parsed'].apply(lambda x: x['strike'])

                        # Nearest future expiry (>= tomorrow)
                        tomorrow = day + timedelta(days=1)
                        future_exp = sorted([e for e in eth_puts['_expiry'].dropna().unique() if e >= tomorrow])
                        if future_exp:
                            exp_date = future_exp[0]
                            day_puts = eth_puts[eth_puts['_expiry'] == exp_date].copy()
                            day_puts['_sdiff'] = (day_puts['_strike'] - target_strike).abs()
                            best = day_puts.loc[day_puts['_sdiff'].idxmin()]
                            parsed = best['_parsed']

                            sell_col = 'bid_price' if use_bid else 'mark_price'
                            sell_price_eth = safe_float(best.get(sell_col)) or safe_float(best.get('mark_price'))

                            if sell_price_eth > 0:
                                iv_at_open = safe_float(best.get('mark_iv')) or 60.0
                                open_position = {
                                    "instrument": best.name,
                                    "strike": parsed['strike'],
                                    "expiry_date": exp_date,
                                    "expiry_str": parsed['expiry_str'],
                                    "sell_price_eth": sell_price_eth,
                                    "underlying": spot,
                                    "open_ts": ts_str,
                                    "open_date": d_str,
                                    "iv_at_open": iv_at_open,
                                }
                                trades.append({
                                    "date": d_str, "ts": ts_str, "action": "open",
                                    "instrument": best.name, "strike": parsed['strike'],
                                    "expiry": parsed['expiry_str'],
                                    "sell_price": round(sell_price_eth, 6),
                                    "close_price": None,
                                    "underlying_open": round(spot, 2), "underlying_close": None,
                                    "pnl_eth": None, "pnl_usd": None, "close_type": None,
                                    "equity": round(equity, 2),
                                    "premium_usd_estimate": round(sell_price_eth * lots_per_trade * LOT_SIZE * spot, 2),
                                    "delta": round(safe_float(best.get('delta')), 4),
                                    "mark_iv": round(safe_float(best.get('mark_iv')), 2),
                                })

            # ── Every minute: equity mark-to-market + payoff snapshot ──
            if open_position is not None and spot:
                pos = open_position
                opt = day_data.get_option(ts, pos['instrument'])

                if opt and opt['mark_price'] is not None:
                    # mark_price=0.0 is valid (OTM option near expiry)
                    current_price_eth = opt['mark_price']
                    current_iv = opt['mark_iv'] if opt.get('mark_iv', 0) > 0 else pos['iv_at_open']
                elif opt:
                    # mark_price missing/None but we have option data — use ask as proxy
                    current_price_eth = safe_float(opt.get('ask_price')) or safe_float(opt.get('bid_price')) or 0.0
                    current_iv = opt.get('mark_iv') or pos['iv_at_open']
                else:
                    # No data at all — keep last known price (conservative)
                    current_price_eth = pos['sell_price_eth']
                    current_iv = pos['iv_at_open']

                # TTE: seconds from now to expiry at 08:00
                expiry_dt = datetime(pos['expiry_date'].year, pos['expiry_date'].month,
                                     pos['expiry_date'].day, 8, 0, 0, tzinfo=timezone.utc)
                ts_utc = ts.to_pydatetime()
                if ts_utc.tzinfo is None:
                    ts_utc = ts_utc.replace(tzinfo=timezone.utc)
                tte_secs = max(0, (expiry_dt - ts_utc).total_seconds())
                tte_frac = tte_secs / (365.25 * 24 * 3600)

                unrealized_pnl_eth = (pos['sell_price_eth'] - current_price_eth) * lots_per_trade * LOT_SIZE
                unrealized_pnl_usd = unrealized_pnl_eth * spot
                mark_equity = equity + unrealized_pnl_usd
                bs_price_eth = bs_put_price_eth(spot, pos['strike'], tte_frac, 0.05, current_iv / 100.0)
                bs_pnl_at_spot_usd = (pos['sell_price_eth'] - bs_price_eth) * lots_per_trade * LOT_SIZE * spot
                expiry_price_eth = max(0.0, pos['strike'] - spot) / max(spot, 1.0)
                expiry_payoff_at_spot_usd = (pos['sell_price_eth'] - expiry_price_eth) * lots_per_trade * LOT_SIZE * spot
                total_mark_pnl_usd = mark_equity - initial_capital

                equity_curve.append({
                    "ts": ts_str,
                    "equity": round(mark_equity, 2),
                    "spot": round(spot, 2),
                    "unrealized_pnl": round(unrealized_pnl_usd, 2),
                    "mark_pnl_usd": round(unrealized_pnl_usd, 2),
                    "total_mark_pnl_usd": round(mark_equity - initial_capital, 2),
                    "bs_pnl_at_spot_usd": round(bs_pnl_at_spot_usd, 2),
                    "expiry_payoff_at_spot_usd": round(expiry_payoff_at_spot_usd, 2),
                    "realized_pnl_usd": round(equity - initial_capital, 2),
                })

                payoff_snapshots.append({
                    "ts": ts_str,
                    "instrument": pos['instrument'],
                    "strike": pos['strike'],
                    "sell_price_eth": round(pos['sell_price_eth'], 6),
                    "spot": round(spot, 2),
                    "tte_frac": round(tte_frac, 8),
                    "tte_mins": round(tte_secs / 60, 1),
                    "iv": round(current_iv, 2),
                    "lots": lots_per_trade,
                    "lot_size": LOT_SIZE,
                    "direction": -1,
                    "equity": round(mark_equity, 2),
                    "cumulative_pnl": round(total_mark_pnl_usd, 2),
                    "mark_pnl_usd": round(unrealized_pnl_usd, 2),
                    "total_mark_pnl_usd": round(total_mark_pnl_usd, 2),
                    "bs_pnl_at_spot_usd": round(bs_pnl_at_spot_usd, 2),
                    "expiry_payoff_at_spot_usd": round(expiry_payoff_at_spot_usd, 2),
                })
            elif spot:
                # No open position — still track equity (flat)
                equity_curve.append({
                    "ts": ts_str,
                    "equity": round(equity, 2),
                    "spot": round(spot, 2),
                    "unrealized_pnl": 0.0,
                    "mark_pnl_usd": 0.0,
                    "total_mark_pnl_usd": round(equity - initial_capital, 2),
                    "bs_pnl_at_spot_usd": 0.0,
                    "expiry_payoff_at_spot_usd": 0.0,
                    "realized_pnl_usd": round(equity - initial_capital, 2),
                })

        day += timedelta(days=1)

    # ── Statistics ──
    pnl_list = [t['pnl_usd'] for t in trades if t['action'] == 'close' and t['pnl_usd'] is not None]
    total_pnl = sum(pnl_list)
    n_trades = len(pnl_list)
    winners = [p for p in pnl_list if p > 0]
    losers  = [p for p in pnl_list if p < 0]
    win_rate = len(winners) / n_trades * 100 if n_trades else 0
    avg_win  = float(np.mean(winners)) if winners else 0.0
    avg_loss = float(np.mean(losers))  if losers  else 0.0
    total_return_pct = total_pnl / initial_capital * 100

    # Max drawdown on mark-to-market equity curve
    peak = initial_capital
    max_dd = 0.0
    for e in equity_curve:
        eq = e['equity']
        if eq > peak: peak = eq
        dd = (peak - eq) / peak * 100
        if dd > max_dd: max_dd = dd

    days_count = max((date_to - date_from).days, 1)
    ann_return = ((1 + total_return_pct / 100) ** (365 / days_count) - 1) * 100

    return {
        "strategy": "short_eth_put_daily",
        "params": {
            "strike_offset_pct": strike_offset_pct,
            "initial_capital": initial_capital,
            "lots_per_trade": lots_per_trade,
            "lot_size": LOT_SIZE,
            "use_bid": use_bid,
        },
        "date_from": date_from.isoformat(),
        "date_to": date_to.isoformat(),
        "stats": {
            "total_pnl_usd": round(total_pnl, 2),
            "total_return_pct": round(total_return_pct, 2),
            "annualized_return_pct": round(ann_return, 2),
            "max_drawdown_pct": round(max_dd, 2),
            "n_trades": n_trades,
            "win_rate_pct": round(win_rate, 2),
            "avg_win_usd": round(avg_win, 2),
            "avg_loss_usd": round(avg_loss, 2),
            "final_equity": round(equity, 2),
        },
        "equity_curve": equity_curve,
        "trades": trades,
        "payoff_snapshots": payoff_snapshots,
    }

# ─── Strategy: Short ETH Strangle (Daily, minute-level) ──────────────────────

def run_short_eth_strangle_strategy(
    date_from: date,
    date_to: date,
    put_offset_pct: float = 2.5,
    call_offset_pct: float = 2.5,
    initial_capital: float = 10000.0,
    lots_per_trade: int = 1,
    use_bid: bool = True,
) -> Dict[str, Any]:
    """
    Short Strangle strategy:
      At 08:00 UTC each day:
        - Sell 1 OTM ETH put  at strike = spot * (1 - put_offset_pct/100)
        - Sell 1 OTM ETH call at strike = spot * (1 + call_offset_pct/100)
        Both nearest next-day expiry.
      Track combined mark P&L every minute.
      Close at next 08:00 (expire or buy back).
    Rationale: ETH IV consistently > realized vol → collect double premium with theta decay.
    """
    LOT_SIZE = 0.1
    CLEARING_HOUR = 8
    CLEARING_MIN  = 0

    available = set(get_available_dates())
    trades = []
    equity_curve = []
    payoff_snapshots = []
    payoff_snapshots = []

    equity = initial_capital
    open_position = None  # dict with 'put' and 'call' sub-positions

    day = date_from
    while day <= date_to:
        d_str = day.isoformat()
        if d_str not in available:
            day += timedelta(days=1)
            continue

        day_data = DayData(day)
        minutes = day_data.minutes()
        if not minutes:
            day += timedelta(days=1)
            continue

        for ts in minutes:
            ts_str = ts.strftime("%Y-%m-%dT%H:%M")
            is_clearing = (ts.hour == CLEARING_HOUR and ts.minute == CLEARING_MIN)

            spot = day_data.get_spot(ts)
            if spot is None:
                # Try underlying_price from options — ETH instruments only to avoid BTC underlying
                opt_grp = day_data.opts_by_min.get(ts)
                if opt_grp is not None and not opt_grp.empty and 'underlying_price' in opt_grp.columns:
                    eth_rows = opt_grp[opt_grp.index.str.match(r'^ETH-')]
                    vals = eth_rows['underlying_price'].dropna() if not eth_rows.empty else pd.Series()
                    if not vals.empty:
                        spot = safe_float(vals.iloc[0])

            # ── At clearing: close previous + open new ──
            if is_clearing:

                if open_position is not None:
                    pos = open_position
                    close_spot = spot or pos['put']['underlying']
                    total_sell_eth  = pos['put']['sell_price_eth'] + pos['call']['sell_price_eth']
                    total_close_eth = 0.0

                    for leg_key in ('put', 'call'):
                        leg = pos[leg_key]
                        exp_date = leg['expiry_date']
                        if exp_date <= day:
                            if leg_key == 'put':
                                close_eth = max(0.0, (leg['strike'] - close_spot) / close_spot) if close_spot < leg['strike'] else 0.0
                            else:
                                close_eth = max(0.0, (close_spot - leg['strike']) / close_spot) if close_spot > leg['strike'] else 0.0
                            close_type = "expired_itm" if close_eth > 0 else "expired_worthless"
                        else:
                            opt = day_data.get_option(ts, leg['instrument'])
                            if opt:
                                close_eth = opt['ask_price'] or opt['mark_price']
                                close_type = "bought_back"
                            else:
                                close_eth = 0.0
                                close_type = "not_found_assume_expired"
                        total_close_eth += close_eth

                        trades.append({
                            "date": d_str, "ts": ts_str, "action": "close",
                            "leg": leg_key,
                            "instrument": leg['instrument'], "strike": leg['strike'],
                            "expiry": leg['expiry_str'],
                            "sell_price": round(leg['sell_price_eth'], 6),
                            "close_price": round(close_eth, 6),
                            "underlying_open": round(leg['underlying'], 2),
                            "underlying_close": round(close_spot, 2),
                            "close_type": close_type,
                        })

                    pnl_eth = (total_sell_eth - total_close_eth) * lots_per_trade * LOT_SIZE
                    pnl_usd = pnl_eth * close_spot
                    equity += pnl_usd

                    # annotate last close trade with combined P&L
                    trades[-1]["pnl_eth"] = round(pnl_eth, 6)
                    trades[-1]["pnl_usd"] = round(pnl_usd, 2)
                    trades[-1]["equity"]  = round(equity, 2)
                    open_position = None

                # Open new strangle
                if spot:
                    target_put_strike  = spot * (1 - put_offset_pct  / 100)
                    target_call_strike = spot * (1 + call_offset_pct / 100)

                    eth_puts  = day_data.get_eth_puts_at(ts)
                    eth_calls = day_data.get_eth_calls_at(ts)

                    def _best_leg(df, target_strike):
                        if df.empty: return None
                        df = df.copy()
                        df['_parsed'] = df.index.map(parse_instrument)
                        df = df[df['_parsed'].notna()].copy()
                        df['_expiry'] = df['_parsed'].apply(lambda x: x['expiry'])
                        df['_strike'] = df['_parsed'].apply(lambda x: x['strike'])
                        tomorrow = day + timedelta(days=1)
                        future_exp = sorted([e for e in df['_expiry'].dropna().unique() if e >= tomorrow])
                        if not future_exp: return None
                        exp_date = future_exp[0]
                        day_df = df[df['_expiry'] == exp_date].copy()
                        day_df['_sdiff'] = (day_df['_strike'] - target_strike).abs()
                        best = day_df.loc[day_df['_sdiff'].idxmin()]
                        sell_col = 'bid_price' if use_bid else 'mark_price'
                        sell_price_eth = safe_float(best.get(sell_col)) or safe_float(best.get('mark_price'))
                        if sell_price_eth <= 0: return None
                        parsed = best['_parsed']
                        return {
                            "instrument": best.name,
                            "strike": parsed['strike'],
                            "expiry_date": exp_date,
                            "expiry_str": parsed['expiry_str'],
                            "sell_price_eth": sell_price_eth,
                            "underlying": spot,
                            "open_ts": ts_str,
                            "iv_at_open": safe_float(best.get('mark_iv')) or 60.0,
                            "delta": safe_float(best.get('delta')),
                        }

                    put_leg  = _best_leg(eth_puts,  target_put_strike)
                    call_leg = _best_leg(eth_calls, target_call_strike)

                    if put_leg and call_leg:
                        open_position = {"put": put_leg, "call": call_leg}
                        combined_premium_usd = (
                            (put_leg['sell_price_eth'] + call_leg['sell_price_eth'])
                            * lots_per_trade * LOT_SIZE * spot
                        )
                        for leg_key, leg in (("put", put_leg), ("call", call_leg)):
                            trades.append({
                                "date": d_str, "ts": ts_str, "action": "open",
                                "leg": leg_key,
                                "instrument": leg['instrument'], "strike": leg['strike'],
                                "expiry": leg['expiry_str'],
                                "sell_price": round(leg['sell_price_eth'], 6),
                                "close_price": None,
                                "underlying_open": round(spot, 2), "underlying_close": None,
                                "pnl_eth": None, "pnl_usd": None, "close_type": None,
                                "equity": round(equity, 2),
                                "delta": round(leg['delta'], 4),
                                "mark_iv": round(leg['iv_at_open'], 2),
                            })
                        trades[-1]["combined_premium_usd"] = round(combined_premium_usd, 2)

            # ── Every minute: mark-to-market ──
            if open_position is not None and spot:
                pos = open_position
                total_sell_eth    = pos['put']['sell_price_eth'] + pos['call']['sell_price_eth']
                total_current_eth = 0.0

                for leg in (pos['put'], pos['call']):
                    opt = day_data.get_option(ts, leg['instrument'])
                    if opt and opt['mark_price'] is not None:
                        # mark_price=0.0 is valid (OTM option near expiry)
                        total_current_eth += opt['mark_price']
                    elif opt:
                        # mark_price missing/None — use ask as proxy
                        total_current_eth += safe_float(opt.get('ask_price')) or safe_float(opt.get('bid_price')) or 0.0
                    else:
                        # No data — keep last known price (conservative)
                        total_current_eth += leg['sell_price_eth']

                unrealized_pnl_eth = (total_sell_eth - total_current_eth) * lots_per_trade * LOT_SIZE
                unrealized_pnl_usd = unrealized_pnl_eth * spot
                mark_equity = equity + unrealized_pnl_usd

                # TTE from put leg
                expiry_dt = datetime(pos['put']['expiry_date'].year,
                                     pos['put']['expiry_date'].month,
                                     pos['put']['expiry_date'].day, 8, 0, 0, tzinfo=timezone.utc)
                ts_utc = ts.to_pydatetime()
                if ts_utc.tzinfo is None: ts_utc = ts_utc.replace(tzinfo=timezone.utc)
                tte_secs = max(0, (expiry_dt - ts_utc).total_seconds())
                tte_frac = tte_secs / (365.25 * 24 * 3600)

                # IVs for today-curve approximation
                opt_put  = day_data.get_option(ts, pos['put']['instrument'])
                opt_call = day_data.get_option(ts, pos['call']['instrument'])
                put_iv_now  = safe_float(opt_put.get('mark_iv'))  if opt_put  else pos['put']['iv_at_open']
                call_iv_now = safe_float(opt_call.get('mark_iv')) if opt_call else pos['call']['iv_at_open']
                bs_put_eth = bs_put_price_eth(spot, pos['put']['strike'], tte_frac, 0.05, put_iv_now / 100.0)
                bs_call_eth = bs_call_price_eth(spot, pos['call']['strike'], tte_frac, 0.05, call_iv_now / 100.0)
                bs_pnl_at_spot_usd = (total_sell_eth - bs_put_eth - bs_call_eth) * lots_per_trade * LOT_SIZE * spot
                put_expiry_eth = max(0.0, pos['put']['strike'] - spot) / max(spot, 1.0)
                call_expiry_eth = max(0.0, spot - pos['call']['strike']) / max(spot, 1.0)
                expiry_payoff_at_spot_usd = (total_sell_eth - put_expiry_eth - call_expiry_eth) * lots_per_trade * LOT_SIZE * spot
                total_mark_pnl_usd = mark_equity - initial_capital
                equity_curve.append({
                    "ts": ts_str, "equity": round(mark_equity, 2),
                    "spot": round(spot, 2), "unrealized_pnl": round(unrealized_pnl_usd, 2),
                    "mark_pnl_usd": round(unrealized_pnl_usd, 2),
                    "total_mark_pnl_usd": round(total_mark_pnl_usd, 2),
                    "bs_pnl_at_spot_usd": round(bs_pnl_at_spot_usd, 2),
                    "expiry_payoff_at_spot_usd": round(expiry_payoff_at_spot_usd, 2),
                    "realized_pnl_usd": round(equity - initial_capital, 2),
                })
                payoff_snapshots.append({
                    "ts": ts_str,
                    "put_instrument":      pos['put']['instrument'],
                    "call_instrument":     pos['call']['instrument'],
                    "put_strike":          pos['put']['strike'],
                    "call_strike":         pos['call']['strike'],
                    "put_sell_price_eth":  round(pos['put']['sell_price_eth'], 6),
                    "call_sell_price_eth": round(pos['call']['sell_price_eth'], 6),
                    "put_iv":              round(put_iv_now,  2) if put_iv_now  else 0,
                    "call_iv":             round(call_iv_now, 2) if call_iv_now else 0,
                    "spot": round(spot, 2),
                    "tte_frac": round(tte_frac, 8),
                    "tte_mins": round(tte_secs / 60, 1),
                    "lots": lots_per_trade,
                    "lot_size": LOT_SIZE,
                    "equity": round(mark_equity, 2),
                    "cumulative_pnl": round(total_mark_pnl_usd, 2),
                    "mark_pnl_usd": round(unrealized_pnl_usd, 2),
                    "total_mark_pnl_usd": round(total_mark_pnl_usd, 2),
                    "bs_pnl_at_spot_usd": round(bs_pnl_at_spot_usd, 2),
                    "expiry_payoff_at_spot_usd": round(expiry_payoff_at_spot_usd, 2),
                })
            elif spot:
                equity_curve.append({
                    "ts": ts_str, "equity": round(equity, 2),
                    "spot": round(spot, 2), "unrealized_pnl": 0.0,
                    "mark_pnl_usd": 0.0,
                    "total_mark_pnl_usd": round(equity - initial_capital, 2),
                    "bs_pnl_at_spot_usd": 0.0,
                    "expiry_payoff_at_spot_usd": 0.0,
                    "realized_pnl_usd": round(equity - initial_capital, 2),
                })

        day += timedelta(days=1)

    # ── Statistics ──
    pnl_list = [t['pnl_usd'] for t in trades if t.get('action') == 'close' and t.get('pnl_usd') is not None]
    total_pnl   = sum(pnl_list)
    n_trades    = len(pnl_list)
    winners     = [p for p in pnl_list if p > 0]
    losers      = [p for p in pnl_list if p < 0]
    win_rate    = len(winners) / n_trades * 100 if n_trades else 0
    avg_win     = float(np.mean(winners)) if winners else 0.0
    avg_loss    = float(np.mean(losers))  if losers  else 0.0
    total_return_pct = total_pnl / initial_capital * 100

    peak = initial_capital
    max_dd = 0.0
    for e in equity_curve:
        eq = e['equity']
        if eq > peak: peak = eq
        dd = (peak - eq) / peak * 100
        if dd > max_dd: max_dd = dd

    days_count = max((date_to - date_from).days, 1)
    ann_return = ((1 + total_return_pct / 100) ** (365 / days_count) - 1) * 100

    return {
        "strategy": "short_eth_strangle_daily",
        "params": {
            "put_offset_pct": put_offset_pct,
            "call_offset_pct": call_offset_pct,
            "initial_capital": initial_capital,
            "lots_per_trade": lots_per_trade,
            "lot_size": LOT_SIZE,
            "use_bid": use_bid,
        },
        "date_from": date_from.isoformat(),
        "date_to": date_to.isoformat(),
        "stats": {
            "total_pnl_usd": round(total_pnl, 2),
            "total_return_pct": round(total_return_pct, 2),
            "annualized_return_pct": round(ann_return, 2),
            "max_drawdown_pct": round(max_dd, 2),
            "n_trades": n_trades,
            "win_rate_pct": round(win_rate, 2),
            "avg_win_usd": round(avg_win, 2),
            "avg_loss_usd": round(avg_loss, 2),
            "final_equity": round(equity, 2),
        },
        "equity_curve": equity_curve,
        "trades": trades,
        "payoff_snapshots": payoff_snapshots,
    }


# ─── Strategy: Futures Algo 1 (ETH-PERPETUAL grid buy) ───────────────────────

def run_futures_algo1_strategy(
    date_from: date,
    date_to: date,
    strategy_id: str = "futures_algo1",
    step_pct: float = 1.0,
    max_orders: int = 20,
    tp_pct: float = 2.0,
    order_size_usd: float = 5.0,
    initial_capital: float = 1000.0,
    use_split_tp: bool = True,
    tp1_offset_pct: float = 15.0,
    tp3_offset_pct: float = 15.0,
) -> Dict[str, Any]:
    """
    Futures Algo 1 — ETH-PERPETUAL grid buy strategy (unified TP).

    Logic:
      - Place up to max_orders BUY limit orders below start price, spaced step_pct apart.
      - As orders fill, avg_entry is recalculated across all filled orders.
      - Single unified TP for the whole position: avg_entry * (1 + tp_pct/100).
        Recalculated each time a new buy fills (avg drops → TP drops with it).
      - When spot >= unified_tp → close ENTIRE position at once, book P&L, restart grid.
      - Re-anchor: if no fills yet and price moves >0.5% above grid start → restart.
    """
    available = set(get_available_dates())
    trades = []
    equity_curve = []
    payoff_snapshots = []

    total_days_requested = max((date_to - date_from).days + 1, 1)
    sample_points_target = 20000
    snapshot_stride = max(1, math.ceil(total_days_requested * 1440 / sample_points_target))
    minute_counter = 0

    equity = initial_capital
    realized_pnl = 0.0
    run_number = 0
    grid_start_price: Optional[float] = None
    orders: List[Dict] = []
    order_counter = 0
    # Split-TP state for current run
    first_fill_price: Optional[float] = None   # price of first buy fill in current run
    min_spot_since_first_fill: Optional[float] = None  # running minimum since first fill
    # Partial TP tracking: {1: filled?, 2: filled?, 3: filled?}
    tp1_filled = False
    tp2_filled = False
    # tp3 triggers restart — handled inline
    tp1_size_eth = 0.0   # size to close at TP1
    tp2_size_eth = 0.0   # size to close at TP2
    # remaining goes to TP3 at restart
    tp1_price: Optional[float] = None
    tp2_price: Optional[float] = None
    tp3_price: Optional[float] = None
    tp1_realized = 0.0   # realized at TP1 (partial)
    tp2_realized = 0.0   # realized at TP2 (partial)
    # Cycle tracking: fills since last cycle start (resets on TP1 hit or full restart)
    cycle_fills: List[Dict] = []   # list of {"buy_price", "size_eth"}
    cycle_after_tp1 = False        # True after TP1 hit until next restart
    # After TP1: accumulated legacy TP orders from prior cycles
    legacy_orders: List[Dict[str, Any]] = []
    # After TP1: new sub-grid orders (appended to orders list)
    new_cycle_started = False      # True if sub-grid already launched this cycle
    # Overall position state for dollar-account model: Avg Entry price updates on BUY fills only.
    position_avg_entry: Optional[float] = None

    def make_grid(start_price: float, run_id: int) -> List[Dict]:
        nonlocal order_counter
        grid = []
        for i in range(1, max_orders + 1):
            buy_p = start_price * (1 - step_pct / 100 * i)
            size_eth = order_size_usd / buy_p
            order_counter += 1
            grid.append({
                "id": order_counter,
                "buy_price": buy_p,
                "size_usd": order_size_usd,
                "size_eth": size_eth,
                "remaining_size_eth": 0.0,
                "remaining_size_usd": 0.0,
                "filled": False,
                "run": run_id,
            })
        return grid

    def calc_position(orders_list):
        """Return (open_longs, total_size_eth, avg_entry, unified_tp, total_size_usd) on USD basis."""
        longs = [o for o in orders_list if o["filled"] and o.get("remaining_size_usd", 0.0) > 1e-12]
        if not longs:
            return longs, 0.0, None, None, 0.0
        total_usd = sum(o.get("remaining_size_usd", o["size_usd"]) for o in longs)
        avg_e = sum(o["buy_price"] * o.get("remaining_size_usd", o["size_usd"]) for o in longs) / total_usd
        total_eth = sum(o.get("remaining_size_usd", o["size_usd"]) / o["buy_price"] for o in longs)
        utp = avg_e * (1 + tp_pct / 100)
        return longs, total_eth, avg_e, utp, total_usd

    def reduce_position_usd(usd_to_close: float):
        """Reduce the single aggregated open position proportionally across all open BUY lots.
        Partial exits must not target a specific run/cycle; legacy TP orders are sell orders
        against the same unified position ledger.
        """
        if usd_to_close <= 1e-12:
            return
        candidates = [o for o in orders if o["filled"] and o.get("remaining_size_usd", 0.0) > 1e-12]
        total = sum(o.get("remaining_size_usd", o["size_usd"]) for o in candidates)
        if total <= 1e-12:
            return
        ratio = min(1.0, usd_to_close / total)
        for o in candidates:
            cur_usd = o.get("remaining_size_usd", o["size_usd"])
            closed_usd = cur_usd * ratio
            new_usd = max(0.0, cur_usd - closed_usd)
            o["remaining_size_usd"] = new_usd
            o["remaining_size_eth"] = new_usd / o["buy_price"] if o["buy_price"] > 0 else 0.0

    def split_display_usd(total_usd: float):
        if order_size_usd <= 0:
            total_int = max(0, int(round(total_usd)))
            left = total_int // 3
            right = total_int // 3
            mid = total_int - left - right
            return left, mid, right
        total_lots = max(0, int(round(total_usd / order_size_usd)))
        left_lots = total_lots // 3
        right_lots = total_lots // 3
        mid_lots = total_lots - left_lots - right_lots
        return (
            int(round(left_lots * order_size_usd)),
            int(round(mid_lots * order_size_usd)),
            int(round(right_lots * order_size_usd)),
        )

    def split_tp_sizes_eth(total_size_usd: float, avg_px: Optional[float]):
        if avg_px is None or avg_px <= 0 or total_size_usd <= 0:
            return 0.0, 0.0
        left_usd, mid_usd, _ = split_display_usd(total_size_usd)
        return left_usd / avg_px, mid_usd / avg_px

    def do_restart(ts_str, spot, reason, avg_e=None, pnl=None):
        nonlocal grid_start_price, run_number, orders
        nonlocal first_fill_price, min_spot_since_first_fill
        nonlocal tp1_filled, tp2_filled
        nonlocal tp1_size_eth, tp2_size_eth
        nonlocal tp1_price, tp2_price, tp3_price
        nonlocal tp1_realized, tp2_realized
        nonlocal cycle_fills, cycle_after_tp1
        nonlocal legacy_orders
        nonlocal new_cycle_started
        info = {
            "ts": ts_str, "action": "grid_restart", "run": run_number,
            "trigger_price": round(spot, 2), "reason": reason,
        }
        if avg_e is not None: info["avg_entry"] = round(avg_e, 2)
        if pnl  is not None: info["pnl_usd"]   = round(pnl, 2)
        trades.append(info)
        grid_start_price = spot
        run_number += 1
        orders = make_grid(grid_start_price, run_number)
        trades.append({
            "ts": ts_str, "action": "grid_start", "run": run_number,
            "start_price": round(grid_start_price, 2), "orders": max_orders,
        })
        # Reset split-TP state
        first_fill_price = None
        min_spot_since_first_fill = None
        tp1_filled = False; tp2_filled = False
        tp1_size_eth = 0.0; tp2_size_eth = 0.0
        tp1_price = None; tp2_price = None; tp3_price = None
        tp1_realized = 0.0; tp2_realized = 0.0
        cycle_fills = []
        cycle_after_tp1 = False
        legacy_orders = []
        new_cycle_started = False

    day = date_from
    while day <= date_to:
        d_str = day.isoformat()
        if d_str not in available:
            day += timedelta(days=1)
            continue

        day_data = DayData(day)
        minutes = day_data.minutes()
        if not minutes:
            day += timedelta(days=1)
            continue

        for ts in minutes:
            ts_str = ts.strftime("%Y-%m-%dT%H:%M")
            spot = day_data.get_spot(ts)
            if spot is None:
                continue

            # ── Init grid on first tick ──
            if grid_start_price is None:
                grid_start_price = spot
                run_number += 1
                orders = make_grid(grid_start_price, run_number)
                trades.append({
                    "ts": ts_str, "action": "grid_start", "run": run_number,
                    "start_price": round(grid_start_price, 2), "orders": max_orders,
                })

            # ── Check new buy fills ──
            newly_filled = []
            for o in orders:
                if not o["filled"] and spot <= o["buy_price"]:
                    o["filled"] = True
                    o["remaining_size_eth"] = o.get("size_eth", 0.0)
                    o["remaining_size_usd"] = o.get("size_usd", 0.0)
                    newly_filled.append(o)

            # ── Compute current position ──
            open_longs, total_size_eth, avg_entry, unified_tp, total_size_usd = calc_position(orders)
            if position_avg_entry is None and avg_entry is not None:
                position_avg_entry = avg_entry

            # Track min spot and first fill for swing_down calculation
            if newly_filled and first_fill_price is None:
                first_fill_price = newly_filled[0]["buy_price"]
                min_spot_since_first_fill = spot
            if first_fill_price is not None:
                if min_spot_since_first_fill is None or spot < min_spot_since_first_fill:
                    min_spot_since_first_fill = spot

            # Recompute TP levels whenever position changes.
            # Split TP (3 levels) activates only when use_split_tp=True AND ≥3 orders filled.
            # Otherwise a single unified TP is used regardless of use_split_tp flag.
            _split_active = use_split_tp and len(open_longs) >= 3

            if open_longs and avg_entry is not None and not cycle_after_tp1:
                if _split_active:
                    swing_down = (first_fill_price - min_spot_since_first_fill) if (first_fill_price and min_spot_since_first_fill) else 0.0
                    _tp2 = avg_entry * (1 + tp_pct / 100)
                    if swing_down > 0:
                        _tp1 = _tp2 - (tp1_offset_pct / 100) * swing_down
                        _tp3 = _tp2 + (tp3_offset_pct / 100) * swing_down
                    else:
                        _tp1 = _tp2; _tp3 = _tp2  # fallback: all at same level (effectively unified)
                    # Recalculate sizes only if TPs haven't been partially hit yet
                    if not tp1_filled and not tp2_filled:
                        _s1, _s2 = split_tp_sizes_eth(total_size_usd, avg_entry)
                        tp1_size_eth = _s1
                        tp2_size_eth = _s2
                    tp1_price = _tp1; tp2_price = _tp2; tp3_price = _tp3
                else:
                    # Fewer than 3 fills — single unified TP for whole position
                    unified_tp = avg_entry * (1 + tp_pct / 100)
                    tp1_price = None; tp2_price = None; tp3_price = None
                    tp1_filled = False; tp2_filled = False
                    tp1_size_eth = 0.0; tp2_size_eth = 0.0
            elif open_longs and avg_entry is not None and cycle_after_tp1:
                # New cycle after TP1: compute TPs for new cycle fills
                if cycle_fills:
                    _cycle_sz = sum(f["size_eth"] for f in cycle_fills)
                    _cycle_size_usd = sum(f.get("size_usd", order_size_usd) for f in cycle_fills)
                    _cycle_avg = sum(f["buy_price"] * f.get("size_usd", order_size_usd) for f in cycle_fills) / _cycle_size_usd
                    _cy_split = use_split_tp and len(cycle_fills) >= 3
                    _cy_swing = (cycle_fills[0]["buy_price"] - min_spot_since_first_fill) if min_spot_since_first_fill else 0.0
                    _cy_tp2 = _cycle_avg * (1 + tp_pct / 100)
                    unified_tp = _cy_tp2
                    if _cy_split:
                        if _cy_swing > 0:
                            tp1_price = _cy_tp2 - (tp1_offset_pct / 100) * _cy_swing
                            tp3_price = _cy_tp2 + (tp3_offset_pct / 100) * _cy_swing
                        else:
                            tp1_price = _cy_tp2; tp3_price = _cy_tp2
                        tp2_price = _cy_tp2
                        tp1_filled = False
                        _cy_s1, _cy_s2 = split_tp_sizes_eth(_cycle_size_usd, _cycle_avg)
                        tp1_size_eth = _cy_s1
                        tp2_size_eth = _cy_s2
                    else:
                        tp1_price = None
                        tp2_price = _cy_tp2
                        tp3_price = None
                        tp1_filled = False
                        tp2_filled = False
                        tp1_size_eth = 0.0
                        tp2_size_eth = 0.0

            # Track fills for current cycle avg entry
            for o in newly_filled:
                cycle_fills.append({"buy_price": o["buy_price"], "size_eth": o["size_eth"], "size_usd": o["size_usd"]})

            # Recompute new-cycle TP state after appending newly_filled,
            # so the first/second fill after TP1 uses single Avg TP,
            # and split TP activates only from the 3rd fill onward.
            if cycle_after_tp1 and cycle_fills:
                _cycle_sz = sum(f["size_eth"] for f in cycle_fills)
                _cycle_size_usd = sum(f.get("size_usd", order_size_usd) for f in cycle_fills)
                _cycle_avg = sum(f["buy_price"] * f.get("size_usd", order_size_usd) for f in cycle_fills) / _cycle_size_usd
                _cy_split = use_split_tp and len(cycle_fills) >= 3
                _cy_swing = (cycle_fills[0]["buy_price"] - min_spot_since_first_fill) if min_spot_since_first_fill else 0.0
                _cy_tp2 = _cycle_avg * (1 + tp_pct / 100)
                unified_tp = _cy_tp2
                if _cy_split:
                    if _cy_swing > 0:
                        tp1_price = _cy_tp2 - (tp1_offset_pct / 100) * _cy_swing
                        tp3_price = _cy_tp2 + (tp3_offset_pct / 100) * _cy_swing
                    else:
                        tp1_price = _cy_tp2
                        tp3_price = _cy_tp2
                    tp2_price = _cy_tp2
                    tp1_filled = False
                    _cy_s1, _cy_s2 = split_tp_sizes_eth(_cycle_size_usd, _cycle_avg)
                    tp1_size_eth = _cy_s1
                    tp2_size_eth = _cy_s2
                else:
                    tp1_price = None
                    tp2_price = _cy_tp2
                    tp3_price = None
                    tp1_filled = False
                    tp2_filled = False
                    tp1_size_eth = 0.0
                    tp2_size_eth = 0.0

            # BUY fills are the only events that may change overall Avg Entry price.
            if newly_filled and avg_entry is not None:
                position_avg_entry = avg_entry

            # Log new fills with updated avg/tp
            for o in newly_filled:
                trades.append({
                    "ts": ts_str, "action": "buy_fill",
                    "order_id": o["id"], "run": o["run"],
                    "buy_price": round(o["buy_price"], 2),
                    "size_eth": round(o["size_eth"], 6),
                    "size_usd": round(o["size_usd"], 2),
                    "spot": round(spot, 2),
                    "avg_entry_after": round(avg_entry, 2) if avg_entry else None,
                    "unified_tp_after": round(unified_tp, 2) if unified_tp else None,
                    "tp1_after": round(tp1_price, 2) if tp1_price else None,
                    "tp2_after": round(tp2_price, 2) if tp2_price else None,
                    "tp3_after": round(tp3_price, 2) if tp3_price else None,
                })

            # ── Re-anchor: no fills, price moved >0.5% above grid start ──
            if not open_longs and grid_start_price is not None and spot >= grid_start_price * 1.005:
                do_restart(ts_str, spot, "price_above_grid_start_0.5pct")
                open_longs, total_size_eth, avg_entry, unified_tp, total_size_usd = calc_position(orders)

            # ── Check TPs ──
            elif open_longs and avg_entry is not None:
                if position_avg_entry is not None:
                    avg_entry = position_avg_entry
                _overall_avg_entry_before_tp = avg_entry
                # ── Legacy TP orders from old cycles remain active until filled ──
                if legacy_orders:
                    for lg in legacy_orders:
                        if lg.get("filled"):
                            continue
                        if lg.get("tp_price") is None or spot < lg["tp_price"]:
                            continue
                        lg["filled"] = True
                        _lg_usd = lg.get("size_usd_display", lg["size_eth"] * lg["avg_entry"])
                        pnl_legacy = _lg_usd * (lg["tp_price"] / avg_entry - 1.0) if avg_entry and avg_entry > 0 else 0.0
                        realized_pnl += pnl_legacy
                        equity += pnl_legacy
                        reduce_position_usd(lg.get("size_usd_display", lg["size_eth"] * lg["avg_entry"]))
                        trades.append({
                            "ts": ts_str, "action": "tp_fill", "tp_level": lg["tp_level"],
                            "run": run_number, "legacy": True,
                            "legacy_source_run": lg["source_run"],
                            "tp_price": round(lg["tp_price"], 2),
                            "avg_entry": round(avg_entry, 2) if avg_entry is not None else None,
                            "size_eth": round(lg["size_eth"], 6),
                            "pnl_usd": round(pnl_legacy, 2),
                            "spot": round(spot, 2),
                            "equity": round(equity, 2),
                            "unified_tp": round(lg["tp_price"], 2),
                        })
                    open_longs, total_size_eth, _recalc_avg_entry, unified_tp, total_size_usd = calc_position(orders)
                    if total_size_usd <= 1e-12:
                        avg_entry = None
                        position_avg_entry = None
                    else:
                        avg_entry = _overall_avg_entry_before_tp
                        position_avg_entry = _overall_avg_entry_before_tp
                if use_split_tp and tp1_price is not None and tp1_price != tp2_price:
                    # ── Split TP mode ──
                    # TP1 hit
                    if not tp1_filled and spot >= tp1_price:
                        tp1_filled = True
                        _tp_cycle_size = total_size_eth
                        _tp_cycle_size_usd = total_size_usd
                        _tp_cycle_avg = avg_entry
                        if cycle_fills:
                            _tp_cycle_size = sum(f["size_eth"] for f in cycle_fills)
                            _tp_cycle_size_usd = sum(f.get("size_usd", order_size_usd) for f in cycle_fills)
                            # Preserve overall Avg Entry price on TP1; do not recalculate it from cycle-only fills.
                        _tp1_usd = split_display_usd(_tp_cycle_size_usd)[0]
                        pnl1 = _tp1_usd * (tp1_price / _tp_cycle_avg - 1.0) if _tp_cycle_avg and _tp_cycle_avg > 0 else 0.0
                        tp1_realized = pnl1
                        realized_pnl += pnl1
                        equity += pnl1
                        # Freeze TP2/TP3 at current prices/sizes/avg — they belong to current cycle only
                        old_cycle_run = run_number
                        _disp_tp1_usd, _disp_tp2_usd, _disp_tp3_usd = split_display_usd(_tp_cycle_size_usd)
                        legacy_orders.append({
                            "source_run": old_cycle_run,
                            "tp_level": 2,
                            "tp_price": tp2_price,
                            "size_eth": tp2_size_eth,
                            "avg_entry": _tp_cycle_avg,
                            "size_usd_display": _disp_tp2_usd,
                            "filled": False,
                        })
                        legacy_orders.append({
                            "source_run": old_cycle_run,
                            "tp_level": 3,
                            "tp_price": tp3_price,
                            "size_eth": max(0.0, _tp_cycle_size - tp1_size_eth - tp2_size_eth),
                            "avg_entry": _tp_cycle_avg,
                            "size_usd_display": _disp_tp3_usd,
                            "filled": False,
                        })
                        reduce_position_usd(_disp_tp1_usd)
                        # Start new sub-grid from current spot (new cycle)
                        # Keep legacy TP state for already-filled old-cycle position,
                        # but drop stale unfilled BUY limits from the old cycle.
                        orders = [o for o in orders if o["filled"] and o.get("remaining_size_eth", 0.0) > 1e-12]
                        new_cycle_started = True
                        cycle_after_tp1 = True
                        cycle_fills = []
                        # Reset swing_down tracking for new cycle
                        first_fill_price = None
                        min_spot_since_first_fill = None
                        run_number += 1
                        new_cycle_orders = make_grid(spot, run_number)
                        orders.extend(new_cycle_orders)
                        grid_start_price = spot
                        trades.append({
                            "ts": ts_str, "action": "grid_start", "run": run_number,
                            "start_price": round(spot, 2), "orders": max_orders,
                            "reason": "new_cycle_after_tp1",
                        })
                        # Mark tp1_size_eth orders as partially closed (reduce open longs virtually)
                        trades.append({
                            "ts": ts_str, "action": "tp_fill", "tp_level": 1,
                            "run": run_number,
                            "tp_price": round(tp1_price, 2),
                            "avg_entry": round(avg_entry, 2),
                            "size_eth": round(tp1_size_eth, 6),
                            "pnl_usd": round(pnl1, 2),
                            "spot": round(spot, 2),
                            "equity": round(equity, 2),
                            "unified_tp": round(tp1_price, 2),
                        })
                    # ── Current cycle TP2/TP3 (only active before TP1 hit) ──
                    if not cycle_after_tp1:
                        if not tp2_filled and spot >= tp2_price:
                            tp2_filled = True
                            _tp2_usd = split_display_usd(total_size_usd)[1]
                            pnl2 = _tp2_usd * (tp2_price / avg_entry - 1.0) if avg_entry and avg_entry > 0 else 0.0
                            tp2_realized = pnl2
                            realized_pnl += pnl2
                            equity += pnl2
                            reduce_position_usd(split_display_usd(total_size_usd)[1])
                            trades.append({
                                "ts": ts_str, "action": "tp_fill", "tp_level": 2,
                                "run": run_number,
                                "tp_price": round(tp2_price, 2),
                                "avg_entry": round(avg_entry, 2),
                                "size_eth": round(tp2_size_eth, 6),
                                "pnl_usd": round(pnl2, 2),
                                "spot": round(spot, 2),
                                "equity": round(equity, 2),
                                "unified_tp": round(tp2_price, 2),
                            })
                        # TP3 hit → close remainder + restart
                        if spot >= tp3_price and tp1_filled and tp2_filled:
                            tp3_size = total_size_eth - tp1_size_eth - tp2_size_eth
                            pnl3 = (tp3_price - avg_entry) * tp3_size
                            realized_pnl += pnl3
                            equity += pnl3
                            reduce_position_usd(split_display_usd(total_size_usd)[2])
                            total_pnl_run = tp1_realized + tp2_realized + pnl3
                            trades.append({
                                "ts": ts_str, "action": "tp_fill", "tp_level": 3,
                                "run": run_number,
                                "tp_price": round(tp3_price, 2),
                                "avg_entry": round(avg_entry, 2),
                                "size_eth": round(tp3_size, 6),
                                "pnl_usd": round(pnl3, 2),
                                "spot": round(spot, 2),
                                "equity": round(equity, 2),
                                "unified_tp": round(tp3_price, 2),
                                "total_run_pnl": round(total_pnl_run, 2),
                            })
                            do_restart(ts_str, spot, "split_tp3_hit", avg_e=avg_entry, pnl=total_pnl_run)
                            open_longs, total_size_eth, avg_entry, unified_tp, total_size_usd = calc_position(orders)
                        # TP3 hit before TP1/TP2 (edge case: gap up) → close all
                        elif spot >= tp3_price and not (tp1_filled and tp2_filled):
                            remaining = total_size_eth - (tp1_size_eth if tp1_filled else 0) - (tp2_size_eth if tp2_filled else 0)
                            _tp3_usd = split_display_usd(total_size_usd)[2]
                            pnl_r = _tp3_usd * (tp3_price / avg_entry - 1.0) if avg_entry and avg_entry > 0 else 0.0
                            realized_pnl += pnl_r
                            equity += pnl_r
                            reduce_position_usd(split_display_usd(total_size_usd)[2])
                            trades.append({
                                "ts": ts_str, "action": "tp_fill", "tp_level": 3,
                                "run": run_number,
                                "tp_price": round(tp3_price, 2),
                                "avg_entry": round(avg_entry, 2),
                                "size_eth": round(remaining, 6),
                                "pnl_usd": round(pnl_r, 2),
                                "spot": round(spot, 2),
                                "equity": round(equity, 2),
                                "unified_tp": round(tp3_price, 2),
                            })
                            do_restart(ts_str, spot, "split_tp3_hit_gap", avg_e=avg_entry, pnl=pnl_r)
                            open_longs, total_size_eth, avg_entry, unified_tp, total_size_usd = calc_position(orders)
                else:
                    # ── Single TP mode (use_split_tp=False, or new-cycle has <3 fills) ──
                    if cycle_after_tp1 and cycle_fills and tp2_price is not None and spot >= tp2_price:
                        _cycle_size_usd = sum(f.get("size_usd", order_size_usd) for f in cycle_fills)
                        _cycle_avg = sum(f["buy_price"] * f.get("size_usd", order_size_usd) for f in cycle_fills) / _cycle_size_usd
                        pnl_usd = _cycle_size_usd * (tp2_price / _cycle_avg - 1.0) if _cycle_avg and _cycle_avg > 0 else 0.0
                        realized_pnl += pnl_usd
                        equity += pnl_usd
                        reduce_position_usd(_cycle_size_usd)
                        trades.append({
                            "ts": ts_str, "action": "tp_fill",
                            "run": run_number,
                            "avg_entry": round(_cycle_avg, 2),
                            "unified_tp": round(tp2_price, 2),
                            "total_size_eth": round(_cycle_size_usd / _cycle_avg, 6) if _cycle_avg else 0.0,
                            "filled_orders": len(cycle_fills),
                            "pnl_usd": round(pnl_usd, 2),
                            "spot": round(spot, 2),
                            "equity": round(equity, 2),
                        })
                        cycle_fills = []
                        tp1_price = None; tp2_price = None; tp3_price = None
                        tp1_filled = False; tp2_filled = False
                        tp1_size_eth = 0.0; tp2_size_eth = 0.0
                        unified_tp = None
                        open_longs, total_size_eth, avg_entry, unified_tp, total_size_usd = calc_position(orders)
                    elif (not cycle_after_tp1) and unified_tp is not None and spot >= unified_tp:
                        pnl_usd = total_size_usd * (unified_tp / avg_entry - 1.0) if avg_entry and avg_entry > 0 else 0.0
                        realized_pnl += pnl_usd
                        equity += pnl_usd
                        reduce_position_usd(total_size_usd)
                        trades.append({
                            "ts": ts_str, "action": "tp_fill",
                            "run": run_number,
                            "avg_entry": round(avg_entry, 2),
                            "unified_tp": round(unified_tp, 2),
                            "total_size_eth": round(total_size_eth, 6),
                            "filled_orders": len(open_longs),
                            "pnl_usd": round(pnl_usd, 2),
                            "spot": round(spot, 2),
                            "equity": round(equity, 2),
                        })
                        do_restart(ts_str, spot, "unified_tp_hit", avg_e=avg_entry, pnl=pnl_usd)
                        open_longs, total_size_eth, avg_entry, unified_tp, total_size_usd = calc_position(orders)

            # Refresh position after any TP-side size reductions, but keep Avg Entry price unchanged on partial exits.
            _prev_avg_entry = _overall_avg_entry_before_tp if '_overall_avg_entry_before_tp' in locals() else avg_entry
            open_longs, total_size_eth, _recalc_avg_entry, unified_tp, total_size_usd = calc_position(orders)
            if open_longs and _prev_avg_entry is not None:
                avg_entry = _prev_avg_entry
                position_avg_entry = _prev_avg_entry
            else:
                avg_entry = _recalc_avg_entry
                position_avg_entry = _recalc_avg_entry

            # Adjust avg_tp for snapshots: show tp2 as primary level
            _snap_avg_tp = (tp2_price if (use_split_tp and tp2_price and not cycle_after_tp1) else unified_tp)

            minute_counter += 1

            # ── Mark-to-market ──
            unrealized_pnl = sum((spot / o["buy_price"] - 1.0) * o.get("remaining_size_usd", o["size_usd"]) for o in open_longs)
            mark_equity = equity + unrealized_pnl

            if (minute_counter % snapshot_stride) == 0:
                equity_curve.append({
                    "ts": ts_str,
                    "equity": round(mark_equity, 2),
                    "spot": round(spot, 2),
                    "realized_pnl": round(realized_pnl, 2),
                    "unrealized_pnl": round(unrealized_pnl, 2),
                    "total_mark_pnl_usd": round(mark_equity - initial_capital, 2),
                    "open_longs": len(open_longs),
                    "run": run_number,
                    "avg_entry": round(avg_entry, 2) if avg_entry is not None else None,
                    "avg_tp": round(_snap_avg_tp, 2) if _snap_avg_tp is not None else None,
                })

            # Compute cycle avg entry / size for current virtual cycle
            _cycle_size = sum(f["size_eth"] for f in cycle_fills)
            _cycle_size_usd = sum(f.get("size_usd", order_size_usd) for f in cycle_fills)
            _cycle_avg_entry = (
                sum(f["buy_price"] * f.get("size_usd", order_size_usd) for f in cycle_fills) / _cycle_size_usd
                if _cycle_size_usd > 0 else None
            )

            # Emit payoff snapshots adaptively for long ranges to keep response size bounded.
            if (minute_counter % snapshot_stride) == 0:
                payoff_snapshots.append({
                    "viewer_type": "futures_algo1_pnl",
                    "ts": ts_str,
                    "spot": round(spot, 2),
                    "run": run_number,
                    "open_longs": len(open_longs),
                    "total_size_eth": round(total_size_eth, 6),
                    "current_lots": round(total_size_usd / order_size_usd, 6) if (order_size_usd > 0) else None,
                    "avg_entry": round(avg_entry, 2) if avg_entry is not None else None,
                    "avg_tp": round(_snap_avg_tp, 2) if _snap_avg_tp is not None else None,
                    "tp1": round(tp1_price, 2) if (use_split_tp and tp1_price and not cycle_after_tp1) else None,
                    "tp2": round(tp2_price, 2) if (use_split_tp and tp2_price and not cycle_after_tp1) else None,
                    "tp3": round(tp3_price, 2) if (use_split_tp and tp3_price and not cycle_after_tp1) else None,
                    "tp1_size_usd": split_display_usd(total_size_usd)[0] if (use_split_tp and tp1_price and not cycle_after_tp1 and total_size_usd > 0) else None,
                    "tp2_size_usd": split_display_usd(total_size_usd)[1] if (use_split_tp and tp2_price and not cycle_after_tp1 and total_size_usd > 0) else None,
                    "tp3_size_usd": split_display_usd(total_size_usd)[2] if (use_split_tp and tp3_price and not cycle_after_tp1 and total_size_usd > 0) else None,
                    "tp1_hit": tp1_filled,
                    "tp2_hit": tp2_filled,
                    "after_tp1": cycle_after_tp1,
                    "legacy_tp2": None,
                    "legacy_tp3": None,
                    "legacy_orders": [
                        {
                            "source_run": lg["source_run"],
                            "tp_level": lg["tp_level"],
                            "tp_price": round(lg["tp_price"], 2),
                            "size_eth": round(lg["size_eth"], 6),
                            "size_usd": int(lg.get("size_usd_display", round(lg["size_eth"] * lg["avg_entry"]))),
                            "avg_entry": round(lg["avg_entry"], 2),
                            "label": f"Legacy R{lg['source_run']} TP{lg['tp_level']} ${int(lg.get('size_usd_display', round(lg['size_eth'] * lg['avg_entry'])))} @ {lg['tp_price']:.2f}",
                        }
                        for lg in legacy_orders if not lg.get("filled") and lg.get("tp_price") is not None
                    ],
                    "new_tp1": round(tp1_price, 2) if (cycle_after_tp1 and _cycle_size > 0 and tp1_price) else None,
                    "new_tp2": round(tp2_price, 2) if (cycle_after_tp1 and _cycle_size > 0 and tp2_price) else None,
                    "new_tp3": round(tp3_price, 2) if (cycle_after_tp1 and _cycle_size > 0 and tp3_price) else None,
                    "new_tp1_size_usd": split_display_usd(_cycle_size_usd)[0] if (cycle_after_tp1 and _cycle_size_usd > 0 and tp1_price and _cycle_avg_entry is not None) else None,
                    "new_tp2_size_usd": split_display_usd(_cycle_size_usd)[1] if (cycle_after_tp1 and _cycle_size_usd > 0 and tp2_price and _cycle_avg_entry is not None) else None,
                    "new_tp3_size_usd": split_display_usd(_cycle_size_usd)[2] if (cycle_after_tp1 and _cycle_size_usd > 0 and tp3_price and _cycle_avg_entry is not None) else None,
                    "cycle_avg_entry": round(_cycle_avg_entry, 2) if _cycle_avg_entry is not None else None,
                    "cycle_size_eth": round(_cycle_size, 6),
                    "cycle_lots": round(_cycle_size_usd / order_size_usd, 6) if (order_size_usd > 0) else None,
                    "realized_pnl_usd": round(realized_pnl, 2),
                    "unrealized_pnl_usd": round(unrealized_pnl, 2),
                    "total_mark_pnl_usd": round(mark_equity - initial_capital, 2),
                    "equity": round(mark_equity, 2),
                })

        day += timedelta(days=1)

    # ── Statistics ──
    pnl_list = [t["pnl_usd"] for t in trades if t.get("action") == "tp_fill"]
    total_pnl = sum(pnl_list)
    n_trades  = len(pnl_list)
    winners   = [p for p in pnl_list if p > 0]
    win_rate  = len(winners) / n_trades * 100 if n_trades else 0
    avg_win   = float(np.mean(winners)) if winners else 0.0
    total_return_pct = total_pnl / initial_capital * 100

    peak = initial_capital
    max_dd = 0.0
    for e in equity_curve:
        eq = e["equity"]
        if eq > peak: peak = eq
        dd = (peak - eq) / peak * 100
        if dd > max_dd: max_dd = dd

    days_count = max((date_to - date_from).days, 1)
    ann_return = ((1 + total_return_pct / 100) ** (365 / days_count) - 1) * 100

    if equity_curve:
        last_eq = equity_curve[-1]
        if last_eq.get("ts") != ts_str:
            equity_curve.append({
                "ts": ts_str,
                "equity": round(mark_equity, 2),
                "spot": round(spot, 2),
                "realized_pnl": round(realized_pnl, 2),
                "unrealized_pnl": round(unrealized_pnl, 2),
                "total_mark_pnl_usd": round(mark_equity - initial_capital, 2),
                "open_longs": len(open_longs),
                "run": run_number,
                "avg_entry": round(avg_entry, 2) if avg_entry is not None else None,
                "avg_tp": round(_snap_avg_tp, 2) if _snap_avg_tp is not None else None,
            })
    if payoff_snapshots:
        last_snap = payoff_snapshots[-1]
        if last_snap.get("ts") != ts_str:
            payoff_snapshots.append({
                "viewer_type": "futures_algo1_pnl",
                "ts": ts_str,
                "spot": round(spot, 2),
                "run": run_number,
                "open_longs": len(open_longs),
                "total_size_eth": round(total_size_eth, 6),
                "current_lots": round(total_size_usd / order_size_usd, 6) if (order_size_usd > 0) else None,
                "avg_entry": round(avg_entry, 2) if avg_entry is not None else None,
                "avg_tp": round(_snap_avg_tp, 2) if _snap_avg_tp is not None else None,
                "tp1": round(tp1_price, 2) if (use_split_tp and tp1_price and not cycle_after_tp1) else None,
                "tp2": round(tp2_price, 2) if (use_split_tp and tp2_price and not cycle_after_tp1) else None,
                "tp3": round(tp3_price, 2) if (use_split_tp and tp3_price and not cycle_after_tp1) else None,
                "tp1_size_usd": split_display_usd(total_size_usd)[0] if (use_split_tp and tp1_price and not cycle_after_tp1 and total_size_usd > 0) else None,
                "tp2_size_usd": split_display_usd(total_size_usd)[1] if (use_split_tp and tp2_price and not cycle_after_tp1 and total_size_usd > 0) else None,
                "tp3_size_usd": split_display_usd(total_size_usd)[2] if (use_split_tp and tp3_price and not cycle_after_tp1 and total_size_usd > 0) else None,
                "tp1_hit": tp1_filled,
                "tp2_hit": tp2_filled,
                "after_tp1": cycle_after_tp1,
                "legacy_tp2": None,
                "legacy_tp3": None,
                "legacy_orders": [
                    {
                        "source_run": lg["source_run"],
                        "tp_level": lg["tp_level"],
                        "tp_price": round(lg["tp_price"], 2),
                        "size_eth": round(lg["size_eth"], 6),
                        "size_usd": int(lg.get("size_usd_display", round(lg["size_eth"] * lg["avg_entry"]))),
                        "avg_entry": round(lg["avg_entry"], 2),
                        "label": f"Legacy R{lg['source_run']} TP{lg['tp_level']} ${int(lg.get('size_usd_display', round(lg['size_eth'] * lg['avg_entry'])))} @ {lg['tp_price']:.2f}",
                    }
                    for lg in legacy_orders if not lg.get("filled") and lg.get("tp_price") is not None
                ],
                "new_tp1": round(tp1_price, 2) if (cycle_after_tp1 and _cycle_size > 0 and tp1_price) else None,
                "new_tp2": round(tp2_price, 2) if (cycle_after_tp1 and _cycle_size > 0 and tp2_price) else None,
                "new_tp3": round(tp3_price, 2) if (cycle_after_tp1 and _cycle_size > 0 and tp3_price) else None,
                "new_tp1_size_usd": split_display_usd(_cycle_size_usd)[0] if (cycle_after_tp1 and _cycle_size_usd > 0 and tp1_price and _cycle_avg_entry is not None) else None,
                "new_tp2_size_usd": split_display_usd(_cycle_size_usd)[1] if (cycle_after_tp1 and _cycle_size_usd > 0 and tp2_price and _cycle_avg_entry is not None) else None,
                "new_tp3_size_usd": split_display_usd(_cycle_size_usd)[2] if (cycle_after_tp1 and _cycle_size_usd > 0 and tp3_price and _cycle_avg_entry is not None) else None,
                "cycle_avg_entry": round(_cycle_avg_entry, 2) if _cycle_avg_entry is not None else None,
                "cycle_size_eth": round(_cycle_size, 6),
                "cycle_lots": round(_cycle_size_usd / order_size_usd, 6) if (order_size_usd > 0) else None,
                "realized_pnl_usd": round(realized_pnl, 2),
                "unrealized_pnl_usd": round(unrealized_pnl, 2),
                "total_mark_pnl_usd": round(mark_equity - initial_capital, 2),
                "equity": round(mark_equity, 2),
            })

    return {
        "strategy": strategy_id,
        "params": {
            "step_pct": step_pct,
            "max_orders": max_orders,
            "tp_pct": tp_pct,
            "order_size_usd": order_size_usd,
            "initial_capital": initial_capital,
            "use_split_tp": use_split_tp,
            "tp1_offset_pct": tp1_offset_pct,
            "tp3_offset_pct": tp3_offset_pct,
        },
        "date_from": date_from.isoformat(),
        "date_to": date_to.isoformat(),
        "stats": {
            "total_pnl_usd": round(total_pnl, 2),
            "total_return_pct": round(total_return_pct, 2),
            "annualized_return_pct": round(ann_return, 2),
            "max_drawdown_pct": round(max_dd, 2),
            "n_trades": n_trades,
            "win_rate_pct": round(win_rate, 2),
            "avg_win_usd": round(avg_win, 2),
            "avg_loss_usd": 0.0,
            "final_equity": round(equity, 2),
            "realized_pnl_usd": round(realized_pnl, 2),
            "grid_restarts": run_number - 1,
        },
        "equity_curve": equity_curve,
        "trades": trades,
        "payoff_snapshots": payoff_snapshots,
    }


# ─── Routes ───────────────────────────────────────────────────────────────────

@app.get("/health")
def health():
    return {"status": "ok", "available_dates": get_available_dates()}

@app.get("/available-dates")
def available_dates():
    return {"dates": get_available_dates()}

@app.get("/futures-spread-algo1/instruments")
def futures_spread_algo1_instruments(date_from: Optional[str] = None, date_to: Optional[str] = None):
    dates = get_available_dates()
    if not dates:
        raise HTTPException(503, "No data available")
    try:
        df = _parse_dt_param(date_from, date.fromisoformat(dates[-1]))
        dt = _parse_dt_param(date_to, df)
    except ValueError:
        raise HTTPException(400, "Invalid date format, use YYYY-MM-DD")
    df = max(df, date.fromisoformat(dates[0]))
    dt = min(dt, date.fromisoformat(dates[-1]))
    return {
        "source": "deribit-md-collector futures_parsed.parquet",
        "instruments": _available_eth_futures_instruments(df, dt),
    }

@app.get("/futures-spread-algo1/history")
def futures_spread_algo1_history(date_from: str, date_to: str, short_ticker: str = "ETH-25DEC26", long_ticker: str = "ETH-PERPETUAL"):
    dates = get_available_dates()
    if not dates:
        raise HTTPException(503, "No data available")
    try:
        df = datetime.strptime(date_from, "%Y-%m-%d").date()
        dt = datetime.strptime(date_to, "%Y-%m-%d").date()
    except ValueError:
        raise HTTPException(400, "Invalid date format, use YYYY-MM-DD")
    df = max(df, date.fromisoformat(dates[0]))
    dt = min(dt, date.fromisoformat(dates[-1]))
    if df > dt:
        raise HTTPException(400, "date_from must be <= date_to")

    short_ticker = (short_ticker or "ETH-25DEC26").strip().upper()
    long_ticker = (long_ticker or "ETH-PERPETUAL").strip().upper()
    spread_name = _spread_instrument_name(short_ticker, long_ticker)
    short_prices = _load_futures_price_series(df, dt, short_ticker)
    long_prices = _load_futures_price_series(df, dt, long_ticker)
    spread_points = _load_native_spread_series(df, dt, spread_name)
    short_ohlc = sum(1 for p in short_prices if p.get("ohlc_available"))
    long_ohlc = sum(1 for p in long_prices if p.get("ohlc_available"))
    candles_available = short_ohlc > 0 and long_ohlc > 0
    missing_reason = None
    if not spread_points:
        missing_reason = (
            f"Missing historical spread source: {DATA_DIR}/spreads/YYYY-MM-DD/"
            f"spreads_parsed.parquet has no instrument_name={spread_name} for {df.isoformat()}..{dt.isoformat()}"
        )
    return {
        "source": {
            "prices": "deribit-md-collector futures_parsed.parquet",
            "spread": "deribit-md-collector spreads_parsed.parquet",
            "candles": "open/high/low/close from Deribit public/get_tradingview_chart_data",
        },
        "candles_available": candles_available,
        "candles_unavailable_reason": None if candles_available else "Real OHLC candles are unavailable for one or both selected instruments in this range",
        "short_ticker": short_ticker,
        "long_ticker": long_ticker,
        "spread_instrument": spread_name,
        "short_prices": short_prices,
        "long_prices": long_prices,
        "spread_points": spread_points,
        "spread_unavailable_reason": missing_reason,
    }

@app.get("/strategies")
def list_strategies():
    return {"strategies": [
        {
            "id": "short_eth_put_daily",
            "name": "Short ETH Put (Daily, minute-level)",
            "description": "At 08:00 UTC each day: sell ETH put at strike -N% from spot, next-day expiry. P&L tracked minute-by-minute using live mark prices.",
            "params": [
                {"id": "strike_offset_pct", "label": "Strike offset %",          "type": "number",  "default": 2.5,   "min": 0.5,  "max": 20,      "step": 0.5},
                {"id": "initial_capital",   "label": "Initial capital (USD)",     "type": "number",  "default": 10000, "min": 100,  "max": 1000000, "step": 100},
                {"id": "lots_per_trade",    "label": "Lots per trade",            "type": "number",  "default": 1,     "min": 1,    "max": 100,     "step": 1},
                {"id": "use_bid",           "label": "Sell at bid (conservative)","type": "boolean", "default": True},
            ]
        },
        {
            "id": "short_eth_strangle_daily",
            "name": "Short ETH Strangle (Daily, minute-level)",
            "description": "At 08:00 UTC each day: sell OTM ETH put (strike -X%) + OTM ETH call (strike +X%), next-day expiry. Collects double premium via theta decay. ETH IV historically exceeds realized vol. P&L tracked minute-by-minute.",
            "params": [
                {"id": "put_offset_pct",  "label": "Put strike offset %",         "type": "number",  "default": 2.5,   "min": 0.5,  "max": 20,      "step": 0.5},
                {"id": "call_offset_pct", "label": "Call strike offset %",        "type": "number",  "default": 2.5,   "min": 0.5,  "max": 20,      "step": 0.5},
                {"id": "initial_capital", "label": "Initial capital (USD)",        "type": "number",  "default": 10000, "min": 100,  "max": 1000000, "step": 100},
                {"id": "lots_per_trade",  "label": "Lots per trade",              "type": "number",  "default": 1,     "min": 1,    "max": 100,     "step": 1},
                {"id": "use_bid",         "label": "Sell at bid (conservative)",  "type": "boolean", "default": True},
            ]
        },
        {
            "id": "futures_algo1",
            "name": "Futures Algo 1 (ETH-PERPETUAL Grid Buy)",
            "description": "Grid buy strategy on ETH-PERPETUAL. Places up to max_orders BUY limit orders below current price spaced step_pct apart. Each fill gets TP at entry+tp_pct%. If >10 orders filled and price returns to avg entry — restart grid from current price, keeping open TPs alive.",
            "params": [
                {"id": "step_pct",        "label": "Grid step %",             "type": "number",  "default": 1.0,   "min": 0.1,  "max": 10,     "step": 0.1},
                {"id": "max_orders",      "label": "Max orders",              "type": "number",  "default": 20,    "min": 1,    "max": 50,     "step": 1},
                {"id": "tp_pct",          "label": "TP offset %",             "type": "number",  "default": 2.0,   "min": 0.1,  "max": 20,     "step": 0.1},
                {"id": "order_size_usd",  "label": "Order size (USD)",        "type": "number",  "default": 5,   "min": 1,   "max": 10000,  "step": 1},
                {"id": "initial_capital", "label": "Initial capital (USD)",   "type": "number",  "default": 1000, "min": 10,  "max": 1000000,"step": 10},
                {"id": "use_split_tp",    "label": "Split TP (3 levels)",       "type": "boolean", "default": True},
                {"id": "tp1_offset_pct",  "label": "TP1 offset % of SwingDown", "type": "number",  "default": 15.0,  "min": 1,    "max": 100,    "step": 1},
                {"id": "tp3_offset_pct",  "label": "TP3 offset % of SwingDown", "type": "number",  "default": 15.0,  "min": 1,    "max": 100,    "step": 1},
            ]
        },
        {
            "id": "futures_spread_algo1",
            "name": "Futures Spread Algo 1",
            "description": "Spread state machine: sell Short leg after first upper envelope trigger, then buy Long leg by limit order.",
            "params": [
                {"id": "short_ticker",    "label": "Short ticker",           "type": "text",    "default": "ETH-25DEC26"},
                {"id": "long_ticker",     "label": "Long ticker",            "type": "text",    "default": "ETH-PERPETUAL"},
                {"id": "envelope_step_pct","label": "Envelope Step, %",       "type": "number",  "default": 0.1,   "min": 0.001,"max": 10,     "step": 0.1},
                {"id": "fee_pct",          "label": "Fee, %",                 "type": "number",  "default": 0,     "min": 0,    "max": 10,     "step": 0.01},
                {"id": "order_size_usd",  "label": "Order size (USD)",        "type": "number",  "default": 5,   "min": 1,   "max": 10000,  "step": 1},
                {"id": "initial_capital", "label": "Initial capital (USD)",   "type": "number",  "default": 1000, "min": 10,  "max": 1000000,"step": 10},
            ]
        },
    ]}

class RunRequest(BaseModel):
    strategy_id: str
    date_from: str
    date_to: str
    params: Optional[Dict[str, Any]] = None

@app.post("/run")
def run_backtest(req: RunRequest):
    try:
        df = datetime.strptime(req.date_from, "%Y-%m-%d").date()
        dt = datetime.strptime(req.date_to,   "%Y-%m-%d").date()
    except ValueError:
        raise HTTPException(400, "Invalid date format, use YYYY-MM-DD")

    avail = get_available_dates()
    if not avail:
        raise HTTPException(503, "No data available")

    df = max(df, date.fromisoformat(avail[0]))
    dt = min(dt, date.fromisoformat(avail[-1]))
    if df > dt:
        raise HTTPException(400, "date_from must be <= date_to")

    p = req.params or {}

    if req.strategy_id == "short_eth_put_daily":
        result = run_short_eth_put_strategy(
            date_from=df, date_to=dt,
            strike_offset_pct=float(p.get("strike_offset_pct", 2.5)),
            initial_capital=float(p.get("initial_capital", 10000)),
            lots_per_trade=int(p.get("lots_per_trade", 1)),
            use_bid=bool(p.get("use_bid", True)),
        )
        return JSONResponse(content=_sanitize(result))
    elif req.strategy_id == "short_eth_strangle_daily":
        result = run_short_eth_strangle_strategy(
            date_from=df, date_to=dt,
            put_offset_pct=float(p.get("put_offset_pct", 2.5)),
            call_offset_pct=float(p.get("call_offset_pct", 2.5)),
            initial_capital=float(p.get("initial_capital", 10000)),
            lots_per_trade=int(p.get("lots_per_trade", 1)),
            use_bid=bool(p.get("use_bid", True)),
        )
        return JSONResponse(content=_sanitize(result))
    elif req.strategy_id == "futures_algo1":
        result = run_futures_algo1_strategy(
            date_from=df, date_to=dt,
            strategy_id=req.strategy_id,
            step_pct=float(p.get("step_pct", 1.0)),
            max_orders=int(p.get("max_orders", 20)),
            tp_pct=float(p.get("tp_pct", 2.0)),
            order_size_usd=float(p.get("order_size_usd", 5)),
            initial_capital=float(p.get("initial_capital", 1000)),
            use_split_tp=bool(p.get("use_split_tp", True)),
            tp1_offset_pct=float(p.get("tp1_offset_pct", 15.0)),
            tp3_offset_pct=float(p.get("tp3_offset_pct", 15.0)),
        )
        return JSONResponse(content=_sanitize(result))
    elif req.strategy_id == "futures_spread_algo1":
        result = run_futures_spread_algo1_strategy(
            date_from=df, date_to=dt,
            short_ticker=str(p.get("short_ticker", "ETH-25DEC26")),
            long_ticker=str(p.get("long_ticker", "ETH-PERPETUAL")),
            envelope_step_pct=float(p.get("envelope_step_pct", 0.1)),
            order_size_usd=float(p.get("order_size_usd", 5)),
            initial_capital=float(p.get("initial_capital", 1000)),
            fee_pct=float(p.get("fee_pct", 0)),
        )
        return JSONResponse(content=_sanitize(result))
    else:
        raise HTTPException(404, f"Unknown strategy: {req.strategy_id}")
