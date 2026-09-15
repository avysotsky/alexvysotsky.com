#!/usr/bin/env python3
from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import subprocess
import time
import urllib.parse
import urllib.request
from pathlib import Path

from cryptography.hazmat.backends import default_backend
from cryptography.hazmat.primitives import padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

ROOT = Path("/home/user/VAN-v2")
DB_ENV = ROOT / "config/db.env"
SERVICE_ENV = ROOT / "config/van-web-service.env"


def load_env(path: Path) -> dict[str, str]:
    data: dict[str, str] = {}
    for line in path.read_text().splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        data[key.strip()] = value.strip().strip('"').strip("'")
    return data


DB = load_env(DB_ENV)
SERVICE = load_env(SERVICE_ENV)
PRICE_CACHE: dict[str, tuple[float, float]] = {}


def psql_text(sql: str) -> str:
    env = os.environ.copy()
    env["PGPASSWORD"] = DB["VAN_V2_DB_PASSWORD"]
    return subprocess.check_output(
        [
            "psql",
            "-h",
            "127.0.0.1",
            "-p",
            "5432",
            "-U",
            DB["VAN_V2_DB_USER"],
            "-d",
            DB["VAN_V2_DB"],
            "-Atc",
            sql,
        ],
        env=env,
        text=True,
        timeout=20,
    )


def decrypt_secret(cipher_text: str) -> str:
    raw = base64.b64decode(cipher_text)
    iv, ciphertext = raw[:16], raw[16:]
    key = hashlib.sha256(SERVICE["VAN_API_SECRET_ENCRYPTION_KEY"].encode()).digest()
    decryptor = Cipher(algorithms.AES(key), modes.CBC(iv), backend=default_backend()).decryptor()
    padded = decryptor.update(ciphertext) + decryptor.finalize()
    unpadder = padding.PKCS7(128).unpadder()
    return (unpadder.update(padded) + unpadder.finalize()).decode().lstrip("\ufeff")


def active_account() -> dict[str, object]:
    out = psql_text(
        "select id::text,name,api_key_cipher,api_secret_cipher "
        "from public.van_binance_account where is_active=true order by id limit 1"
    ).strip()
    if not out:
        raise RuntimeError("active Binance account not found")
    row = out.split("|", 3)
    if len(row) != 4:
        raise RuntimeError("active Binance account row malformed")
    return {
        "id": int(row[0]),
        "name": row[1],
        "api_key": decrypt_secret(row[2]),
        "api_secret": decrypt_secret(row[3]),
    }


def as_float(value: object) -> float | None:
    try:
        n = float(str(value).replace(",", ""))
        if n == n and n not in (float("inf"), float("-inf")):
            return n
    except Exception:
        return None
    return None


def query(values: dict[str, str | None]) -> str:
    return urllib.parse.urlencode([(k, v) for k, v in values.items() if v not in (None, "")])


def sign(secret: str, payload: str) -> str:
    return hmac.new(secret.encode(), payload.encode(), hashlib.sha256).hexdigest()


def fetch_json(url: str, headers: dict[str, str] | None = None) -> object:
    req = urllib.request.Request(url, headers=headers or {})
    with urllib.request.urlopen(req, timeout=20) as resp:
        return json.loads(resp.read().decode() or "{}")


def signed_get(base: str, path: str, account: dict[str, object], values: dict[str, str | None] | None = None) -> object:
    params = dict(values or {})
    params["recvWindow"] = params.get("recvWindow") or "5000"
    params["timestamp"] = str(int(time.time() * 1000))
    unsigned = query(params)
    params["signature"] = sign(str(account["api_secret"]), unsigned)
    url = base + path + "?" + query(params)
    return fetch_json(url, {"X-MBX-APIKEY": str(account["api_key"]), "Accept": "application/json"})


def usdt_price(asset: str) -> float | None:
    a = str(asset or "").upper()
    if a in {"USD", "USDT", "USDC"}:
        return 1.0
    if not a or a.endswith("USDT") or len(a) > 18:
        return None
    now = time.time()
    cached = PRICE_CACHE.get(a)
    if cached and now - cached[0] < 30:
        return cached[1]
    try:
        data = fetch_json("https://api.binance.com/api/v3/ticker/price?symbol=" + urllib.parse.quote(a + "USDT"))
        price = as_float(data.get("price")) if isinstance(data, dict) else None
        if price and price > 0:
            PRICE_CACHE[a] = (now, price)
            return price
    except Exception:
        return None
    return None


