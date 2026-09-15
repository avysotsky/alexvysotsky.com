#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   repair_account_daily_equity_from_minute.sh [YYYY-MM-DD]
# Default date: today UTC.
#
# Purpose:
# - operational repair for VAN daily-equity rows when Deribit tx-log based USDC
#   settlement selection fails around clearing time;
# - rebuild the missing/today row from the first minute-equity snapshot after
#   Deribit clearing, starting at 08:01 UTC.
#
# The target container and database identity must be supplied by the deployment
# environment. No production identifiers are stored in this repository.

TODAY_UTC="${1:-$(date -u +%F)}"
WINDOW_START="08:01:00"
WINDOW_END="08:15:00"

: "${VAN_POSTGRES_CONTAINER:?VAN_POSTGRES_CONTAINER is required}"
: "${PGUSER:?PGUSER is required}"
: "${PGDATABASE:?PGDATABASE is required}"

echo "[repair_account_daily_equity_from_minute] date=${TODAY_UTC} window=${WINDOW_START}..${WINDOW_END} UTC"

docker exec -i "${VAN_POSTGRES_CONTAINER}" psql -U "${PGUSER}" -d "${PGDATABASE}" -v ON_ERROR_STOP=1 -v today="${TODAY_UTC}" -v window_start="${WINDOW_START}" -v window_end="${WINDOW_END}" <<SQL
with params as (
    select
        :'today'::date as d,
        (:'today'::date + (:'window_start')::time) as start_utc,
        (:'today'::date + (:'window_end')::time) as end_utc
),
snap as (
    select distinct on (ae.acc_id)
        ae.acc_id,
        ae.timestamp_utc,
        round(coalesce(ae.equity_usdc_usd, 0)::numeric, 2) as usdc_equity,
        round(coalesce(ae.equity_eth_usd, 0)::numeric, 2) as eth_equity_usd,
        round(coalesce(ae.equity_btc_usd, 0)::numeric, 2) as btc_equity_usd
    from public.van_account_equity ae
    cross join params p
    where ae.timestamp_utc >= p.start_utc
      and ae.timestamp_utc <= p.end_utc
    order by ae.acc_id, ae.timestamp_utc asc
),
upserted as (
    insert into public.van_account_daily_equity (
        acc_id, date_utc,
        usdc_equity, eth_equity, btc_equity,
        eth_price, btc_price,
        eth_equity_usd, btc_equity_usd,
        include_usdc_in_total, include_eth_in_total, include_btc_in_total
    )
    select
        a.acc_id,
        p.d,
        s.usdc_equity,
        null,
        null,
        null,
        null,
        s.eth_equity_usd,
        s.btc_equity_usd,
        a.include_usdc_in_total,
        a.include_eth_in_total,
        a.include_btc_in_total
    from snap s
    join public.van_account a on a.acc_id = s.acc_id
    cross join params p
    on conflict (acc_id, date_utc) do update
    set
        usdc_equity = case when public.van_account_daily_equity.usdc_equity is null then excluded.usdc_equity else public.van_account_daily_equity.usdc_equity end,
        eth_equity_usd = case when public.van_account_daily_equity.eth_equity_usd is null then excluded.eth_equity_usd else public.van_account_daily_equity.eth_equity_usd end,
        btc_equity_usd = case when public.van_account_daily_equity.btc_equity_usd is null then excluded.btc_equity_usd else public.van_account_daily_equity.btc_equity_usd end,
        include_usdc_in_total = excluded.include_usdc_in_total,
        include_eth_in_total = excluded.include_eth_in_total,
        include_btc_in_total = excluded.include_btc_in_total
    returning acc_id, date_utc, usdc_equity, eth_equity_usd, btc_equity_usd, total_equity_usd
)
select * from upserted order by acc_id;
SQL
