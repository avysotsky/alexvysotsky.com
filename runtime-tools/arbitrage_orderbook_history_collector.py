#!/usr/bin/env python3
from __future__ import annotations

import asyncio
import contextlib
import datetime as dt
import json
import os
import signal
import urllib.parse
import urllib.request
from pathlib import Path

import pyarrow as pa
import pyarrow.parquet as pq
import websockets

ROOT = Path("/home/user/VAN-v2/data/arbitrage_orderbook_history")
LIVE_LATEST_PATH = Path("/home/user/VAN-v2/data/arbitrage_orderbook_live_latest.json")
LIVE_WS_HOST = "0.0.0.0"
LIVE_WS_PORT = 38133
LIVE_SECONDS = 1
SNAPSHOT_SECONDS = 5
BINANCE_STREAM_CHUNK = 80
MEXC_SUB_CHUNK = 80
BIDASK_FILTER_WINDOW = 20
BIDASK_FILTER_MAX_REL_DEV = 0.01
SPREAD_ALERTS_ENABLED = False
SPREAD_ALERT_THRESHOLD_PCT = 0.5
SPREAD_ALERT_RECOVERY_PCT = 0.4
SPREAD_ALERT_COOLDOWN_SECONDS = 15 * 60
SPREAD_ALERT_ENV = Path("/home/user/VAN-v2/config/arbitrage-alerts.env")
EXCHANGE_MARKET_ID = {
    "binance": "BINANCE_F_PERP",
    "mexc": "MEXC_F_PERP",
}


SCHEMA = pa.schema(
    [
        ("ts_ms", pa.int64()),
        ("ts_utc", pa.string()),
        ("coin", pa.string()),
        ("exchange", pa.string()),
        ("symbol", pa.string()),
        ("bid_px_1", pa.float64()), ("bid_sz_1", pa.float64()),
        ("bid_px_2", pa.float64()), ("bid_sz_2", pa.float64()),
        ("bid_px_3", pa.float64()), ("bid_sz_3", pa.float64()),
        ("bid_px_4", pa.float64()), ("bid_sz_4", pa.float64()),
        ("bid_px_5", pa.float64()), ("bid_sz_5", pa.float64()),
        ("ask_px_1", pa.float64()), ("ask_sz_1", pa.float64()),
        ("ask_px_2", pa.float64()), ("ask_sz_2", pa.float64()),
        ("ask_px_3", pa.float64()), ("ask_sz_3", pa.float64()),
        ("ask_px_4", pa.float64()), ("ask_sz_4", pa.float64()),
        ("ask_px_5", pa.float64()), ("ask_sz_5", pa.float64()),
        ("bid_ask_median", pa.float64()),
        ("funding_rate", pa.float64()),
        ("funding_pct", pa.float64()),
    ]
)


def utcnow() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def hour_key(ts: dt.datetime) -> str:
    return ts.strftime("%Y%m%dT%H0000Z")


def get_json(url: str):
    req = urllib.request.Request(url, headers={"User-Agent": "VAN-v2-arbitrage-history/1.0"})
    with urllib.request.urlopen(req, timeout=20) as resp:
        return json.loads(resp.read().decode("utf-8"))


def fnum(value):
    try:
        n = float(value)
        return n if n == n else None
    except Exception:
        return None


def clean_levels(levels, size_mult: float = 1.0, reverse: bool = False):
    out = []
    for item in levels or []:
        if isinstance(item, dict):
            px = fnum(item.get("p") or item.get("price") or item.get("px"))
            sz = fnum(item.get("v") or item.get("q") or item.get("quantity") or item.get("size") or item.get("sz"))
        else:
            px = fnum(item[0] if len(item) > 0 else None)
            sz = fnum(item[1] if len(item) > 1 else None)
        if px is None or sz is None or px <= 0 or sz <= 0:
            continue
        out.append((px, sz * size_mult))
    out.sort(key=lambda x: x[0], reverse=reverse)
    return out[:5]


