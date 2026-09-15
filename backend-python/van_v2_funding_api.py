#!/usr/bin/env python3
import json
import os
import re
import subprocess
import threading
import time
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

DATA_DIR = Path(os.environ.get("VAN_V2_FUNDING_DATA_DIR", "/home/user/VAN-v2/data/funding-minute"))
HOST = os.environ.get("VAN_V2_FUNDING_HOST", "0.0.0.0")
PORT = int(os.environ.get("VAN_V2_FUNDING_PORT", "38132"))
INTERVAL_SECONDS = int(os.environ.get("VAN_V2_FUNDING_INTERVAL_SECONDS", "60"))
ASSETS = ("BTC", "ETH")


def psql_command(sql):
    required = ("PGHOST", "PGPORT", "PGDATABASE", "PGUSER", "PGPASSWORD")
    missing = [name for name in required if not os.environ.get(name)]
    if missing:
        raise RuntimeError("Missing required database environment variables")
    return [
        "psql",
        "-h", os.environ["PGHOST"],
        "-p", os.environ["PGPORT"],
        "-d", os.environ["PGDATABASE"],
        "-U", os.environ["PGUSER"],
        "-t", "-A", "-c", sql,
    ]


def utc_now():
    return datetime.now(timezone.utc)


def minute_iso(dt=None):
    dt = dt or utc_now()
    dt = dt.astimezone(timezone.utc).replace(second=0, microsecond=0)
    return dt.isoformat().replace("+00:00", "Z")


