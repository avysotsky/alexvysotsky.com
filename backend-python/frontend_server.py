#!/usr/bin/env python3
from __future__ import annotations

import argparse
import base64
import datetime
import hashlib
import hmac
import http.client
import json
import mimetypes
import os
import posixpath
import subprocess
import time
import urllib.parse
import urllib.request
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
from pathlib import Path

from cryptography.hazmat.backends import default_backend
from cryptography.hazmat.primitives import padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

DIST = Path("/home/user/VAN-v2/webclient/dist")
BACKEND_HOST = "127.0.0.1"
BACKEND_PORT = 38131
FUNDING_API_PORT = 38132
HISTORY_QUERY = "/home/user/VAN-v2/scripts/arbitrage_history_query.py"
SNAPSHOT_QUERY = "/home/user/VAN-v2/scripts/arbitrage_snapshot_query.py"
HISTORY_PYTHON = "/home/user/VAN-v2/collector-venv/bin/python"
ARBITRAGE_LIVE_LATEST = Path("/home/user/VAN-v2/data/arbitrage_orderbook_live_latest.json")
CONFIG_JSON = Path("/home/user/VAN-v2/runtime/app/config.json")
ENV_FILE = Path("/home/user/VAN-v2/config/van-web-service.env")

PROXY_PREFIXES = (
    "/api/",
    "/public/",
    "/getListOfAccounts",
    "/mutations",
)

EXTENSIONLESS = {
    "/": "index.html",
    "/app": "app.html",
    "/trading": "trading.html",
    "/market-data": "market-data.html",
    "/market-data/spreads": "market-data.html",
    "/market-data/spreads/": "market-data.html",
    "/market-data/funding": "market-data.html",
    "/market-data/funding/": "market-data.html",
    "/market-data/options": "market-data.html",
    "/market-data/options/": "market-data.html",
    "/okx": "okx.html",
    "/bybit": "bybit.html",
    "/kraken": "kraken.html",
    "/kucoin": "kucoin.html",
    "/coincall": "coincall.html",
    "/deribit": "deribit.html",
    "/deribit/trading": "deribit.html",
    "/deribit/trading/": "deribit.html",
    "/deribit/trading/spot": "deribit.html",
    "/deribit/trading/spot/": "deribit.html",
    "/deribit/trading/futures": "deribit.html",
    "/deribit/trading/futures/": "deribit.html",
    "/deribit/trading/options": "deribit.html",
    "/deribit/trading/options/": "deribit.html",
    "/deribit/trading/data": "deribit.html",
    "/deribit/trading/data/": "deribit.html",
    "/deribit/trading/spreads": "deribit.html",
    "/deribit/trading/spreads/": "deribit.html",
    "/deribit/trading/info": "deribit.html",
    "/deribit/trading/info/": "deribit.html",
    "/mexc": "mexc.html",
    "/binance": "binance.html",
    "/hyperliquid": "hyperliquid.html",
    "/bitget": "bitget.html",
    "/gate": "gate.html",
    "/coinbase": "coinbase.html",
    "/arbitrage": "arbitrage.html",
    "/arbitrage/tokens": "arbitrage.html",
    "/arbitrage/pair": "arbitrage.html",
    "/arbitrage/snapshot": "arbitrage-snapshot.html",
    "/arbitrage/funding": "arbitrage.html",
    "/arbitrage/tab2": "arbitrage.html",
    "/position-builder": "position-builder.html",
    "/backtester": "backtester.html",
    "/visitors": "visitors.html",
}

class BackendHttpError(Exception):
    def __init__(self, status: int):
        super().__init__(f"backend returned HTTP {status}")
        self.status = status