class HourlyParquet:
    def __init__(self):
        self.key = ""
        self.writer = None
        self.path = None
        self.tmp_path = None
        self.jsonl = None
        self.jsonl_path = None
        self.jsonl_tmp_path = None

    def unique_final_path(self, final_path: Path) -> Path:
        if not final_path.exists():
            return final_path
        stem = final_path.with_suffix("")
        suffix = final_path.suffix
        n = 2
        while True:
            candidate = final_path.with_name(f"{stem.name}.part{n}{suffix}")
            if not candidate.exists():
                return candidate
            n += 1

    def write(self, rows):
        if not rows:
            return
        now = utcnow()
        key = hour_key(now)
        if key != self.key:
            self.close()
            folder = ROOT / now.strftime("%Y") / now.strftime("%m") / now.strftime("%d")
            folder.mkdir(parents=True, exist_ok=True)
            self.path = folder / f"arbitrage_orderbook_{key}.parquet"
            self.tmp_path = folder / f"arbitrage_orderbook_{key}.parquet.inprogress"
            self.jsonl_path = folder / f"arbitrage_orderbook_{key}.jsonl"
            self.jsonl_tmp_path = folder / f"arbitrage_orderbook_{key}.jsonl.inprogress"
            if self.tmp_path.exists():
                self.tmp_path.unlink()
            if self.jsonl_tmp_path.exists():
                self.jsonl_tmp_path.unlink()
            self.writer = pq.ParquetWriter(self.tmp_path, SCHEMA, compression="zstd")
            self.jsonl = self.jsonl_tmp_path.open("a", encoding="utf-8", buffering=1)
            self.key = key
        table = pa.Table.from_pylist(rows, schema=SCHEMA)
        self.writer.write_table(table)
        for row in rows:
            self.jsonl.write(json.dumps(row, separators=(",", ":")) + "\n")
        self.jsonl.flush()

    def close(self):
        if self.jsonl:
            self.jsonl.close()
        if self.writer:
            self.writer.close()
            final_path = self.unique_final_path(self.path) if self.path else None
            if self.tmp_path and self.tmp_path.exists() and final_path:
                self.tmp_path.replace(final_path)
        if self.jsonl_tmp_path and self.jsonl_tmp_path.exists():
            self.jsonl_tmp_path.unlink()
        self.writer = None
        self.path = None
        self.tmp_path = None
        self.jsonl = None
        self.jsonl_path = None
        self.jsonl_tmp_path = None
        self.key = ""