def fetch_json(url, *, method="GET", body=None, timeout=12):
    data = None
    headers = {"accept": "application/json", "user-agent": "VAN-v2-funding-collector/1.0"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        headers["content-type"] = "application/json"
    req = urllib.request.Request(url, data=data, method=method, headers=headers)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def as_float(value):
    if value is None:
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def ms_to_iso(value):
    try:
        n = float(value)
    except (TypeError, ValueError):
        return None
    if n <= 0:
        return None
    if n < 10_000_000_000:
        n *= 1000
    return datetime.fromtimestamp(n / 1000, timezone.utc).isoformat().replace("+00:00", "Z")


def write_row(row):
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    exchange = row["exchange"].lower()
    symbol = re.sub(r"[^A-Z0-9_.-]+", "_", row["symbol"].upper())
    path = DATA_DIR / f"{exchange}_{symbol}.ndjson"
    with path.open("a", encoding="utf-8") as fh:
        fh.write(json.dumps(row, separators=(",", ":"), sort_keys=True) + "\n")


def base_row(exchange, symbol, display, source, current, raw, **extra):
    now = utc_now()
    row = {
        "exchange": exchange,
        "symbol": symbol,
        "displayName": display,
        "tsUtc": minute_iso(now),
        "minuteUtc": minute_iso(now),
        "observedMinuteUtc": minute_iso(now),
        "currentFunding": current,
        "current_funding": current,
        "fundingRate": current,
        "fundRate": current,
        "interest8h": extra.pop("interest8h", None),
        "interest_8h": extra.pop("interest_8h", None),
        "source": source,
        "raw": raw,
        "fetchedAtUtc": now.isoformat().replace("+00:00", "Z"),
    }
    row.update(extra)
    return row


def collect_mexc(asset):
    symbol = f"{asset}_USDT"
    obj = fetch_json(f"https://contract.mexc.com/api/v1/contract/funding_rate/{symbol}")
    data = obj.get("data") or {}
    return base_row("MEXC", symbol, f"{asset}USDT Perp", "mexc:/api/v1/contract/funding_rate", as_float(data.get("fundingRate")), obj,
                    nextFundingTimeUtc=ms_to_iso(data.get("nextSettleTime")), markPrice=as_float(data.get("fairPrice")), indexPrice=as_float(data.get("idxPrice")))


def collect_gate(asset):
    symbol = f"{asset}_USDT"
    obj = fetch_json(f"https://api.gateio.ws/api/v4/futures/usdt/contracts/{symbol}")
    return base_row("Gate", symbol, f"{asset}USDT Perp", "gate:/api/v4/futures/usdt/contracts", as_float(obj.get("funding_rate")), obj,
                    interest8h=as_float(obj.get("interest_rate")), interest_8h=as_float(obj.get("interest_rate")),
                    nextFundingTimeUtc=ms_to_iso(obj.get("funding_next_apply")), markPrice=as_float(obj.get("mark_price")), indexPrice=as_float(obj.get("index_price")))


def collect_bitget(asset):
    symbol = f"{asset}USDT"
    obj = fetch_json(f"https://api.bitget.com/api/v2/mix/market/current-fund-rate?symbol={symbol}&productType=USDT-FUTURES")
    rows = obj.get("data") or []
    data = rows[0] if rows else {}
    return base_row("Bitget", symbol, f"{asset}USDT Perp", "bitget:/api/v2/mix/market/current-fund-rate", as_float(data.get("fundingRate")), obj,
                    nextFundingTimeUtc=ms_to_iso(data.get("nextUpdate")))


def collect_kucoin(asset):
    symbol = "XBTUSDTM" if asset == "BTC" else f"{asset}USDTM"
    obj = fetch_json(f"https://api-futures.kucoin.com/api/v1/funding-rate/{symbol}/current")
    data = obj.get("data") or {}
    return base_row("KuCoin", symbol, f"{asset}USDT Perp", "kucoin:/api/v1/funding-rate/current", as_float(data.get("value")), obj,
                    interest8h=as_float(data.get("dailyInterestRate")), interest_8h=as_float(data.get("dailyInterestRate")),
                    fundingTimeUtc=ms_to_iso(data.get("timePoint")), nextFundingTimeUtc=ms_to_iso(data.get("fundingTime")))


def collect_coinbase(asset):
    symbol = f"{asset}-PERP"
    obj = fetch_json(f"https://api.international.coinbase.com/api/v1/instruments/{symbol}/quote")
    return base_row("Coinbase", symbol, f"{asset}-PERP", "coinbase:/api/v1/instruments/quote", as_float(obj.get("predicted_funding")), obj,
                    markPrice=as_float(obj.get("mark_price")), indexPrice=as_float(obj.get("index_price")))


def collect_hyperliquid_all():
    obj = fetch_json("https://api.hyperliquid.xyz/info", method="POST", body={"type": "metaAndAssetCtxs"})
    universe, ctxs = obj[0].get("universe", []), obj[1]
    out = []
    for asset in ASSETS:
        idx = next((i for i, u in enumerate(universe) if u.get("name") == asset), None)
        if idx is None:
            continue
        ctx = ctxs[idx]
        out.append(base_row("Hyperliquid", asset, f"{asset} Perp", "hyperliquid:info/metaAndAssetCtxs", as_float(ctx.get("funding")), ctx,
                            markPrice=as_float(ctx.get("markPx")), indexPrice=as_float(ctx.get("oraclePx"))))
    return out


def collect_kraken_all():
    obj = fetch_json("https://futures.kraken.com/derivatives/api/v3/tickers")
    wanted = {"BTC": "PF_XBTUSD", "ETH": "PF_ETHUSD"}
    tickers = obj.get("tickers") or []
    by_symbol = {t.get("symbol"): t for t in tickers}
    out = []
    for asset, symbol in wanted.items():
        data = by_symbol.get(symbol)
        if not data:
            continue
        current = normalize_kraken_funding(as_float(data.get("fundingRatePrediction") or data.get("fundingRate")))
        settled = normalize_kraken_funding(as_float(data.get("fundingRate")))
        out.append(base_row("Kraken", symbol, f"{asset}USD Perp", "kraken-futures:/derivatives/api/v3/tickers", current, data,
                            interest8h=settled, interest_8h=settled,
                            markPrice=as_float(data.get("markPrice")), indexPrice=as_float(data.get("indexPrice"))))
    return out


def normalize_kraken_funding(value):
    if value is None:
        return None
    # Kraken Futures PF_* tickers can return funding in bps-like units
    # (for example -0.190 ~= -0.00190%). The chart expects decimal fractions.
    return value / 10000 if abs(value) > 1e-6 else value


COLLECTORS = [collect_mexc, collect_gate, collect_bitget, collect_kucoin, collect_coinbase]


def collect_once():
    rows = []
    for asset in ASSETS:
        for fn in COLLECTORS:
            try:
                rows.append(fn(asset))
            except Exception as exc:
                print(f"[funding][{fn.__name__}][{asset}][error] {exc}", flush=True)
    for fn in (collect_hyperliquid_all, collect_kraken_all):
        try:
            rows.extend(fn())
        except Exception as exc:
            print(f"[funding][{fn.__name__}][error] {exc}", flush=True)
    for row in rows:
        if row.get("currentFunding") is not None:
            write_row(row)
    print(f"[funding][ok] rows={len(rows)} at={utc_now().isoformat()}", flush=True)


def collector_loop():
    while True:
        start = time.time()
        collect_once()
        delay = max(5, INTERVAL_SECONDS - (time.time() - start))
        time.sleep(delay)


ALIASES = {
    "mexc": {"BTC": "BTC_USDT", "ETH": "ETH_USDT", "BTCUSDT": "BTC_USDT", "ETHUSDT": "ETH_USDT"},
    "gate": {"BTC": "BTC_USDT", "ETH": "ETH_USDT", "BTCUSDT": "BTC_USDT", "ETHUSDT": "ETH_USDT"},
    "bitget": {"BTC": "BTCUSDT", "ETH": "ETHUSDT"},
    "kucoin": {"BTC": "XBTUSDTM", "ETH": "ETHUSDTM", "BTCUSDT": "XBTUSDTM"},
    "coinbase": {"BTC": "BTC-PERP", "ETH": "ETH-PERP"},
    "hyperliquid": {"BTC": "BTC", "ETH": "ETH"},
    "kraken": {"BTC": "PF_XBTUSD", "ETH": "PF_ETHUSD", "BTCUSD": "PF_XBTUSD", "ETHUSD": "PF_ETHUSD"},
}


def normalize_symbol(exchange, symbol):
    raw = (symbol or "BTC").strip().upper()
    raw = raw.replace("/", "").replace("-", "" if exchange not in {"coinbase"} else "-")
    aliases = ALIASES.get(exchange, {})
    return aliases.get(raw, raw)



def read_coincall_history_rows(symbol, limit):
    symbol = (symbol or "BTCUSD").strip().upper()
    if not re.fullmatch(r"[A-Z0-9_.-]{1,40}", symbol):
        raise ValueError("invalid symbol")
    limit = max(1, min(5000, int(limit)))
    sql = f"""
select coalesce(json_agg(row_to_json(t)), '[]'::json)
from (
  select symbol,
         display_name as "displayName",
         record_key as "recordKey",
         record_id as "recordId",
         observed_minute_utc as "observedMinuteUtc",
         record_time_utc as "recordTimeUtc",
         (extract(epoch from record_time_utc) * 1000)::bigint as ctime,
         trade_side as "tradeSide",
         qty,
         fund_fee as "fundFee",
         fund_rate as "fundRate",
         fund_rate as "currentFunding",
         fund_rate * 100 as current_funding,
         null::numeric as "interest8h",
         null::numeric as interest_8h,
         raw_json as raw
  from public.van_coincall_futures_funding_history
  where symbol = '{symbol}'
  order by coalesce(record_time_utc, observed_minute_utc) desc, updated_at desc
  limit {limit}
) t;
"""
    env = os.environ.copy()
    proc = subprocess.run(
        psql_command(sql),
        check=True,
        capture_output=True,
        text=True,
        env=env,
        timeout=8,
    )
    body = proc.stdout.strip() or "[]"
    rows = json.loads(body)
    return rows if isinstance(rows, list) else []




def read_rows(exchange, symbol, limit):
    path = DATA_DIR / f"{exchange}_{re.sub(r'[^A-Z0-9_.-]+', '_', symbol.upper())}.ndjson"
    if not path.exists():
        return []
    rows_by_minute = {}
    with path.open("r", encoding="utf-8") as fh:
        for line in fh:
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue
            key = row.get("minuteUtc") or row.get("tsUtc")
            if key:
                rows_by_minute[key] = row
    rows = [rows_by_minute[k] for k in sorted(rows_by_minute)]
    return rows[-limit:]


def read_deribit_position_funding_rows(symbol, limit):
    symbol = (symbol or "BTC-PERPETUAL").strip().upper()
    if symbol in {"BTC", "BTCPERPETUAL"}:
        symbol = "BTC-PERPETUAL"
    if symbol in {"ETH", "ETHPERPETUAL"}:
        symbol = "ETH-PERPETUAL"
    if not re.fullmatch(r"(BTC|ETH)-PERPETUAL", symbol):
        raise ValueError("invalid Deribit perpetual symbol")
    limit = max(1, min(86400, int(limit)))
    sql = f"""
select coalesce(json_agg(row_to_json(t)), '[]'::json)
from (
  select observed_at as "observedAtUtc",
         instrument_name as "symbol",
         instrument_name as "displayName",
         (extract(epoch from observed_at) * 1000)::bigint as time,
         (extract(epoch from observed_at) * 1000)::bigint as ctime,
         direction,
         size,
         size_currency as "sizeCurrency",
         size_currency,
         coalesce(size_currency, size) as amount,
         coalesce(size_currency, size) as qty,
         realized_funding as "realizedFunding",
         realized_funding,
         realized_profit_loss as "realizedProfitLoss",
         realized_profit_loss,
         floating_profit_loss as "floatingProfitLoss",
         floating_profit_loss,
         total_profit_loss as "totalProfitLoss",
         total_profit_loss,
         coalesce(normalized_by_base_position_size, normalized_by_eth_position_size, realized_funding / nullif(abs(size_currency), 0) * 100) as "normalizedByBasePositionSize",
         coalesce(normalized_by_base_position_size, normalized_by_eth_position_size, realized_funding / nullif(abs(size_currency), 0) * 100) as normalized_by_base_position_size,
         coalesce(normalized_by_eth_position_size, normalized_by_base_position_size, realized_funding / nullif(abs(size_currency), 0) * 100) as "normalizedByEthPositionSize",
         coalesce(normalized_by_eth_position_size, normalized_by_base_position_size, realized_funding / nullif(abs(size_currency), 0) * 100) as normalized_by_eth_position_size,
         coalesce(normalized_to_1_usd_position_size, realized_funding / nullif(abs(size), 0)) as "normalizedTo1UsdPositionSize",
         coalesce(normalized_to_1_usd_position_size, realized_funding / nullif(abs(size), 0)) as normalized_to_1_usd_position_size,
         mark_price as "markPrice",
         index_price as "indexPrice",
         raw_json as raw,
         source
  from (
    select distinct on (date_trunc('minute', observed_at))
           observed_at, instrument_name, direction, size, size_currency,
           realized_funding, realized_profit_loss, floating_profit_loss, total_profit_loss,
           normalized_by_base_position_size, normalized_by_eth_position_size,
           normalized_to_1_usd_position_size, mark_price, index_price, raw_json, source
    from public.van_deribit_futures_position_funding
    where instrument_name = '{symbol}'
    order by date_trunc('minute', observed_at) desc, observed_at desc
    limit {limit}
  ) m
  order by observed_at desc
) t;
"""
    env = os.environ.copy()
    proc = subprocess.run(
        psql_command(sql),
        check=True,
        capture_output=True,
        text=True,
        env=env,
        timeout=8,
    )
    rows = json.loads(proc.stdout.strip() or "[]")
    if not isinstance(rows, list):
        return []
    rows.reverse()
    return rows


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        return

    def send_json(self, status, payload):
        body = json.dumps(payload, separators=(",", ":"), default=str).encode("utf-8")
        self.send_response(status)
        self.send_header("content-type", "application/json")
        self.send_header("cache-control", "no-store")
        self.send_header("content-length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path == "/api/admin/deribit/futures/position-funding":
            qs = urllib.parse.parse_qs(parsed.query)
            symbol = (qs.get("symbol") or ["BTC-PERPETUAL"])[0]
            try:
                limit = max(1, min(86400, int((qs.get("limit") or ["5000"])[0])))
                rows = read_deribit_position_funding_rows(symbol, limit)
                self.send_json(200, {
                    "source": "van_deribit_futures_position_funding",
                    "symbol": symbol,
                    "order": "oldest-to-newest",
                    "rows": rows,
                    "data": {"list": rows},
                    "fetchedAtUtc": utc_now().isoformat().replace("+00:00", "Z"),
                })
            except Exception as exc:
                self.send_json(500, {"message": str(exc)})
            return
        if parsed.path == "/api/admin/coincall/futures/funding/history/db":
            qs = urllib.parse.parse_qs(parsed.query)
            symbol = normalize_symbol("coincall", (qs.get("symbol") or ["BTCUSD"])[0])
            try:
                limit = max(1, min(5000, int((qs.get("limit") or ["500"])[0])))
                rows = read_coincall_history_rows(symbol, limit)
                self.send_json(200, {
                    "source": "van_v2_sidecar:coincall_funding_history",
                    "symbol": symbol,
                    "rows": rows,
                    "data": {"list": rows},
                    "fetchedAtUtc": utc_now().isoformat().replace("+00:00", "Z"),
                })
            except Exception as exc:
                self.send_json(500, {"message": str(exc)})
            return
        m = re.fullmatch(r"/api/admin/(mexc|gate|bitget|kucoin|coinbase|hyperliquid|kraken)/futures/funding/minute", parsed.path)
        if not m:
            self.send_json(404, {"message": "not found"})
            return
        exchange = m.group(1)
        qs = urllib.parse.parse_qs(parsed.query)
        symbol = normalize_symbol(exchange, (qs.get("symbol") or ["BTC"])[0])
        try:
            limit = max(1, min(129600, int((qs.get("limit") or ["5000"])[0])))
        except ValueError:
            limit = 5000
        rows = read_rows(exchange, symbol, limit)
        self.send_json(200, {
            "source": f"van_v2_funding_archive:{exchange}",
            "symbol": symbol,
            "order": "oldest-to-newest",
            "rows": rows,
            "data": {"list": rows},
            "fetchedAtUtc": utc_now().isoformat().replace("+00:00", "Z"),
        })


def main():
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    collect_once()
    threading.Thread(target=collector_loop, daemon=True).start()
    ThreadingHTTPServer((HOST, PORT), Handler).serve_forever()


if __name__ == "__main__":
    main()