class Handler(BaseHTTPRequestHandler):
    server_version = "van-v2-frontend/1.0"

    def log_message(self, fmt, *args):
        print("%s - - [%s] %s" % (self.client_address[0], self.log_date_time_string(), fmt % args), flush=True)

    def do_GET(self): self.route()
    def do_HEAD(self): self.route(head=True)
    def do_POST(self): self.route()
    def do_PUT(self): self.route()
    def do_DELETE(self): self.route()

    def route(self, head: bool = False):
        parsed = urllib.parse.urlsplit(self.path)
        if parsed.path == "/api/admin/mexc/spot/private-ws-auth":
            self.mexc_private_ws_auth(parsed, "spot", head=head)
            return
        if parsed.path == "/api/admin/mexc/futures/private-ws-auth":
            self.mexc_private_ws_auth(parsed, "futures", head=head)
            return
        if parsed.path.startswith("/mexc-equity"):
            self.mexc_equity(parsed, head=head)
            return
        if parsed.path == "/api/admin/mexc/equity":
            self.mexc_equity(parsed, head=head)
            return
        if parsed.path == "/api/admin/binance/pnl":
            self.binance_pnl(parsed, head=head)
            return
        if parsed.path == "/exchange-public/orderbook":
            self.exchange_public_proxy(parsed, "orderbook", head=head)
            return
        if parsed.path == "/exchange-public/ticker":
            self.exchange_public_proxy(parsed, "ticker", head=head)
            return
        if parsed.path == "/exchange-public/funding":
            self.exchange_public_proxy(parsed, "funding", head=head)
            return
        if parsed.path == "/exchange-public/ws-token":
            self.exchange_public_proxy(parsed, "ws-token", head=head)
            return
        if parsed.path == "/exchange-public/contract-detail":
            self.exchange_public_proxy(parsed, "contract-detail", head=head)
            return
        if parsed.path == "/arbitrage-history/spread":
            self.arbitrage_history_spread(parsed, head=head)
            return
        if parsed.path == "/arbitrage-live/latest":
            self.arbitrage_live_latest(parsed, head=head)
            return
        if parsed.path == "/arbitrage-history/latest":
            self.arbitrage_history_latest(parsed, head=head)
            return
        if parsed.path == "/arbitrage-history/snapshot":
            self.arbitrage_history_snapshot(parsed, head=head)
            return
        if parsed.path == "/api/admin/deribit/futures/position-funding":
            self.proxy_to_port(FUNDING_API_PORT)
            return
        if parsed.path.startswith(PROXY_PREFIXES):
            self.proxy()
            return
        self.static(parsed.path, head=head)

    def arbitrage_history_spread(self, parsed, head: bool = False):
        qs = urllib.parse.parse_qs(parsed.query)
        coin = "".join(ch for ch in (qs.get("coin", [""])[0] or "").upper() if ch.isalnum())
        leg1 = "".join(ch for ch in (qs.get("leg1", [""])[0] or "").lower() if ch.isalnum() or ch in "-_")
        leg2 = "".join(ch for ch in (qs.get("leg2", [""])[0] or "").lower() if ch.isalnum() or ch in "-_")
        limit_raw = qs.get("limit", ["720"])[0] or "720"
        try:
            limit = max(1, min(int(limit_raw), 5000))
        except Exception:
            limit = 720
        if not coin:
            self.send_error(400, "coin required")
            return
        try:
            data = b"" if head else subprocess.check_output(
                [HISTORY_PYTHON, HISTORY_QUERY, "--coin", coin, "--limit", str(limit), "--leg1", leg1, "--leg2", leg2],
                timeout=20,
            )
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.end_headers()
            if not head:
                self.wfile.write(data)
        except Exception as exc:
            self.send_error(502, str(exc))

    def normalize_arbitrage_exchange(self, value: str) -> str:
        exchange = str(value or "").strip().upper().replace("_", "-")
        if exchange in {"BINANCE", "BINANCE-F-PERP", "BINANCE-FUTURES"}:
            return "binance"
        if exchange in {"MEXC", "MEXC-F-PERP", "MEXC-FUTURES"}:
            return "mexc"
        return ""

    def arbitrage_book_from_row(self, row):
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

    def arbitrage_live_rows(self, leg1: str, leg2: str, limit: int, coin: str):
        if leg1 not in {"binance", "mexc"} or leg2 not in {"binance", "mexc"} or leg1 == leg2:
            return {"source": "backend-ws", "leg1": leg1, "leg2": leg2, "rows": []}
        payload = json.loads(ARBITRAGE_LIVE_LATEST.read_text(encoding="utf-8"))
        by_coin = {}
        for row in payload.get("rows", []) or []:
            row_coin = str(row.get("coin") or "").upper()
            if coin and row_coin != coin:
                continue
            exchange = self.normalize_arbitrage_exchange(row.get("exchange"))
            if not row_coin or exchange not in {"binance", "mexc"}:
                continue
            mid = row.get("bid_ask_median")
            if mid is None:
                continue
            item = by_coin.setdefault(row_coin, {"ts_ms": row.get("ts_ms"), "ts_utc": row.get("ts_utc"), "coin": row_coin})
            item[exchange] = float(mid)
            item[exchange + "_symbol"] = row.get("symbol")
            item[exchange + "_book"] = self.arbitrage_book_from_row(row)
            funding = row.get("funding_pct")
            if funding is not None:
                item[exchange + "_funding"] = funding
        rows = []
        for item in by_coin.values():
            mid1 = item.get(leg1)
            mid2 = item.get(leg2)
            if mid1 is None or mid2 is None or mid2 == 0:
                continue
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
            rows.append(item)
        rows.sort(key=lambda x: abs(float(x.get("diff") or 0)), reverse=True)
        return {"source": "backend-ws", "leg1": leg1, "leg2": leg2, "rows": rows[:limit]}

    def arbitrage_live_latest(self, parsed, head: bool = False):
        qs = urllib.parse.parse_qs(parsed.query)
        leg1 = self.normalize_arbitrage_exchange(qs.get("leg1", [""])[0])
        leg2 = self.normalize_arbitrage_exchange(qs.get("leg2", [""])[0])
        limit_raw = qs.get("limit", ["20"])[0] or "20"
        coin = "".join(ch for ch in (qs.get("coin", [""])[0] or "").upper() if ch.isalnum())
        try:
            limit = max(1, min(int(limit_raw), 200))
        except Exception:
            limit = 20
        try:
            data = b"" if head else json.dumps(self.arbitrage_live_rows(leg1, leg2, limit, coin), separators=(",", ":")).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.end_headers()
            if not head:
                self.wfile.write(data)
        except FileNotFoundError:
            self.send_error(503, "backend live cache not ready")
        except Exception as exc:
            self.send_error(502, str(exc))

    def arbitrage_history_latest(self, parsed, head: bool = False):
        qs = urllib.parse.parse_qs(parsed.query)
        leg1 = "".join(ch for ch in (qs.get("leg1", [""])[0] or "").lower() if ch.isalnum() or ch in "-_")
        leg2 = "".join(ch for ch in (qs.get("leg2", [""])[0] or "").lower() if ch.isalnum() or ch in "-_")
        limit_raw = qs.get("limit", ["20"])[0] or "20"
        coin = "".join(ch for ch in (qs.get("coin", [""])[0] or "").upper() if ch.isalnum())
        try:
            limit = max(1, min(int(limit_raw), 200))
        except Exception:
            limit = 20
        cmd = [HISTORY_PYTHON, HISTORY_QUERY, "--latest", "--limit", str(limit), "--leg1", leg1, "--leg2", leg2]
        if coin:
            cmd.extend(["--coin", coin])
        try:
            data = b"" if head else subprocess.check_output(
                cmd,
                timeout=20,
            )
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.end_headers()
            if not head:
                self.wfile.write(data)
        except Exception as exc:
            self.send_error(502, str(exc))

    def arbitrage_history_snapshot(self, parsed, head: bool = False):
        qs = urllib.parse.parse_qs(parsed.query)
        ts = (qs.get("ts", [""])[0] or "").strip()
        tolerance_raw = qs.get("tolerance_ms", ["6000"])[0] or "6000"
        try:
            tolerance_ms = max(0, min(int(tolerance_raw), 300000))
        except Exception:
            tolerance_ms = 6000
        if not ts:
            self.send_error(400, "ts required")
            return
        try:
            data = b"" if head else subprocess.check_output(
                [HISTORY_PYTHON, SNAPSHOT_QUERY, "--ts", ts, "--tolerance-ms", str(tolerance_ms)],
                timeout=25,
            )
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.end_headers()
            if not head:
                self.wfile.write(data)
        except Exception as exc:
            self.send_error(502, str(exc))

    def exchange_public_proxy(self, parsed, data_type: str, head: bool = False):
        qs = urllib.parse.parse_qs(parsed.query)
        exchange = (qs.get("exchange", [""])[0] or "").lower()
        market = (qs.get("market", [""])[0] or "").lower()
        symbol = qs.get("symbol", [""])[0] or ""
        try:
            req = self.exchange_public_request(data_type, exchange, market, symbol)
        except ValueError as exc:
            self.send_error(400, str(exc))
            return
        try:
            data = b"" if head else self.fetch_external_json(req)
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.end_headers()
            if not head:
                self.wfile.write(data)
        except Exception as exc:
            self.send_error(502, str(exc))

    def exchange_public_request(self, data_type: str, exchange: str, market: str, symbol: str):
        if data_type == "ticker":
            return self.exchange_ticker_request(exchange, market, symbol)
        if data_type == "funding":
            return self.exchange_funding_request(exchange, market, symbol)
        if data_type == "ws-token":
            return self.exchange_ws_token_request(exchange)
        if data_type == "contract-detail":
            return self.exchange_contract_detail_request(exchange, market, symbol)
        if data_type != "orderbook":
            raise ValueError("unsupported exchange public request")
        return self.exchange_orderbook_request(exchange, market, symbol)

    def mexc_private_ws_auth(self, parsed, market: str, head: bool = False):
        data = b"" if head else json.dumps({
            "ok": False,
            "message": "MEXC private WebSocket auth is disabled in browser clients. Private exchange state is backend-owned."
        }, separators=(",", ":")).encode()
        self.send_response(410)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        self.end_headers()
        if not head:
            self.wfile.write(data)

    def mexc_equity(self, parsed, head: bool = False):
        if self.command not in {"GET", "HEAD"}:
            self.send_error(405)
            return
        qs = urllib.parse.parse_qs(parsed.query)
        account_id = "".join(ch for ch in (qs.get("accountId", [""])[0] or "") if ch.isdigit())
        try:
            if not self.backend_admin_authorized():
                raise BackendHttpError(401)
            acc_id = int(account_id) if account_id else int(self.mexc_active_account_id())
            totals = self.load_mexc_latest_equity_totals(acc_id)
            payload = self.load_mexc_equity_payload(acc_id, totals)
            data = b"" if head else json.dumps(payload, separators=(",", ":")).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.end_headers()
            if not head:
                self.wfile.write(data)
        except BackendHttpError as exc:
            self.send_response(exc.status)
            self.send_header("Content-Length", "0")
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.end_headers()
        except Exception as exc:
            self.send_error(502, str(exc))

    def binance_pnl(self, parsed, head: bool = False):
        if self.command not in {"GET", "HEAD"}:
            self.send_error(405)
            return
        qs = urllib.parse.parse_qs(parsed.query)
        account_id = "".join(ch for ch in (qs.get("accountId", [""])[0] or "") if ch.isdigit())
        try:
            self.backend_json("/api/admin/binance/accounts")
            acc_id = int(account_id) if account_id else int(self.binance_active_account_id())
            totals = self.load_exchange_latest_equity_totals("binance", acc_id)
            payload = self.load_exchange_equity_payload("binance", acc_id, totals)
            data = b"" if head else json.dumps(payload, separators=(",", ":")).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.end_headers()
            if not head:
                self.wfile.write(data)
        except BackendHttpError as exc:
            self.send_response(exc.status)
            self.send_header("Content-Length", "0")
            self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
            self.end_headers()
        except Exception as exc:
            self.send_error(502, str(exc))

    def backend_json(self, path: str):
        conn = http.client.HTTPConnection(BACKEND_HOST, BACKEND_PORT, timeout=30)
        headers = {k: v for k, v in self.headers.items() if k.lower() not in {"host", "connection", "content-length"}}
        headers["Host"] = f"{BACKEND_HOST}:{BACKEND_PORT}"
        try:
            conn.request("GET", path, headers=headers)
            resp = conn.getresponse()
            data = resp.read()
            if not (200 <= resp.status < 300):
                raise BackendHttpError(resp.status)
            return json.loads(data.decode() or "{}")
        finally:
            conn.close()

    def mexc_response_account(self, value):
        if not isinstance(value, dict):
            return None
        if isinstance(value.get("account"), dict):
            return value["account"]
        data = value.get("data")
        if isinstance(data, dict) and isinstance(data.get("account"), dict):
            return data["account"]
        return None

    def mexc_active_account_id(self):
        out = self.psql_text("select id::text from public.van_mexc_account where is_active=true order by id limit 1")
        return (out.strip() or "1")

    def binance_active_account_id(self):
        out = self.psql_text("select id::text from public.van_binance_account where is_active=true order by id limit 1")
        return (out.strip() or "1")

    def mexc_equity_totals(self, raw):
        rows = self.mexc_balance_rows(raw)
        total = 0.0
        total_ok = False
        available = 0.0
        available_ok = False
        for row in rows:
            ccy = str(row.get("ccy") or "").upper()
            bal = self.mexc_float(row.get("balance"))
            avail = self.mexc_float(row.get("available"))
            if bal is None and avail is not None:
                frozen = self.mexc_float(row.get("frozen")) or 0.0
                bal = avail + frozen
            price = self.mexc_usdt_price(ccy)
            if bal is not None and price is not None:
                total += bal * price
                total_ok = True
            if avail is not None and price is not None:
                available += avail * price
                available_ok = True
        return {
            "total": total if total_ok else None,
            "available": available if available_ok else None,
            "currency": "USD",
            "rows": rows,
        }

    def mexc_balance_rows(self, raw):
        rows = []
        seen = set()
        for obj in self.walk_json_objects(raw):
            ccy = self.find_ci(obj, ["ccy", "currency", "coin", "asset", "basecurrency", "symbol", "margincoin"])
            balance = self.find_ci(obj, ["equityamount", "marginbalance", "accountequity", "equity", "eq", "totalbalance", "walletbalance", "cashbalanceamount", "cashbalance", "cashbal", "total", "balance", "bal"])
            avail = self.find_ci(obj, ["available", "avail", "availablebalance", "availablebal", "availbalance", "availeq", "availableequity", "free"])
            frozen = self.find_ci(obj, ["frozen", "locked", "hold", "freeze", "usedmargin"])
            upl = self.find_ci(obj, ["upl", "unrealizedpnl", "unrealisedpnl", "unrealisedprofit", "unrealizedprofit"])
            if ccy is None or (balance is None and avail is None and frozen is None and upl is None):
                continue
            key = (str(ccy).upper(), str(balance), str(avail), str(frozen))
            if key in seen:
                continue
            seen.add(key)
            rows.append({"ccy": str(ccy).upper(), "balance": balance, "available": avail, "frozen": frozen})
        return rows

    def walk_json_objects(self, value):
        if isinstance(value, dict):
            yield value
            for child in value.values():
                yield from self.walk_json_objects(child)
        elif isinstance(value, list):
            for child in value:
                yield from self.walk_json_objects(child)

    def find_ci(self, obj, names):
        want = {str(name).lower() for name in names}
        for key, value in obj.items():
            if str(key).lower() in want and value not in (None, ""):
                return value
        return None

    def mexc_float(self, value):
        try:
            n = float(str(value).replace(",", ""))
            if n == n and n not in (float("inf"), float("-inf")):
                return n
        except Exception:
            return None
        return None

    def mexc_usdt_price(self, ccy: str):
        c = str(ccy or "").upper()
        if c in {"USD", "USDT", "USDC"}:
            return 1.0
        if not c or c.endswith("USDT") or len(c) > 12:
            return None
        cache = getattr(self.server, "mexc_price_cache", None)
        if cache is None:
            cache = {}
            self.server.mexc_price_cache = cache
        now = time.time()
        cached = cache.get(c)
        if cached and now - cached[0] < 30:
            return cached[1]
        try:
            url = "https://api.mexc.com/api/v3/ticker/price?symbol=" + urllib.parse.quote(c + "USDT")
            raw = urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": "VAN-v2-mexc-equity/1.0"}), timeout=8).read()
            price = self.mexc_float(json.loads(raw.decode()).get("price"))
            if price is not None and price > 0:
                cache[c] = (now, price)
                return price
        except Exception:
            return None
        return None

    def ensure_mexc_equity_tables(self):
        self.psql_text("""
create table if not exists public.van_mexc_equity_minute (
  account_id integer not null,
  minute_utc timestamp with time zone not null,
  equity_currency text not null default 'USD',
  total_equity numeric,
  available_equity numeric,
  created_at timestamp with time zone not null default now(),
  updated_at timestamp with time zone not null default now(),
  primary key (account_id, minute_utc)
);
create table if not exists public.van_mexc_equity_daily (
  account_id integer not null,
  day_utc date not null,
  last_ts_utc timestamp with time zone not null,
  equity_currency text not null default 'USD',
  total_equity numeric,
  available_equity numeric,
  created_at timestamp with time zone not null default now(),
  updated_at timestamp with time zone not null default now(),
  primary key (account_id, day_utc)
);
""")

    def store_mexc_equity(self, account_id: int, totals: dict):
        total = self.sql_num(totals.get("total"))
        available = self.sql_num(totals.get("available"))
        self.psql_text(f"""
insert into public.van_mexc_equity_minute(account_id, minute_utc, equity_currency, total_equity, available_equity)
values ({int(account_id)}, date_trunc('minute', now() at time zone 'utc') at time zone 'utc', 'USD', {total}, {available})
on conflict (account_id, minute_utc) do update set
  total_equity=excluded.total_equity,
  available_equity=excluded.available_equity,
  updated_at=now();
insert into public.van_mexc_equity_daily(account_id, day_utc, last_ts_utc, equity_currency, total_equity, available_equity)
values ({int(account_id)}, (now() at time zone 'utc')::date, now() at time zone 'utc', 'USD', {total}, {available})
on conflict (account_id, day_utc) do update set
  last_ts_utc=excluded.last_ts_utc,
  total_equity=excluded.total_equity,
  available_equity=excluded.available_equity,
  updated_at=now();
""")

    def load_mexc_latest_equity_totals(self, account_id: int):
        sql = f"""
select json_build_object(
  'total', total_equity,
  'available', available_equity,
  'currency', equity_currency
)::text
from public.van_mexc_equity_minute
where account_id={int(account_id)} and total_equity is not null
order by minute_utc desc
limit 1
"""
        out = self.psql_text(sql).strip()
        if not out:
            return {"total": None, "available": None, "currency": "USD"}
        return json.loads(out)

    def load_exchange_latest_equity_totals(self, exchange: str, account_id: int):
        if exchange not in {"binance"}:
            raise ValueError("unsupported exchange")
        table = f"public.van_{exchange}_equity_minute"
        sql = f"""
select json_build_object(
  'total', total_equity,
  'available', available_equity,
  'currency', equity_currency
)::text
from {table}
where account_id={int(account_id)} and total_equity is not null
order by minute_utc desc
limit 1
"""
        out = self.psql_text(sql).strip()
        if not out:
            return {"total": None, "available": None, "currency": "USD"}
        return json.loads(out)

    def load_exchange_equity_payload(self, exchange: str, account_id: int, totals: dict):
        if exchange not in {"binance"}:
            raise ValueError("unsupported exchange")
        minute_table = f"public.van_{exchange}_equity_minute"
        daily_table = f"public.van_{exchange}_equity_daily"
        source_name = exchange.upper() + " backend equity"
        source_sql = "'" + source_name.replace("'", "''") + "'"
        sql = f"""
with minute_rows as (
  select minute_utc, total_equity
  from {minute_table}
  where account_id={int(account_id)} and total_equity is not null
  order by minute_utc desc
  limit 1440
),
daily_rows as (
  select day_utc, last_ts_utc, total_equity
  from {daily_table}
  where account_id={int(account_id)} and total_equity is not null
  order by day_utc desc
  limit 730
)
select json_build_object(
  'metrics', json_build_object(
    'equityCurrency','USD',
    'lastSnapshotUtc', coalesce((select max(minute_utc) from minute_rows), now() at time zone 'utc'),
    'storeMinuteEquity', true,
    'source', {source_sql},
    'totalEquity', {self.sql_num(totals.get("total"))},
    'availableEquity', {self.sql_num(totals.get("available"))}
  ),
  'daily', json_build_object(
    'points', coalesce((select json_agg(json_build_object('dt', day_utc::text || 'T00:00:00Z', 'v', total_equity) order by day_utc) from daily_rows), '[]'::json),
    'drawdownPoints', '[]'::json
  ),
  'minute', json_build_object(
    'points', coalesce((select json_agg(json_build_object('dt', to_char(minute_utc at time zone 'utc', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'), 'v', total_equity) order by minute_utc) from minute_rows), '[]'::json),
    'drawdownPoints', '[]'::json
  )
)::text
"""
        out = self.psql_text(sql).strip()
        return json.loads(out or "{}")

    def load_mexc_equity_payload(self, account_id: int, totals: dict):
        sql = f"""
with minute_rows as (
  select minute_utc, total_equity
  from public.van_mexc_equity_minute
  where account_id={int(account_id)} and total_equity is not null
  order by minute_utc desc
  limit 1440
),
daily_rows as (
  select day_utc, last_ts_utc, total_equity
  from public.van_mexc_equity_daily
  where account_id={int(account_id)} and total_equity is not null
  order by day_utc desc
  limit 730
)
select json_build_object(
  'metrics', json_build_object(
    'equityCurrency','USD',
    'lastSnapshotUtc', coalesce((select max(minute_utc) from minute_rows), now() at time zone 'utc'),
    'storeMinuteEquity', true,
    'source', 'MEXC backend equity',
    'totalEquity', {self.sql_num(totals.get("total"))},
    'availableEquity', {self.sql_num(totals.get("available"))}
  ),
  'daily', json_build_object(
    'points', coalesce((select json_agg(json_build_object('dt', day_utc::text || 'T00:00:00Z', 'v', total_equity) order by day_utc) from daily_rows), '[]'::json),
    'drawdownPoints', '[]'::json
  ),
  'minute', json_build_object(
    'points', coalesce((select json_agg(json_build_object('dt', to_char(minute_utc at time zone 'utc', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'), 'v', total_equity) order by minute_utc) from minute_rows), '[]'::json),
    'drawdownPoints', '[]'::json
  )
)::text
"""
        out = self.psql_text(sql).strip()
        return json.loads(out or "{}")

    def sql_num(self, value):
        n = self.mexc_float(value)
        return "null" if n is None else repr(float(n))

    def psql_text(self, sql: str):
        config = json.loads(CONFIG_JSON.read_text())
        conn = self.parse_connection_string(config["AuthorityConnectionString"])
        env = os.environ.copy()
        env["PGPASSWORD"] = conn["Password"]
        return subprocess.check_output(
            ["psql", "-h", conn.get("Host", "127.0.0.1"), "-p", conn.get("Port", "5432"), "-U", conn["Username"], "-d", conn["Database"], "-Atc", sql],
            env=env,
            text=True,
            timeout=15,
        )

    def backend_admin_authorized(self) -> bool:
        conn = http.client.HTTPConnection(BACKEND_HOST, BACKEND_PORT, timeout=15)
        headers = {k: v for k, v in self.headers.items() if k.lower() not in {"host", "connection", "content-length"}}
        headers["Host"] = f"{BACKEND_HOST}:{BACKEND_PORT}"
        try:
            conn.request("GET", "/api/admin/mexc/accounts", headers=headers)
            resp = conn.getresponse()
            resp.read()
            return 200 <= resp.status < 300
        finally:
            conn.close()

    def mexc_private_ws_payload(self, market: str, account: dict):
        api_key = account["api_key"]
        api_secret = account["api_secret"]
        if market == "spot":
            ts = str(int(time.time() * 1000))
            query = "timestamp=" + ts
            sig = hmac.new(api_secret.encode(), query.encode(), hashlib.sha256).hexdigest()
            req = urllib.request.Request(
                "https://api.mexc.com/api/v3/userDataStream?" + query + "&signature=" + sig,
                method="POST",
                headers={"X-MEXC-APIKEY": api_key, "Content-Length": "0"},
            )
            raw = urllib.request.urlopen(req, timeout=10).read()
            listen_key = json.loads(raw.decode()).get("listenKey", "")
            if not listen_key:
                raise RuntimeError("MEXC spot listenKey missing")
            return {
                "market": "spot",
                "accountId": account["id"],
                "url": "wss://wbs-api.mexc.com/ws?listenKey=" + urllib.parse.quote(listen_key),
                "listenKey": listen_key,
                "expiresInSec": 1800,
            }
        if market == "futures":
            req_time = str(int(time.time() * 1000))
            sig = hmac.new(api_secret.encode(), (api_key + req_time).encode(), hashlib.sha256).hexdigest()
            return {
                "market": "futures",
                "accountId": account["id"],
                "url": "wss://contract.mexc.com/edge",
                "loginPayload": {"method": "login", "param": {"apiKey": api_key, "reqTime": req_time, "signature": sig}},
                "subscriptions": [
                    {"method": "sub.personal.order"},
                    {"method": "sub.personal.deals"},
                    {"method": "sub.personal.position"},
                ],
            }
        raise ValueError("unsupported MEXC private WS market")

    def mexc_account_credentials(self, parsed):
        qs = urllib.parse.parse_qs(parsed.query)
        account_id = "".join(ch for ch in (qs.get("accountId", [""])[0] or "") if ch.isdigit())
        config = json.loads(CONFIG_JSON.read_text())
        conn = self.parse_connection_string(config["AuthorityConnectionString"])
        sql = "select id,name,api_key_cipher,api_secret_cipher from public.van_mexc_account "
        if account_id:
            sql += "where id = " + account_id + " limit 1"
        else:
            sql += "where is_active = true order by id limit 1"
        env = os.environ.copy()
        env["PGPASSWORD"] = conn["Password"]
        out = subprocess.check_output(
            ["psql", "-h", conn.get("Host", "127.0.0.1"), "-p", conn.get("Port", "5432"), "-U", conn["Username"], "-d", conn["Database"], "-Atc", sql],
            env=env,
            text=True,
            timeout=10,
        ).strip()
        if not out:
            raise RuntimeError("MEXC account not found")
        row = out.split("|", 3)
        if len(row) != 4:
            raise RuntimeError("MEXC account row malformed")
        return {"id": int(row[0]), "name": row[1], "api_key": self.decrypt_secret(row[2]), "api_secret": self.decrypt_secret(row[3])}

    def parse_connection_string(self, value: str):
        return dict(part.split("=", 1) for part in value.split(";") if "=" in part)

    def encryption_key_material(self):
        for line in ENV_FILE.read_text().splitlines():
            if line.startswith("VAN_API_SECRET_ENCRYPTION_KEY="):
                return line.split("=", 1)[1].strip()
        return os.environ.get("VAN_API_SECRET_ENCRYPTION_KEY", "CHANGE_ME_TO_LONG_RANDOM_SECRET_KEY_32+CHARS")

    def decrypt_secret(self, cipher_text: str) -> str:
        raw = base64.b64decode(cipher_text)
        iv, ciphertext = raw[:16], raw[16:]
        key = hashlib.sha256(self.encryption_key_material().encode()).digest()
        decryptor = Cipher(algorithms.AES(key), modes.CBC(iv), backend=default_backend()).decryptor()
        padded = decryptor.update(ciphertext) + decryptor.finalize()
        unpadder = padding.PKCS7(128).unpadder()
        return (unpadder.update(padded) + unpadder.finalize()).decode().lstrip("\ufeff")

    def exchange_ws_token_request(self, exchange: str):
        if exchange == "kucoin":
            return {"url": "https://api.kucoin.com/api/v1/bullet-public", "method": "POST"}
        raise ValueError("unsupported exchange ws token")

    def exchange_contract_detail_request(self, exchange: str, market: str, symbol: str):
        clean = "".join(ch for ch in symbol.upper() if ch.isalnum() or ch in "-_")
        if exchange == "mexc" and market == "futures" and clean:
            return {"url": f"https://contract.mexc.com/api/v1/contract/detail?symbol={urllib.parse.quote(clean)}"}
        raise ValueError("unsupported exchange contract detail")

    def exchange_ticker_request(self, exchange: str, market: str, symbol: str):
        if exchange == "binance" and market == "futures":
            suffix = f"?symbol={urllib.parse.quote(symbol.upper())}" if symbol else ""
            return {"url": f"https://fapi.binance.com/fapi/v1/ticker/24hr{suffix}"}
        if exchange == "mexc" and market == "futures":
            clean = "".join(ch for ch in symbol.upper() if ch.isalnum() or ch in "-_")
            suffix = f"?symbol={urllib.parse.quote(clean)}" if clean else ""
            return {"url": f"https://contract.mexc.com/api/v1/contract/ticker{suffix}"}
        raise ValueError("unsupported exchange ticker")

    def exchange_funding_request(self, exchange: str, market: str, symbol: str):
        if exchange == "binance" and market == "futures":
            suffix = f"?symbol={urllib.parse.quote(symbol.upper())}" if symbol else ""
            return {"url": f"https://fapi.binance.com/fapi/v1/premiumIndex{suffix}"}
        if exchange == "mexc" and market == "futures":
            clean = "".join(ch for ch in symbol.upper() if ch.isalnum() or ch in "-_")
            suffix = f"?symbol={urllib.parse.quote(clean)}" if clean else ""
            return {"url": f"https://contract.mexc.com/api/v1/contract/ticker{suffix}"}
        if exchange == "bybit" and market in ("futures", "linear", "inverse"):
            clean = "".join(ch for ch in symbol.upper() if ch.isalnum())
            suffix = f"&symbol={urllib.parse.quote(clean)}" if clean else ""
            category = "inverse" if market == "inverse" else "linear"
            return {"url": f"https://api.bybit.com/v5/market/tickers?category={category}{suffix}"}
        if exchange == "okx" and market in ("futures", "swap"):
            clean = "".join(ch for ch in symbol.upper() if ch.isalnum() or ch in "-_")
            if not clean:
                raise ValueError("okx funding symbol required")
            clean = clean.replace("_", "-")
            if clean.endswith("-PERP"):
                clean = clean[:-5] + "-SWAP"
            elif not clean.endswith("-SWAP"):
                clean = clean + "-SWAP"
            return {"url": f"https://www.okx.com/api/v5/public/funding-rate?instId={urllib.parse.quote(clean)}"}
        if exchange == "bitget" and market in ("futures", "linear"):
            clean = "".join(ch for ch in symbol.upper() if ch.isalnum())
            suffix = f"&symbol={urllib.parse.quote(clean)}" if clean else ""
            return {"url": f"https://api.bitget.com/api/v2/mix/market/current-fund-rate?productType=USDT-FUTURES{suffix}"}
        if exchange == "gate" and market == "futures":
            clean = "".join(ch for ch in symbol.upper() if ch.isalnum() or ch in "-_")
            if clean:
                clean = clean.replace("-", "_").replace("_PERP", "")
                return {"url": f"https://api.gateio.ws/api/v4/futures/usdt/contracts/{urllib.parse.quote(clean)}"}
            return {"url": "https://api.gateio.ws/api/v4/futures/usdt/contracts"}
        if exchange == "hyperliquid" and market == "futures":
            return {
                "url": "https://api.hyperliquid.xyz/info",
                "method": "POST",
                "headers": {"Content-Type": "application/json"},
                "body": json.dumps({"type": "predictedFundings"}).encode(),
            }
        if exchange == "deribit" and market == "futures":
            clean = "".join(ch for ch in symbol.upper() if ch.isalnum() or ch in "-_")
            token = clean.split("-")[0].split("_")[0] if clean else ""
            if token not in {"BTC", "ETH"}:
                raise ValueError("deribit funding supports BTC/ETH only")
            return {"url": f"https://www.deribit.com/api/v2/public/get_book_summary_by_currency?currency={urllib.parse.quote(token)}&kind=future"}
        raise ValueError("unsupported exchange funding")

    def exchange_orderbook_request(self, exchange: str, market: str, symbol: str):
        clean = "".join(ch for ch in symbol.upper() if ch.isalnum() or ch in "-_")
        base = clean.replace("-PERP", "").replace("_PERP", "")
        compact = base.replace("-", "").replace("_", "")
        token = compact
        for quote in ("USDT", "USD", "USDC"):
            if token.endswith(quote):
                token = token[:-len(quote)]
                break
        if not token:
            raise ValueError("symbol required")
        dash = f"{token}-USDT"
        underscore = f"{token}_USDT"
        compact_usdt = f"{token}USDT"
        if exchange == "bitget" and market == "spot":
            return {"url": f"https://api.bitget.com/api/v2/spot/market/orderbook?symbol={urllib.parse.quote(compact_usdt)}&type=step0&limit=10"}
        if exchange == "bitget" and market == "futures":
            return {"url": f"https://api.bitget.com/api/v2/mix/market/merge-depth?symbol={urllib.parse.quote(compact_usdt)}&productType=USDT-FUTURES&precision=scale0&limit=10"}
        if exchange == "kraken" and market == "spot":
            pair = ("XBT" if token == "BTC" else token) + "USDT"
            return {"url": f"https://api.kraken.com/0/public/Depth?pair={urllib.parse.quote(pair)}&count=10"}
        if exchange == "kucoin" and market == "spot":
            return {"url": f"https://api.kucoin.com/api/v1/market/orderbook/level2_20?symbol={urllib.parse.quote(dash)}"}
        if exchange == "gate" and market == "spot":
            return {"url": f"https://api.gateio.ws/api/v4/spot/order_book?currency_pair={urllib.parse.quote(underscore)}&limit=10"}
        if exchange == "gate" and market == "futures":
            return {"url": f"https://api.gateio.ws/api/v4/futures/usdt/order_book?contract={urllib.parse.quote(underscore)}&limit=10"}
        if exchange == "coinbase" and market == "spot":
            return {"url": f"https://api.exchange.coinbase.com/products/{urllib.parse.quote(dash)}/book?level=2"}
        if exchange == "hyperliquid" and market == "futures":
            return {
                "url": "https://api.hyperliquid.xyz/info",
                "method": "POST",
                "headers": {"Content-Type": "application/json"},
                "body": json.dumps({"type": "l2Book", "coin": token}).encode(),
            }
        raise ValueError("unsupported exchange orderbook")

    def fetch_external_json(self, req):
        headers = {"User-Agent": "VAN-v2-public-orderbook-proxy/1.0"}
        headers.update(req.get("headers") or {})
        request = urllib.request.Request(req["url"], data=req.get("body"), method=req.get("method", "GET"), headers=headers)
        with urllib.request.urlopen(request, timeout=12) as resp:
            return resp.read()

    def static(self, path: str, head: bool = False):
        rel = EXTENSIONLESS.get(path)
        if rel is None:
            clean = posixpath.normpath(urllib.parse.unquote(path)).lstrip("/")
            if clean == ".":
                clean = "index.html"
            candidate = DIST / clean
            if candidate.is_dir():
                candidate = candidate / "index.html"
            elif not candidate.exists() and "." not in Path(clean).name:
                html_candidate = DIST / (clean + ".html")
                if html_candidate.exists():
                    candidate = html_candidate
            rel_path = candidate
        else:
            rel_path = DIST / rel
        try:
            resolved = rel_path.resolve()
            resolved.relative_to(DIST.resolve())
        except Exception:
            self.send_error(403)
            return
        if not resolved.exists() or not resolved.is_file():
            self.send_error(404)
            return
        ctype = mimetypes.guess_type(str(resolved))[0] or "application/octet-stream"
        data = b"" if head else resolved.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(resolved.stat().st_size if head else len(data)))
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        self.end_headers()
        if not head:
            self.wfile.write(data)

    def proxy(self):
        self.proxy_to_port(BACKEND_PORT)

    def proxy_to_port(self, port: int):
        body_len = int(self.headers.get("Content-Length", "0") or "0")
        body = self.rfile.read(body_len) if body_len else None
        conn = http.client.HTTPConnection(BACKEND_HOST, port, timeout=60)
        headers = {k: v for k, v in self.headers.items() if k.lower() not in {"host", "connection", "content-length"}}
        headers["Host"] = f"{BACKEND_HOST}:{port}"
        if body is not None:
            headers["Content-Length"] = str(len(body))
        try:
            conn.request(self.command, self.path, body=body, headers=headers)
            resp = conn.getresponse()
            data = resp.read()
            self.send_response(resp.status, resp.reason)
            for k, v in resp.getheaders():
                if k.lower() in {"connection", "transfer-encoding", "content-encoding"}:
                    continue
                self.send_header(k, v)
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
        except Exception as exc:
            self.send_error(502, str(exc))
        finally:
            conn.close()

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=18180)
    args = parser.parse_args()
    httpd = ThreadingHTTPServer((args.host, args.port), Handler)
    print(f"VAN v2 frontend listening on {args.host}:{args.port}", flush=True)
    httpd.serve_forever()

if __name__ == "__main__":
    main()
