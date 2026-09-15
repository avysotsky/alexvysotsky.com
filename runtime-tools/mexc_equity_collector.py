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
        "from public.van_mexc_account where is_active=true order by id limit 1"
    ).strip()
    if not out:
        raise RuntimeError("active MEXC account not found")
    row = out.split("|", 3)
    if len(row) != 4:
        raise RuntimeError("active MEXC account row malformed")
    return {
        "id": int(row[0]),
        "name": row[1],
        "api_key": decrypt_secret(row[2]),
        "api_secret": decrypt_secret(row[3]),
    }


def query(values: dict[str, str | None], sort: bool = False) -> str:
    items = [(k, v) for k, v in values.items() if v is not None and str(v) != ""]
    if sort:
        items.sort(key=lambda x: x[0])
    return urllib.parse.urlencode(items)


def hmac_sha256(secret: str, payload: str) -> str:
    return hmac.new(secret.encode(), payload.encode(), hashlib.sha256).hexdigest().lower()


def fetch_json(url: str, headers: dict[str, str] | None = None, method: str = "GET") -> object:
    req = urllib.request.Request(url, headers=headers or {}, method=method)
    with urllib.request.urlopen(req, timeout=20) as resp:
        return json.loads(resp.read().decode() or "{}")


def spot_account(account: dict[str, object]) -> object:
    values = {
        "recvWindow": "5000",
        "timestamp": str(int(time.time() * 1000)),
    }
    unsigned = query(values)
    values["signature"] = hmac_sha256(str(account["api_secret"]), unsigned)
    url = "https://api.mexc.com/api/v3/account?" + query(values)
    return fetch_json(url, {"X-MEXC-APIKEY": str(account["api_key"]), "Accept": "application/json"})


def futures_assets(account: dict[str, object]) -> object:
    values: dict[str, str | None] = {}
    request_time = str(int(time.time() * 1000))
    param_string = query(values, sort=True)
    signature = hmac_sha256(str(account["api_secret"]), str(account["api_key"]) + request_time + param_string)
    url = "https://contract.mexc.com/api/v1/private/account/assets"
    if param_string:
        url += "?" + param_string
    return fetch_json(
        url,
        {
            "ApiKey": str(account["api_key"]),
            "Request-Time": request_time,
            "Signature": signature,
            "Recv-Window": "10000",
            "Accept": "application/json",
        },
    )


def walk_json_objects(value: object):
    if isinstance(value, dict):
        yield value
        for child in value.values():
            yield from walk_json_objects(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk_json_objects(child)


def find_ci(obj: dict[str, object], names: list[str]) -> object | None:
    want = {name.lower() for name in names}
    for key, value in obj.items():
        if key.lower() in want and value not in (None, ""):
            return value
    return None


def as_float(value: object) -> float | None:
    try:
        n = float(str(value).replace(",", ""))
        if n == n and n not in (float("inf"), float("-inf")):
            return n
    except Exception:
        return None
    return None


PRICE_CACHE: dict[str, tuple[float, float]] = {}


def usdt_price(ccy: str) -> float | None:
    c = str(ccy or "").upper()
    if c in {"USD", "USDT", "USDC"}:
        return 1.0
    if not c or c.endswith("USDT") or len(c) > 18:
        return None
    now = time.time()
    cached = PRICE_CACHE.get(c)
    if cached and now - cached[0] < 30:
        return cached[1]
    try:
        url = "https://api.mexc.com/api/v3/ticker/price?symbol=" + urllib.parse.quote(c + "USDT")
        price = as_float(fetch_json(url).get("price"))  # type: ignore[union-attr]
        if price is not None and price > 0:
            PRICE_CACHE[c] = (now, price)
            return price
    except Exception:
        return None
    return None


def balance_rows(raw: object) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    seen: set[tuple[str, str, str, str]] = set()
    for obj in walk_json_objects(raw):
        ccy = find_ci(obj, ["ccy", "currency", "coin", "asset", "basecurrency", "symbol", "margincoin"])
        balance = find_ci(obj, ["equityamount", "marginbalance", "accountequity", "equity", "eq", "totalbalance", "walletbalance", "cashbalanceamount", "cashbalance", "cashbal", "total", "balance", "bal"])
        avail = find_ci(obj, ["available", "avail", "availablebalance", "availablebal", "availbalance", "availeq", "availableequity", "free"])
        frozen = find_ci(obj, ["frozen", "locked", "hold", "freeze", "usedmargin"])
        upl = find_ci(obj, ["upl", "unrealizedpnl", "unrealisedpnl", "unrealisedprofit", "unrealizedprofit"])
        if ccy is None or (balance is None and avail is None and frozen is None and upl is None):
            continue
        key = (str(ccy).upper(), str(balance), str(avail), str(frozen))
        if key in seen:
            continue
        seen.add(key)
        rows.append({"ccy": str(ccy).upper(), "balance": balance, "available": avail, "frozen": frozen})
    return rows


def equity_totals(raw: object) -> dict[str, float | None]:
    total = 0.0
    total_ok = False
    available = 0.0
    available_ok = False
    for row in balance_rows(raw):
        ccy = str(row.get("ccy") or "").upper()
        bal = as_float(row.get("balance"))
        avail = as_float(row.get("available"))
        if bal is None and avail is not None:
            bal = avail + (as_float(row.get("frozen")) or 0.0)
        price = usdt_price(ccy)
        if bal is not None and price is not None:
            total += bal * price
            total_ok = True
        if avail is not None and price is not None:
            available += avail * price
            available_ok = True
    return {
        "total": total if total_ok else None,
        "available": available if available_ok else None,
    }


def sql_num(value: float | None) -> str:
    return "null" if value is None else repr(float(value))


def ensure_tables() -> None:
    psql_text(
        """
set client_min_messages to warning;
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
"""
    )


def store(account_id: int, total: float, available: float | None) -> None:
    psql_text(
        f"""
insert into public.van_mexc_equity_minute(account_id, minute_utc, equity_currency, total_equity, available_equity)
values ({int(account_id)}, date_trunc('minute', now() at time zone 'utc') at time zone 'utc', 'USD', {sql_num(total)}, {sql_num(available)})
on conflict (account_id, minute_utc) do update set
  total_equity=excluded.total_equity,
  available_equity=excluded.available_equity,
  updated_at=now();
insert into public.van_mexc_equity_daily(account_id, day_utc, last_ts_utc, equity_currency, total_equity, available_equity)
values ({int(account_id)}, (now() at time zone 'utc')::date, now() at time zone 'utc', 'USD', {sql_num(total)}, {sql_num(available)})
on conflict (account_id, day_utc) do update set
  last_ts_utc=excluded.last_ts_utc,
  total_equity=excluded.total_equity,
  available_equity=excluded.available_equity,
  updated_at=now();
"""
    )


def main() -> None:
    account = active_account()
    raw = {"spot": spot_account(account)}
    try:
        raw["futures"] = futures_assets(account)
    except Exception as exc:
        print(f"[WARN] futures assets unavailable: {exc}", flush=True)
    totals = equity_totals(raw)
    if totals["total"] is None:
        raise RuntimeError("MEXC equity total unavailable")
    ensure_tables()
    store(int(account["id"]), float(totals["total"]), totals["available"])
    print(
        f"stored account={account['id']} total={float(totals['total']):.8f} "
        f"available={(float(totals['available']) if totals['available'] is not None else float('nan')):.8f}",
        flush=True,
    )


if __name__ == "__main__":
    main()