class Collector:
    def __init__(self):
        self.stop = asyncio.Event()
        self.books = {}
        self.common = {}
        self.mexc_contract_size = {}
        self.bidask_filter = {}
        self.funding = {}
        self.alert_state = {}
        self.symbol_to_coin = {}
        self.live_clients = set()
        self.telegram_config = self.load_telegram_config()
        self.telegram_missing_logged = False
        self.writer = HourlyParquet()

    def filter_average(self, values):
        vals = [float(v) for v in values if v is not None]
        return sum(vals) / len(vals) if vals else None

    def filter_entry(self, exchange, symbol, side):
        key = f"{exchange}:{symbol}:{side}"
        if key not in self.bidask_filter:
            self.bidask_filter[key] = {"values": [], "last_accepted": None}
        return self.bidask_filter[key]

    def filter_push(self, entry, value):
        entry["values"].append(float(value))
        if len(entry["values"]) > BIDASK_FILTER_WINDOW:
            entry["values"] = entry["values"][-BIDASK_FILTER_WINDOW:]

    def filtered_best(self, exchange, symbol, side, raw):
        value = fnum(raw)
        if value is None or value <= 0:
            return None
        entry = self.filter_entry(exchange, symbol, side)
        if len(entry["values"]) < BIDASK_FILTER_WINDOW:
            self.filter_push(entry, value)
            if len(entry["values"]) == BIDASK_FILTER_WINDOW:
                entry["last_accepted"] = value
                return value
            return None
        average = self.filter_average(entry["values"])
        self.filter_push(entry, value)
        if average is None:
            entry["last_accepted"] = value
            return value
        max_dev = max(abs(average) * BIDASK_FILTER_MAX_REL_DEV, 1e-8)
        outlier = value < average - max_dev if side == "bid" else value > average + max_dev
        if outlier:
            return entry["last_accepted"] if entry["last_accepted"] is not None else average
        entry["last_accepted"] = value
        return value

    def load_telegram_config(self):
        config = {}
        if SPREAD_ALERT_ENV.exists():
            for line in SPREAD_ALERT_ENV.read_text().splitlines():
                line = line.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                key, value = line.split("=", 1)
                config[key.strip()] = value.strip().strip("\"'")
        token = os.environ.get("TELEGRAM_BOT_TOKEN") or os.environ.get("OPENCLAW_TELEGRAM_BOT_TOKEN") or config.get("TELEGRAM_BOT_TOKEN") or config.get("OPENCLAW_TELEGRAM_BOT_TOKEN")
        chat_id = os.environ.get("TELEGRAM_CHAT_ID") or os.environ.get("OPENCLAW_TELEGRAM_TARGET") or config.get("TELEGRAM_CHAT_ID") or config.get("OPENCLAW_TELEGRAM_TARGET")
        return {"token": token or "", "chat_id": chat_id or ""}

    def send_telegram(self, text: str):
        token = self.telegram_config.get("token") or ""
        chat_id = self.telegram_config.get("chat_id") or ""
        if not token or not chat_id:
            if not self.telegram_missing_logged:
                print("spread alert telegram config missing; skipping alerts", flush=True)
                self.telegram_missing_logged = True
            return
        data = urllib.parse.urlencode({"chat_id": chat_id, "text": text}).encode("utf-8")
        req = urllib.request.Request(
            f"https://api.telegram.org/bot{token}/sendMessage",
            data=data,
            headers={"Content-Type": "application/x-www-form-urlencoded"},
            method="POST",
        )
        with urllib.request.urlopen(req, timeout=10) as resp:
            resp.read()

    def load_universe(self):
        binance = get_json("https://fapi.binance.com/fapi/v1/exchangeInfo")
        b = {}
        for row in binance.get("symbols", []):
            if row.get("contractType") != "PERPETUAL" or row.get("quoteAsset") != "USDT" or row.get("status") != "TRADING":
                continue
            base = str(row.get("baseAsset") or "").upper()
            symbol = str(row.get("symbol") or "").upper()
            if base and symbol:
                b[base] = symbol

        mexc = get_json("https://contract.mexc.com/api/v1/contract/detail")
        m = {}
        for row in mexc.get("data", []) or []:
            symbol = str(row.get("symbol") or "").upper()
            base = str(row.get("baseCoin") or "").upper()
            quote = str(row.get("quoteCoin") or "").upper()
            if quote != "USDT" or not symbol or not base:
                continue
            m[base] = symbol
            self.mexc_contract_size[symbol] = float(row.get("contractSize") or 1)

        coins = sorted(set(b).intersection(m))
        self.common = {coin: {"binance": b[coin], "mexc": m[coin]} for coin in coins}
        self.symbol_to_coin = {}
        for coin, symbols in self.common.items():
            for exchange, symbol in symbols.items():
                self.symbol_to_coin[(exchange, symbol)] = coin
        print(f"universe common={len(self.common)}", flush=True)

    def refresh_funding(self):
        next_funding = dict(self.funding)
        try:
            data = get_json("https://fapi.binance.com/fapi/v1/premiumIndex")
            for row in data if isinstance(data, list) else []:
                symbol = str(row.get("symbol") or "").upper()
                rate = fnum(row.get("lastFundingRate"))
                if symbol and rate is not None:
                    next_funding[("binance", symbol)] = rate
        except Exception as exc:
            print(f"binance funding refresh failed: {exc}", flush=True)

        try:
            data = get_json("https://contract.mexc.com/api/v1/contract/ticker")
            for row in data.get("data", []) if isinstance(data, dict) else []:
                symbol = str(row.get("symbol") or "").upper()
                rate = fnum(row.get("fundingRate"))
                if symbol and rate is not None:
                    next_funding[("mexc", symbol)] = rate
        except Exception as exc:
            print(f"mexc funding refresh failed: {exc}", flush=True)

        self.funding = next_funding

    async def binance_socket(self, symbols):
        streams = "/".join(s.lower() + "@depth5" for s in symbols)
        url = "wss://fstream.binance.com/stream?streams=" + streams
        while not self.stop.is_set():
            try:
                async with websockets.connect(url, ping_interval=20, ping_timeout=20, close_timeout=5, max_queue=2048) as ws:
                    async for raw in ws:
                        msg = json.loads(raw)
                        data = msg.get("data") or msg
                        symbol = str(data.get("s") or "").upper()
                        if not symbol:
                            continue
                        asks = clean_levels(data.get("a"), reverse=False)
                        bids = clean_levels(data.get("b"), reverse=True)
                        self.books[("binance", symbol)] = {"asks": asks, "bids": bids}
                        await self.broadcast_live_symbol("binance", symbol)
            except Exception as exc:
                print(f"binance ws reconnect: {exc}", flush=True)
                await asyncio.sleep(3)

    async def mexc_socket(self, symbols):
        while not self.stop.is_set():
            try:
                async with websockets.connect("wss://contract.mexc.com/edge", ping_interval=None, close_timeout=5, max_queue=2048) as ws:
                    for symbol in symbols:
                        await ws.send(json.dumps({"method": "sub.depth", "param": {"symbol": symbol, "limit": 5}}))
                    ping_task = asyncio.create_task(self.mexc_ping(ws))
                    try:
                        async for raw in ws:
                            msg = json.loads(raw)
                            if not isinstance(msg, dict):
                                continue
                            data = msg.get("data") or msg.get("d")
                            if data is not None and not isinstance(data, dict):
                                continue
                            if not data or "depth" not in str(msg.get("channel") or msg.get("c") or "").lower():
                                continue
                            symbol = str(data.get("symbol") or msg.get("symbol") or "").upper()
                            if not symbol:
                                continue
                            mult = self.mexc_contract_size.get(symbol, 1.0)
                            asks = clean_levels(data.get("asks") or data.get("a"), size_mult=mult, reverse=False)
                            bids = clean_levels(data.get("bids") or data.get("b"), size_mult=mult, reverse=True)
                            prev = self.books.get(("mexc", symbol), {"asks": [], "bids": []})
                            self.books[("mexc", symbol)] = {"asks": asks or prev["asks"], "bids": bids or prev["bids"]}
                            await self.broadcast_live_symbol("mexc", symbol)
                    finally:
                        ping_task.cancel()
            except Exception as exc:
                print(f"mexc ws reconnect: {exc}", flush=True)
                await asyncio.sleep(3)

    async def mexc_ping(self, ws):
        while True:
            await asyncio.sleep(15)
            with contextlib.suppress(Exception):
                await ws.send(json.dumps({"method": "ping"}))

    def row_for(self, ts, coin, exchange, symbol, book, use_filter=True):
        bids = list(book.get("bids") or [])[:5]
        asks = list(book.get("asks") or [])[:5]
        if not bids or not asks:
            return None
        if use_filter:
            best_bid = self.filtered_best(exchange, symbol, "bid", bids[0][0])
            best_ask = self.filtered_best(exchange, symbol, "ask", asks[0][0])
        else:
            best_bid = fnum(bids[0][0])
            best_ask = fnum(asks[0][0])
        if best_bid is None or best_ask is None:
            return None
        bids[0] = (best_bid, bids[0][1])
        asks[0] = (best_ask, asks[0][1])
        row = {
            "ts_ms": int(ts.timestamp() * 1000),
            "ts_utc": ts.isoformat(timespec="milliseconds").replace("+00:00", "Z"),
            "coin": coin,
            "exchange": EXCHANGE_MARKET_ID.get(exchange, exchange),
            "symbol": symbol,
            "bid_ask_median": (best_bid + best_ask) / 2,
        }
        funding_rate = self.funding.get((exchange, symbol))
        row["funding_rate"] = funding_rate
        row["funding_pct"] = funding_rate * 100 if funding_rate is not None else None
        for i in range(5):
            bp, bs = bids[i] if i < len(bids) else (None, None)
            ap, az = asks[i] if i < len(asks) else (None, None)
            row[f"bid_px_{i+1}"] = bp
            row[f"bid_sz_{i+1}"] = bs
            row[f"ask_px_{i+1}"] = ap
            row[f"ask_sz_{i+1}"] = az
        return row

    def send_spread_alerts(self, rows, ts):
        if not SPREAD_ALERTS_ENABLED:
            return
        by_coin = {}
        for row in rows or []:
            exchange = str(row.get("exchange") or "").upper()
            if exchange == "BINANCE_F_PERP":
                exchange = "binance"
            elif exchange == "MEXC_F_PERP":
                exchange = "mexc"
            by_coin.setdefault(row.get("coin"), {})[exchange] = row
        now_s = ts.timestamp()
        alerts = []
        for coin, legs in by_coin.items():
            b = legs.get("binance")
            m = legs.get("mexc")
            if not coin or not b or not m:
                continue
            b_mid = fnum(b.get("bid_ask_median"))
            m_mid = fnum(m.get("bid_ask_median"))
            if b_mid is None or m_mid is None or m_mid == 0:
                continue
            diff = ((b_mid - m_mid) / m_mid) * 100
            state = self.alert_state.setdefault(coin, {"active": False, "last_sent": 0.0})
            if abs(diff) < SPREAD_ALERT_RECOVERY_PCT:
                state["active"] = False
                continue
            if abs(diff) < SPREAD_ALERT_THRESHOLD_PCT:
                continue
            if state["active"] and now_s - float(state.get("last_sent") or 0) < SPREAD_ALERT_COOLDOWN_SECONDS:
                continue
            state["active"] = True
            state["last_sent"] = now_s
            alerts.append((abs(diff), coin, diff, b, m, b_mid, m_mid))
        if not alerts:
            return
        alerts.sort(reverse=True, key=lambda x: x[0])
        lines = [
            "VAN v2 arbitrage spread alert",
            f"Threshold: > {SPREAD_ALERT_THRESHOLD_PCT:.2f}%",
            ts.isoformat(timespec="seconds").replace("+00:00", "Z"),
            "",
        ]
        for _, coin, diff, b, m, b_mid, m_mid in alerts[:20]:
            lines.append(
                f"{coin}: {diff:+.4f}% | Binance {b.get('symbol')} {b_mid:.8g} | MEXC {m.get('symbol')} {m_mid:.8g}"
            )
        if len(alerts) > 20:
            lines.append(f"... +{len(alerts) - 20} more")
        lines.append("https://v2.alexvysotsky.com/arbitrage/pair")
        try:
            self.send_telegram("\n".join(lines))
            print(f"spread alerts sent count={len(alerts)}", flush=True)
        except Exception as exc:
            print(f"spread alert telegram failed: {exc}", flush=True)

    def write_live_latest(self, rows, ts):
        LIVE_LATEST_PATH.parent.mkdir(parents=True, exist_ok=True)
        tmp_path = LIVE_LATEST_PATH.with_suffix(".json.tmp")
        payload = {
            "source": "backend-ws",
            "ts_ms": int(ts.timestamp() * 1000),
            "ts_utc": ts.isoformat(timespec="milliseconds").replace("+00:00", "Z"),
            "rows": rows or [],
        }
        tmp_path.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8")
        tmp_path.replace(LIVE_LATEST_PATH)

    def normalize_client_exchange(self, value):
        exchange = str(value or "").strip().upper().replace("_", "-")
        if exchange in {"BINANCE", "BINANCE-F-PERP", "BINANCE-FUTURES"}:
            return "binance"
        if exchange in {"MEXC", "MEXC-F-PERP", "MEXC-FUTURES"}:
            return "mexc"
        return ""

    def book_from_row(self, row):
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
        return {"bids": bids, "asks": asks}

    def live_pair_row(self, coin, leg1, leg2):
        coin = str(coin or "").upper()
        symbols = self.common.get(coin)
        if not symbols or leg1 not in symbols or leg2 not in symbols or leg1 == leg2:
            return None
        ts = utcnow()
        rows = {}
        for exchange in (leg1, leg2):
            symbol = symbols.get(exchange)
            book = self.books.get((exchange, symbol))
            if not book:
                return None
            row = self.row_for(ts, coin, exchange, symbol, book, use_filter=False)
            if not row:
                return None
            rows[exchange] = row
        mid1 = rows[leg1].get("bid_ask_median")
        mid2 = rows[leg2].get("bid_ask_median")
        if mid1 is None or mid2 is None or mid2 == 0:
            return None
        return {
            "ts_ms": rows[leg1].get("ts_ms"),
            "ts_utc": rows[leg1].get("ts_utc"),
            "coin": coin,
            "mid1": mid1,
            "mid2": mid2,
            "leg1_exchange": leg1,
            "leg2_exchange": leg2,
            "leg1_symbol": rows[leg1].get("symbol"),
            "leg2_symbol": rows[leg2].get("symbol"),
            "leg1_book": self.book_from_row(rows[leg1]),
            "leg2_book": self.book_from_row(rows[leg2]),
            "fund1": rows[leg1].get("funding_pct"),
            "fund2": rows[leg2].get("funding_pct"),
            "diff": ((mid1 - mid2) / mid2) * 100,
        }

    async def live_ws_handler(self, websocket):
        path = ""
        request = getattr(websocket, "request", None)
        if request is not None:
            path = getattr(request, "path", "") or ""
        if not path:
            path = getattr(websocket, "path", "") or ""
        parsed = urllib.parse.urlsplit(path)
        qs = urllib.parse.parse_qs(parsed.query)
        client = {
            "ws": websocket,
            "coin": "".join(ch for ch in (qs.get("coin", [""])[0] or "").upper() if ch.isalnum()),
            "leg1": self.normalize_client_exchange(qs.get("leg1", [""])[0]),
            "leg2": self.normalize_client_exchange(qs.get("leg2", [""])[0]),
        }
        if not client["coin"] or client["leg1"] not in {"binance", "mexc"} or client["leg2"] not in {"binance", "mexc"} or client["leg1"] == client["leg2"]:
            await websocket.close(code=1008, reason="invalid arbitrage live subscription")
            return
        self.live_clients.add(tuple(client.items()))
        client_key = tuple(client.items())
        try:
            row = self.live_pair_row(client["coin"], client["leg1"], client["leg2"])
            if row:
                await websocket.send(json.dumps({"source": "backend-ws-stream", "row": row}, separators=(",", ":")))
            await websocket.wait_closed()
        finally:
            self.live_clients.discard(client_key)

    async def broadcast_live_symbol(self, exchange, symbol):
        coin = self.symbol_to_coin.get((exchange, symbol))
        if not coin or not self.live_clients:
            return
        dead = []
        for client_key in list(self.live_clients):
            client = dict(client_key)
            ws = client.get("ws")
            if client.get("coin") != coin or exchange not in {client.get("leg1"), client.get("leg2")}:
                continue
            row = self.live_pair_row(coin, client.get("leg1"), client.get("leg2"))
            if not row:
                continue
            try:
                await ws.send(json.dumps({"source": "backend-ws-stream", "row": row}, separators=(",", ":")))
            except Exception:
                dead.append(client_key)
        for client_key in dead:
            self.live_clients.discard(client_key)

    async def live_loop(self):
        while not self.stop.is_set():
            ts = utcnow()
            rows = []
            for coin, symbols in self.common.items():
                for exchange, symbol in symbols.items():
                    book = self.books.get((exchange, symbol))
                    if not book:
                        continue
                    row = self.row_for(ts, coin, exchange, symbol, book, use_filter=False)
                    if row:
                        rows.append(row)
            await asyncio.to_thread(self.write_live_latest, rows, ts)
            await asyncio.sleep(LIVE_SECONDS)

    async def snapshot_loop(self):
        while not self.stop.is_set():
            ts = utcnow()
            await asyncio.to_thread(self.refresh_funding)
            rows = []
            for coin, symbols in self.common.items():
                for exchange, symbol in symbols.items():
                    book = self.books.get((exchange, symbol))
                    if not book:
                        continue
                    row = self.row_for(ts, coin, exchange, symbol, book)
                    if row:
                        rows.append(row)
            await asyncio.to_thread(self.send_spread_alerts, rows, ts)
            self.writer.write(rows)
            print(f"snapshot rows={len(rows)} hour={self.writer.key}", flush=True)
            await asyncio.sleep(SNAPSHOT_SECONDS)

    async def run(self):
        self.load_universe()
        ws_server = await websockets.serve(self.live_ws_handler, LIVE_WS_HOST, LIVE_WS_PORT, ping_interval=20, ping_timeout=20)
        print(f"live ws listening on {LIVE_WS_HOST}:{LIVE_WS_PORT}", flush=True)
        tasks = [asyncio.create_task(self.live_loop()), asyncio.create_task(self.snapshot_loop())]
        b_symbols = [v["binance"] for v in self.common.values()]
        m_symbols = [v["mexc"] for v in self.common.values()]
        for i in range(0, len(b_symbols), BINANCE_STREAM_CHUNK):
            tasks.append(asyncio.create_task(self.binance_socket(b_symbols[i:i + BINANCE_STREAM_CHUNK])))
        for i in range(0, len(m_symbols), MEXC_SUB_CHUNK):
            tasks.append(asyncio.create_task(self.mexc_socket(m_symbols[i:i + MEXC_SUB_CHUNK])))
        await self.stop.wait()
        ws_server.close()
        await ws_server.wait_closed()
        for task in tasks:
            task.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)
        self.writer.close()


async def main():
    collector = Collector()
    loop = asyncio.get_running_loop()
    for sig in (signal.SIGINT, signal.SIGTERM):
        loop.add_signal_handler(sig, collector.stop.set)
    await collector.run()


if __name__ == "__main__":
    asyncio.run(main())