def spot_totals(account: dict[str, object]) -> tuple[float, float]:
    data = signed_get("https://api.binance.com", "/api/v3/account", account)
    total = 0.0
    available = 0.0
    for row in (data.get("balances", []) if isinstance(data, dict) else []):
        asset = str(row.get("asset") or "").upper()
        free = as_float(row.get("free")) or 0.0
        locked = as_float(row.get("locked")) or 0.0
        qty = free + locked
        if qty == 0 and free == 0:
            continue
        price = usdt_price(asset)
        if price is None:
            continue
        total += qty * price
        available += free * price
    return total, available


def futures_totals(account: dict[str, object]) -> tuple[float, float, float]:
    last_error: Exception | None = None
    for path in ("/fapi/v3/account", "/fapi/v2/account"):
        try:
            data = signed_get("https://fapi.binance.com", path, account, {"recvWindow": "10000"})
            if not isinstance(data, dict):
                continue
            wallet = as_float(data.get("totalWalletBalance")) or 0.0
            unrealized = as_float(data.get("totalUnrealizedProfit")) or 0.0
            available = as_float(data.get("availableBalance")) or 0.0
            return wallet + unrealized, available, unrealized
        except Exception as exc:
            last_error = exc
    if last_error:
        print("futures account unavailable: " + str(last_error), flush=True)
    return 0.0, 0.0, 0.0


def sql_num(value: float | None) -> str:
    return "null" if value is None else repr(float(value))


def ensure_tables() -> None:
    psql_text(
        """
set client_min_messages to warning;
create table if not exists public.van_binance_equity_minute (
  account_id integer not null,
  account_name text not null,
  minute_utc timestamp with time zone not null,
  equity_currency text not null,
  total_equity numeric(38,10) not null,
  available_equity numeric(38,10) not null,
  unrealized_pnl numeric(38,10) not null,
  margin_ratio numeric(38,10),
  created_at timestamp with time zone not null default (now() at time zone 'utc'),
  updated_at timestamp with time zone not null default (now() at time zone 'utc'),
  primary key (account_id, minute_utc)
);
create table if not exists public.van_binance_equity_daily (
  account_id integer not null,
  account_name text not null,
  day_utc date not null,
  last_ts_utc timestamp with time zone not null,
  equity_currency text not null,
  total_equity numeric(38,10) not null,
  available_equity numeric(38,10) not null,
  unrealized_pnl numeric(38,10) not null,
  margin_ratio numeric(38,10),
  created_at timestamp with time zone not null default (now() at time zone 'utc'),
  updated_at timestamp with time zone not null default (now() at time zone 'utc'),
  primary key (account_id, day_utc)
);
"""
    )


def store(account: dict[str, object], total: float, available: float, unrealized: float) -> None:
    account_id = int(account["id"])
    account_name = str(account["name"]).replace("'", "''")
    psql_text(
        f"""
insert into public.van_binance_equity_minute(account_id, account_name, minute_utc, equity_currency, total_equity, available_equity, unrealized_pnl)
values ({account_id}, '{account_name}', date_trunc('minute', now() at time zone 'utc') at time zone 'utc', 'USD', {sql_num(total)}, {sql_num(available)}, {sql_num(unrealized)})
on conflict (account_id, minute_utc) do update set
  account_name=excluded.account_name,
  total_equity=excluded.total_equity,
  available_equity=excluded.available_equity,
  unrealized_pnl=excluded.unrealized_pnl,
  updated_at=now() at time zone 'utc';
insert into public.van_binance_equity_daily(account_id, account_name, day_utc, last_ts_utc, equity_currency, total_equity, available_equity, unrealized_pnl)
values ({account_id}, '{account_name}', (now() at time zone 'utc')::date, now() at time zone 'utc', 'USD', {sql_num(total)}, {sql_num(available)}, {sql_num(unrealized)})
on conflict (account_id, day_utc) do update set
  account_name=excluded.account_name,
  last_ts_utc=excluded.last_ts_utc,
  total_equity=excluded.total_equity,
  available_equity=excluded.available_equity,
  unrealized_pnl=excluded.unrealized_pnl,
  updated_at=now() at time zone 'utc';
update public.van_binance_account set store_minute_equity=true, updated_at=now() at time zone 'utc' where id={account_id};
"""
    )


def main() -> None:
    account = active_account()
    spot_total, spot_available = spot_totals(account)
    futures_total, futures_available, unrealized = futures_totals(account)
    total = spot_total + futures_total
    available = spot_available + futures_available
    ensure_tables()
    store(account, total, available, unrealized)
    print(f"stored account={account['id']} total={total:.8f} available={available:.8f}", flush=True)


if __name__ == "__main__":
    main()
