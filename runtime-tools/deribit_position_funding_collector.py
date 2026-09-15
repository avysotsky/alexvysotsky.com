#!/usr/bin/env python3
from __future__ import annotations

import asyncio
import base64
import hashlib
import json
import os
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import psycopg
import websockets
from cryptography.hazmat.primitives import padding
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

CONFIG_DIR = Path(os.environ.get("VAN_V2_CONFIG_DIR", "/home/user/VAN-v2/config"))
DERIBIT_COLLECTOR_CONFIG = Path(os.environ.get("DERIBIT_COLLECTOR_CONFIG", "/home/user/deribit-md-collector/config/python.settings.json"))
WS_URL = os.environ.get("DERIBIT_WS_URL", "wss://www.deribit.com/ws/api/v2")
INTERVAL_SECONDS = float(os.environ.get("VAN_V2_DERIBIT_POSITION_INTERVAL_SECONDS", "60"))
ASSETS = ("BTC", "ETH")


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def read_env_file(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    try:
        for line in path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            values[key.strip()] = value.strip().strip('"').strip("'")
    except FileNotFoundError:
        pass
    return values


def db_conninfo() -> str:
    env = read_env_file(CONFIG_DIR / "db.env")
    return (
        "host=127.0.0.1 port=5432 "
        f"dbname={env.get('VAN_V2_DB', 'van_v2')} "
        f"user={env.get('VAN_V2_DB_USER', 'van_v2_app')} "
        f"password={env.get('VAN_V2_DB_PASSWORD', '')}"
    )


def secret_key_material() -> str:
    env = read_env_file(CONFIG_DIR / "van-web-service.env")
    return (
        os.environ.get("VAN_API_SECRET_ENCRYPTION_KEY")
        or env.get("VAN_API_SECRET_ENCRYPTION_KEY")
        or "CHANGE_ME_TO_LONG_RANDOM_SECRET_KEY_32+CHARS"
    )


def decrypt_api_secret(cipher_text: str) -> str:
    raw = base64.b64decode(cipher_text)
    iv, payload = raw[:16], raw[16:]
    key = hashlib.sha256(secret_key_material().encode("utf-8")).digest()
    decryptor = Cipher(algorithms.AES(key), modes.CBC(iv)).decryptor()
    padded = decryptor.update(payload) + decryptor.finalize()
    unpadder = padding.PKCS7(128).unpadder()
    return (unpadder.update(padded) + unpadder.finalize()).decode("utf-8")


def num(value: Any) -> float | None:
    if value in (None, ""):
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def div_or_none(numerator: float | None, denominator: float | None, *, percent: bool = False) -> float | None:
    if numerator is None or denominator is None:
        return None
    denominator = abs(denominator)
    if denominator <= 0:
        return None
    value = numerator / denominator
    return value * 100 if percent else value


def ensure_schema(conn: psycopg.Connection) -> None:
    conn.execute(
        """
create table if not exists public.van_deribit_futures_position_funding (
  id bigserial primary key,
  observed_at timestamp with time zone not null,
  account_id integer,
  account_name text,
  instrument_name text not null,
  direction text,
  size numeric,
  size_currency numeric,
  realized_funding numeric,
  realized_profit_loss numeric,
  floating_profit_loss numeric,
  total_profit_loss numeric,
  normalized_by_base_position_size numeric,
  normalized_by_eth_position_size numeric,
  normalized_to_1_usd_position_size numeric,
  mark_price numeric,
  index_price numeric,
  raw_json jsonb not null,
  source text not null default 'deribit_ws_private_get_positions',
  created_at timestamp with time zone not null default now()
);
create index if not exists idx_van_deribit_pos_funding_inst_time
  on public.van_deribit_futures_position_funding (instrument_name, observed_at desc);
alter table public.van_deribit_futures_position_funding
  add column if not exists normalized_by_base_position_size numeric,
  add column if not exists normalized_by_eth_position_size numeric,
  add column if not exists normalized_to_1_usd_position_size numeric;
"""
    )
    conn.commit()


def load_account(conn: psycopg.Connection) -> dict[str, Any]:
    try:
        cfg = json.loads(DERIBIT_COLLECTOR_CONFIG.read_text(encoding="utf-8"))
        deribit = cfg.get("deribit") or {}
        if deribit.get("client_id") and deribit.get("client_secret"):
            return {
                "id": None,
                "name": "deribit-md-collector",
                "client_id": deribit["client_id"],
                "client_secret": deribit["client_secret"],
            }
    except Exception as exc:
        print(f"[deribit-position-funding][collector_config][error] {exc}", flush=True)

    row = conn.execute(
        """
select acc_id, acc_name, api_public_key, api_secret_cipher
from public.van_account
where api_public_key is not null
  and api_secret_cipher is not null
order by acc_id
limit 1;
"""
    ).fetchone()
    if not row:
        raise RuntimeError("No Deribit account with API credentials in van_account")
    return {
        "id": row[0],
        "name": row[1] or f"Account {row[0]}",
        "client_id": row[2],
        "client_secret": decrypt_api_secret(row[3]),
    }


def insert_positions(conn: psycopg.Connection, account: dict[str, Any], positions: list[dict[str, Any]], observed_at: datetime, source: str) -> int:
    rows = []
    for p in positions:
        inst = str(p.get("instrument_name") or "")
        if inst not in {"BTC-PERPETUAL", "ETH-PERPETUAL"}:
            continue
        size = num(p.get("size"))
        size_currency = num(p.get("size_currency"))
        realized_funding = num(p.get("realized_funding"))
        rows.append(
            (
                observed_at,
                account["id"],
                account["name"],
                inst,
                p.get("direction"),
                size,
                size_currency,
                realized_funding,
                num(p.get("realized_profit_loss")),
                num(p.get("floating_profit_loss")),
                num(p.get("total_profit_loss")),
                div_or_none(realized_funding, size_currency, percent=True),
                div_or_none(realized_funding, size_currency, percent=True),
                div_or_none(realized_funding, size),
                num(p.get("mark_price")),
                num(p.get("index_price")),
                json.dumps(p, separators=(",", ":"), sort_keys=True),
                source,
            )
        )
    if not rows:
        return 0
    with conn.cursor() as cur:
        cur.executemany(
            """
insert into public.van_deribit_futures_position_funding (
  observed_at, account_id, account_name, instrument_name, direction,
  size, size_currency, realized_funding, realized_profit_loss,
  floating_profit_loss, total_profit_loss, normalized_by_base_position_size,
  normalized_by_eth_position_size, normalized_to_1_usd_position_size,
  mark_price, index_price,
  raw_json, source
) values (
  %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s::jsonb, %s
);
""",
            rows,
        )
    conn.commit()
    return len(rows)


async def rpc(ws, req_id: int, method: str, params: dict[str, Any]) -> Any:
    await ws.send(json.dumps({"jsonrpc": "2.0", "id": req_id, "method": method, "params": params}))
    while True:
        msg = json.loads(await ws.recv())
        if msg.get("id") != req_id:
            continue
        if msg.get("error"):
            raise RuntimeError(f"Deribit {method} error: {msg['error']}")
        return msg.get("result")


async def collect_loop() -> None:
    with psycopg.connect(db_conninfo()) as conn:
        ensure_schema(conn)
        account = load_account(conn)
        print(f"[deribit-position-funding][account] id={account['id']} name={account['name']}", flush=True)
        req_id = 100
        while True:
            try:
                async with websockets.connect(WS_URL, ping_interval=20, ping_timeout=20, close_timeout=10) as ws:
                    req_id += 1
                    await rpc(
                        ws,
                        req_id,
                        "public/auth",
                        {
                            "grant_type": "client_credentials",
                            "client_id": account["client_id"],
                            "client_secret": account["client_secret"],
                        },
                    )
                    print("[deribit-position-funding][ws] authenticated", flush=True)
                    while True:
                        observed_at = utc_now()
                        total = 0
                        for asset in ASSETS:
                            req_id += 1
                            positions = await rpc(ws, req_id, "private/get_positions", {"currency": asset, "kind": "future"})
                            if isinstance(positions, list):
                                total += insert_positions(conn, account, positions, observed_at, "deribit_ws_private_get_positions")
                        print(f"[deribit-position-funding][ok] rows={total} at={observed_at.isoformat()}", flush=True)
                        await asyncio.sleep(INTERVAL_SECONDS)
            except Exception as exc:
                print(f"[deribit-position-funding][error] {exc}", flush=True)
                await asyncio.sleep(5)


def main() -> None:
    asyncio.run(collect_loop())


if __name__ == "__main__":
    main()
