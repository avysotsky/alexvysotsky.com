using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Npgsql;
using NpgsqlTypes;
using VANWebService.Security;

namespace VANWebService.Models;

public sealed class CoincallAccountRecord
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string apiKey { get; set; } = "";
    public string apiSecret { get; set; } = "";
    public bool isActive { get; set; }
    public bool storeMinuteEquity { get; set; }
    public string equityCurrency { get; set; } = "USD";
    public DateTime createdAtUtc { get; set; }
    public DateTime updatedAtUtc { get; set; }
}


public sealed class CoincallEquityPoint
{
    public DateTime tsUtc { get; set; }
    public decimal totalEquity { get; set; }
    public decimal availableEquity { get; set; }
    public decimal unrealizedPnl { get; set; }
    public decimal? marginRatio { get; set; }
    public string equityCurrency { get; set; } = "USD";
}

public sealed class CoincallAssetEquityPoint
{
    public DateTime tsUtc { get; set; }
    public string asset { get; set; } = "";
    public decimal? equityNative { get; set; }
    public decimal? availableNative { get; set; }
    public decimal? frozenNative { get; set; }
    public decimal? unrealizedPnlNative { get; set; }
    public decimal? equityUsdt { get; set; }
    public decimal? equityUsd { get; set; }
    public string valuationSource { get; set; } = "none";
    public string? equityNativeSource { get; set; }
    public string? availableNativeSource { get; set; }
    public string? frozenNativeSource { get; set; }
    public string? unrealizedPnlNativeSource { get; set; }
    public string? equityUsdSource { get; set; }
    public string rawJson { get; set; } = "{}";
}

public sealed class CoincallMinuteCandle
{
    public string symbol { get; set; } = "";
    public string displayName { get; set; } = "";
    public string baseCurrency { get; set; } = "";
    public string quoteCurrency { get; set; } = "";
    public DateTime minuteUtc { get; set; }
    public decimal? open { get; set; }
    public decimal? high { get; set; }
    public decimal? low { get; set; }
    public decimal? close { get; set; }
    public decimal? volume { get; set; }
    public decimal? quoteVolume { get; set; }
    public string tickerId { get; set; } = "";
    public string productType { get; set; } = "";
    public decimal? markPriceClose { get; set; }
    public decimal? indexPriceClose { get; set; }
    public decimal? bestBidClose { get; set; }
    public decimal? bestAskClose { get; set; }
}

public sealed class CoincallChartMinuteRow
{
    public DateTime minuteUtc { get; set; }
    public decimal? open { get; set; }
    public decimal? high { get; set; }
    public decimal? low { get; set; }
    public decimal? close { get; set; }
    public decimal? volume { get; set; }
    public decimal? markPriceClose { get; set; }
    public decimal? indexPriceClose { get; set; }
    public decimal? bestBidClose { get; set; }
    public decimal? bestAskClose { get; set; }
}

public sealed class CoincallFuturesSpreadMinuteRow
{
    public string spreadSymbol { get; set; } = "";
    public string baseCurrency { get; set; } = "";
    public string rowSymbol { get; set; } = "";
    public string rowDisplayName { get; set; } = "";
    public string colSymbol { get; set; } = "";
    public string colDisplayName { get; set; } = "";
    public DateTime minuteUtc { get; set; }
    public decimal? markSpreadClose { get; set; }
    public decimal? bidSpreadClose { get; set; }
    public decimal? askSpreadClose { get; set; }
    public decimal? midSpreadClose { get; set; }
}

public sealed class CoincallFuturesSpreadChartRow
{
    public DateTime minuteUtc { get; set; }
    public decimal? markSpreadClose { get; set; }
    public decimal? bidSpreadClose { get; set; }
    public decimal? askSpreadClose { get; set; }
    public decimal? midSpreadClose { get; set; }
}

public sealed class CoincallStoredSymbolRow
{
    public string symbol { get; set; } = "";
    public string displayName { get; set; } = "";
}

public sealed class CoincallFuturesFundingRow
{
    public string symbol { get; set; } = "";
    public string displayName { get; set; } = "";
    public string recordKey { get; set; } = "";
    public string recordId { get; set; } = "";
    public DateTime observedMinuteUtc { get; set; }
    public DateTime? recordTimeUtc { get; set; }
    public int? tradeSide { get; set; }
    public decimal? qty { get; set; }
    public decimal? fundFee { get; set; }
    public decimal? fundRate { get; set; }
    public decimal? interest8h { get; set; }
    public string rawJson { get; set; } = "{}";
    public DateTime createdAtUtc { get; set; }
    public DateTime updatedAtUtc { get; set; }
}

public sealed class CoincallRepository
{
    private readonly string _connectionString;
    private readonly Enums.LogAction _log;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaEnsured;

    public CoincallRepository(string connectionString, Enums.LogAction log)
    {
        _connectionString = connectionString;
        _log = log;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (_schemaEnsured) return;

        await _schemaLock.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;

            const string sql = @"
create table if not exists public.van_coincall_account (
    id serial primary key,
    name text not null,
    api_key text not null default '',
    api_key_cipher text not null default '',
    api_secret_cipher text not null default '',
    is_active boolean not null default false,
    store_minute_equity boolean not null default false,
    equity_currency text not null default 'USD',
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc')
);
alter table public.van_coincall_account add column if not exists api_key text not null default '';
alter table public.van_coincall_account add column if not exists api_key_cipher text not null default '';
alter table public.van_coincall_account add column if not exists api_secret_cipher text not null default '';
alter table public.van_coincall_account add column if not exists store_minute_equity boolean not null default false;
alter table public.van_coincall_account add column if not exists equity_currency text not null default 'USD';
alter table public.van_coincall_account drop constraint if exists van_coincall_account_equity_currency_chk;
alter table public.van_coincall_account add constraint van_coincall_account_equity_currency_chk check (equity_currency in ('USD','BTC'));
create unique index if not exists van_coincall_account_name_uq on public.van_coincall_account ((lower(name)));
create unique index if not exists van_coincall_account_single_active_idx on public.van_coincall_account ((is_active)) where is_active = true;

create table if not exists public.van_coincall_equity_minute (
    account_id integer not null references public.van_coincall_account(id) on delete cascade,
    minute_utc timestamptz not null,
    equity_currency text not null,
    total_equity numeric(38,10) not null,
    available_equity numeric(38,10) not null,
    unrealized_pnl numeric(38,10) not null,
    margin_ratio numeric(38,10) null,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (account_id, minute_utc)
);
create index if not exists van_coincall_equity_minute_account_ts_idx on public.van_coincall_equity_minute (account_id, minute_utc desc);

create table if not exists public.van_coincall_equity_daily (
    account_id integer not null references public.van_coincall_account(id) on delete cascade,
    day_utc date not null,
    last_ts_utc timestamptz not null,
    equity_currency text not null,
    total_equity numeric(38,10) not null,
    available_equity numeric(38,10) not null,
    unrealized_pnl numeric(38,10) not null,
    margin_ratio numeric(38,10) null,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (account_id, day_utc)
);
create index if not exists van_coincall_equity_daily_account_day_idx on public.van_coincall_equity_daily (account_id, day_utc desc);

create table if not exists public.van_coincall_asset_equity_minute (
    account_id integer not null references public.van_coincall_account(id) on delete cascade,
    asset text not null,
    minute_utc timestamptz not null,
    equity_native numeric(38,10) null,
    available_native numeric(38,10) null,
    frozen_native numeric(38,10) null,
    unrealized_pnl_native numeric(38,10) null,
    equity_usdt numeric(38,10) null,
    equity_usd numeric(38,10) null,
    valuation_source text not null default 'none',
    equity_native_source text null,
    available_native_source text null,
    frozen_native_source text null,
    unrealized_pnl_native_source text null,
    equity_usd_source text null,
    raw_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (account_id, asset, minute_utc)
);
create index if not exists van_coincall_asset_equity_minute_account_asset_ts_idx on public.van_coincall_asset_equity_minute (account_id, asset, minute_utc desc);
alter table public.van_coincall_asset_equity_minute add column if not exists equity_native numeric(38,10) null;
alter table public.van_coincall_asset_equity_minute add column if not exists available_native numeric(38,10) null;
alter table public.van_coincall_asset_equity_minute add column if not exists frozen_native numeric(38,10) null;
alter table public.van_coincall_asset_equity_minute add column if not exists unrealized_pnl_native numeric(38,10) null;
alter table public.van_coincall_asset_equity_minute add column if not exists equity_usdt numeric(38,10) null;
alter table public.van_coincall_asset_equity_minute add column if not exists equity_usd numeric(38,10) null;
alter table public.van_coincall_asset_equity_minute add column if not exists valuation_source text not null default 'none';
alter table public.van_coincall_asset_equity_minute add column if not exists equity_native_source text null;
alter table public.van_coincall_asset_equity_minute add column if not exists available_native_source text null;
alter table public.van_coincall_asset_equity_minute add column if not exists frozen_native_source text null;
alter table public.van_coincall_asset_equity_minute add column if not exists unrealized_pnl_native_source text null;
alter table public.van_coincall_asset_equity_minute add column if not exists equity_usd_source text null;
alter table public.van_coincall_asset_equity_minute add column if not exists raw_json jsonb not null default '{}'::jsonb;

create table if not exists public.van_coincall_asset_equity_daily (
    account_id integer not null references public.van_coincall_account(id) on delete cascade,
    asset text not null,
    day_utc date not null,
    last_ts_utc timestamptz not null,
    equity_native numeric(38,10) null,
    available_native numeric(38,10) null,
    frozen_native numeric(38,10) null,
    unrealized_pnl_native numeric(38,10) null,
    equity_usdt numeric(38,10) null,
    equity_usd numeric(38,10) null,
    valuation_source text not null default 'none',
    equity_native_source text null,
    available_native_source text null,
    frozen_native_source text null,
    unrealized_pnl_native_source text null,
    equity_usd_source text null,
    raw_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (account_id, asset, day_utc)
);
create index if not exists van_coincall_asset_equity_daily_account_asset_day_idx on public.van_coincall_asset_equity_daily (account_id, asset, day_utc desc);
alter table public.van_coincall_asset_equity_daily add column if not exists last_ts_utc timestamptz not null default (now() at time zone 'utc');
alter table public.van_coincall_asset_equity_daily add column if not exists equity_native numeric(38,10) null;
alter table public.van_coincall_asset_equity_daily add column if not exists available_native numeric(38,10) null;
alter table public.van_coincall_asset_equity_daily add column if not exists frozen_native numeric(38,10) null;
alter table public.van_coincall_asset_equity_daily add column if not exists unrealized_pnl_native numeric(38,10) null;
alter table public.van_coincall_asset_equity_daily add column if not exists equity_usdt numeric(38,10) null;
alter table public.van_coincall_asset_equity_daily add column if not exists equity_usd numeric(38,10) null;
alter table public.van_coincall_asset_equity_daily add column if not exists valuation_source text not null default 'none';
alter table public.van_coincall_asset_equity_daily add column if not exists equity_native_source text null;
alter table public.van_coincall_asset_equity_daily add column if not exists available_native_source text null;
alter table public.van_coincall_asset_equity_daily add column if not exists frozen_native_source text null;
alter table public.van_coincall_asset_equity_daily add column if not exists unrealized_pnl_native_source text null;
alter table public.van_coincall_asset_equity_daily add column if not exists equity_usd_source text null;
alter table public.van_coincall_asset_equity_daily add column if not exists raw_json jsonb not null default '{}'::jsonb;

create table if not exists public.van_coincall_spot_candle_minute (
    symbol text not null,
    base_currency text not null,
    quote_currency text not null,
    minute_utc timestamptz not null,
    open numeric(38,10) not null,
    high numeric(38,10) not null,
    low numeric(38,10) not null,
    close numeric(38,10) not null,
    volume numeric(38,10) not null,
    quote_volume numeric(38,10) null,
    best_bid_close numeric(38,10) null,
    best_ask_close numeric(38,10) null,
    index_price_close numeric(38,10) null,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (symbol, minute_utc)
);
create index if not exists van_coincall_spot_candle_minute_ts_idx on public.van_coincall_spot_candle_minute (minute_utc desc, symbol);
alter table public.van_coincall_spot_candle_minute add column if not exists best_bid_close numeric(38,10) null;
alter table public.van_coincall_spot_candle_minute add column if not exists best_ask_close numeric(38,10) null;
alter table public.van_coincall_spot_candle_minute add column if not exists index_price_close numeric(38,10) null;

create table if not exists public.van_coincall_futures_candle_minute (
    symbol text not null,
    display_name text not null default '',
    ticker_id text not null default '',
    base_currency text not null,
    quote_currency text not null,
    product_type text not null default '',
    minute_utc timestamptz not null,
    open numeric(38,10) not null,
    high numeric(38,10) not null,
    low numeric(38,10) not null,
    close numeric(38,10) not null,
    volume numeric(38,10) not null,
    quote_volume numeric(38,10) null,
    mark_price_close numeric(38,10) null,
    index_price_close numeric(38,10) null,
    best_bid_close numeric(38,10) null,
    best_ask_close numeric(38,10) null,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (symbol, minute_utc)
);
create index if not exists van_coincall_futures_candle_minute_ts_idx on public.van_coincall_futures_candle_minute (minute_utc desc, symbol);
alter table public.van_coincall_futures_candle_minute add column if not exists display_name text not null default '';
alter table public.van_coincall_futures_candle_minute add column if not exists mark_price_close numeric(38,10) null;
alter table public.van_coincall_futures_candle_minute add column if not exists index_price_close numeric(38,10) null;
alter table public.van_coincall_futures_candle_minute add column if not exists best_bid_close numeric(38,10) null;
alter table public.van_coincall_futures_candle_minute add column if not exists best_ask_close numeric(38,10) null;
alter table public.van_coincall_futures_candle_minute alter column open drop not null;
alter table public.van_coincall_futures_candle_minute alter column high drop not null;
alter table public.van_coincall_futures_candle_minute alter column low drop not null;
alter table public.van_coincall_futures_candle_minute alter column close drop not null;
alter table public.van_coincall_futures_candle_minute alter column volume drop not null;

create table if not exists public.van_coincall_futures_spread_minute (
    spread_symbol text not null,
    base_currency text not null,
    row_symbol text not null,
    row_display_name text not null default '',
    col_symbol text not null,
    col_display_name text not null default '',
    minute_utc timestamptz not null,
    mark_spread_close numeric(38,10) null,
    bid_spread_close numeric(38,10) null,
    ask_spread_close numeric(38,10) null,
    mid_spread_close numeric(38,10) null,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (spread_symbol, minute_utc)
);
create index if not exists van_coincall_futures_spread_minute_ts_idx on public.van_coincall_futures_spread_minute (minute_utc desc, spread_symbol);
create index if not exists van_coincall_futures_spread_minute_legs_idx on public.van_coincall_futures_spread_minute (row_symbol, col_symbol, minute_utc desc);

create table if not exists public.van_coincall_futures_funding_history (
    symbol text not null,
    display_name text not null default '',
    record_key text not null,
    record_id text not null default '',
    observed_minute_utc timestamptz not null,
    record_time_utc timestamptz null,
    trade_side integer null,
    qty numeric(38,10) null,
    fund_fee numeric(38,10) null,
    fund_rate numeric(38,18) null,
    interest_8h numeric(38,18) null,
    raw_json jsonb not null,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (symbol, record_key)
);
create index if not exists van_coincall_futures_funding_history_symbol_time_idx on public.van_coincall_futures_funding_history (symbol, coalesce(record_time_utc, observed_minute_utc) desc);
alter table public.van_coincall_futures_funding_history add column if not exists display_name text not null default '';
alter table public.van_coincall_futures_funding_history add column if not exists record_id text not null default '';
alter table public.van_coincall_futures_funding_history add column if not exists observed_minute_utc timestamptz not null default (date_trunc('minute', now() at time zone 'utc'));
alter table public.van_coincall_futures_funding_history add column if not exists record_time_utc timestamptz null;
alter table public.van_coincall_futures_funding_history add column if not exists trade_side integer null;
alter table public.van_coincall_futures_funding_history add column if not exists qty numeric(38,10) null;
alter table public.van_coincall_futures_funding_history add column if not exists fund_fee numeric(38,10) null;
alter table public.van_coincall_futures_funding_history add column if not exists fund_rate numeric(38,18) null;
alter table public.van_coincall_futures_funding_history add column if not exists interest_8h numeric(38,18) null;
alter table public.van_coincall_futures_funding_history add column if not exists raw_json jsonb not null default '{}'::jsonb;

create table if not exists public.van_coincall_futures_funding_minute (
    symbol text not null,
    display_name text not null default '',
    minute_utc timestamptz not null,
    fund_rate numeric(38,18) null,
    interest_8h numeric(38,18) null,
    fund_fee numeric(38,10) null,
    record_time_utc timestamptz null,
    trade_side integer null,
    qty numeric(38,10) null,
    raw_json jsonb not null,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (symbol, minute_utc)
);
create index if not exists van_coincall_futures_funding_minute_symbol_time_idx on public.van_coincall_futures_funding_minute (symbol, minute_utc desc);
alter table public.van_coincall_futures_funding_minute add column if not exists display_name text not null default '';
alter table public.van_coincall_futures_funding_minute add column if not exists fund_rate numeric(38,18) null;
alter table public.van_coincall_futures_funding_minute add column if not exists interest_8h numeric(38,18) null;
alter table public.van_coincall_futures_funding_minute add column if not exists fund_fee numeric(38,10) null;
alter table public.van_coincall_futures_funding_minute add column if not exists record_time_utc timestamptz null;
alter table public.van_coincall_futures_funding_minute add column if not exists trade_side integer null;
alter table public.van_coincall_futures_funding_minute add column if not exists qty numeric(38,10) null;
alter table public.van_coincall_futures_funding_minute add column if not exists raw_json jsonb not null default '{}'::jsonb;
";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
            _schemaEnsured = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public async Task<List<CoincallAccountRecord>> GetAccountsAsync(bool includeSecrets, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"select id, name, api_key_cipher, api_secret_cipher, is_active, store_minute_equity, equity_currency, created_at, updated_at from public.van_coincall_account order by is_active desc, lower(name), id;";
        var list = new List<CoincallAccountRecord>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new CoincallAccountRecord { id = r.GetInt32(0), name = r.GetString(1), apiKey = includeSecrets ? ApiSecretCrypto.Decrypt(r.GetString(2)) : "", apiSecret = includeSecrets ? ApiSecretCrypto.Decrypt(r.GetString(3)) : "", isActive = r.GetBoolean(4), storeMinuteEquity = r.GetBoolean(5), equityCurrency = NormalizeEquityCurrency(r.IsDBNull(6) ? "USD" : r.GetString(6)), createdAtUtc = r.GetDateTime(7), updatedAtUtc = r.GetDateTime(8) });
        }
        return list;
    }

    public async Task<CoincallAccountRecord?> GetAccountByIdAsync(int id, CancellationToken ct = default)
    {
        var accounts = await GetAccountsAsync(true, ct);
        return accounts.FirstOrDefault(a => a.id == id);
    }

    public async Task<CoincallAccountRecord?> GetActiveAccountAsync(CancellationToken ct = default)
    {
        var accounts = await GetAccountsAsync(true, ct);
        return accounts.FirstOrDefault(a => a.isActive) ?? accounts.FirstOrDefault();
    }

    public async Task<int> CreateAccountAsync(string name, string apiKey, string apiSecret, bool setActive, bool storeMinuteEquity, string equityCurrency, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (setActive)
        {
            await using var clear = new NpgsqlCommand("update public.van_coincall_account set is_active=false, updated_at=(now() at time zone 'utc') where is_active=true;", conn, tx);
            await clear.ExecuteNonQueryAsync(ct);
        }
        const string sql = @"insert into public.van_coincall_account (name, api_key, api_key_cipher, api_secret_cipher, is_active, store_minute_equity, equity_currency, created_at, updated_at) values (@name, '', @api_key_cipher, @api_secret_cipher, @is_active, @store_minute_equity, @equity_currency, (now() at time zone 'utc'), (now() at time zone 'utc')) returning id;";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("name", name.Trim());
        cmd.Parameters.AddWithValue("api_key_cipher", ApiSecretCrypto.Encrypt(apiKey.Trim()));
        cmd.Parameters.AddWithValue("api_secret_cipher", ApiSecretCrypto.Encrypt(apiSecret.Trim()));
        cmd.Parameters.AddWithValue("is_active", setActive);
        cmd.Parameters.AddWithValue("store_minute_equity", storeMinuteEquity);
        cmd.Parameters.AddWithValue("equity_currency", NormalizeEquityCurrency(equityCurrency));
        int id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAccountSettingsAsync(int id, bool storeMinuteEquity, string equityCurrency, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("update public.van_coincall_account set store_minute_equity=@store_minute_equity, equity_currency=@equity_currency, updated_at=(now() at time zone 'utc') where id=@id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("store_minute_equity", storeMinuteEquity);
        cmd.Parameters.AddWithValue("equity_currency", NormalizeEquityCurrency(equityCurrency));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public static string NormalizeEquityCurrency(string? equityCurrency)
    {
        string normalized = (equityCurrency ?? "USD").Trim().ToUpperInvariant();
        return normalized == "BTC" ? "BTC" : "USD";
    }

    private static string NormalizeAsset(string? asset)
    {
        return (asset ?? "").Trim().ToUpperInvariant();
    }

    public async Task<bool> SetActiveAccountAsync(int id, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var clear = new NpgsqlCommand("update public.van_coincall_account set is_active=false, updated_at=(now() at time zone 'utc') where is_active=true;", conn, tx)) await clear.ExecuteNonQueryAsync(ct);
        await using var cmd = new NpgsqlCommand("update public.van_coincall_account set is_active=true, updated_at=(now() at time zone 'utc') where id=@id;", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        int rows = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    public async Task<bool> DeleteAccountAsync(int id, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("delete from public.van_coincall_account where id=@id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task UpsertMinuteEquityAsync(int accountId, DateTime minuteUtc, string equityCurrency, decimal totalEquity, decimal availableEquity, decimal unrealizedPnl, decimal? marginRatio, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_equity_minute (account_id, minute_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
values (@account_id, @minute_utc, @equity_currency, @total_equity, @available_equity, @unrealized_pnl, @margin_ratio, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (account_id, minute_utc)
do update set equity_currency = excluded.equity_currency,
              total_equity = excluded.total_equity,
              available_equity = excluded.available_equity,
              unrealized_pnl = excluded.unrealized_pnl,
              margin_ratio = excluded.margin_ratio,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("minute_utc", minuteUtc);
        cmd.Parameters.AddWithValue("equity_currency", NormalizeEquityCurrency(equityCurrency));
        cmd.Parameters.AddWithValue("total_equity", totalEquity);
        cmd.Parameters.AddWithValue("available_equity", availableEquity);
        cmd.Parameters.AddWithValue("unrealized_pnl", unrealizedPnl);
        cmd.Parameters.AddWithValue("margin_ratio", marginRatio.HasValue ? marginRatio.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertDailyEquityAsync(int accountId, DateOnly dayUtc, DateTime tsUtc, string equityCurrency, decimal totalEquity, decimal availableEquity, decimal unrealizedPnl, decimal? marginRatio, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_equity_daily (account_id, day_utc, last_ts_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
values (@account_id, @day_utc, @last_ts_utc, @equity_currency, @total_equity, @available_equity, @unrealized_pnl, @margin_ratio, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (account_id, day_utc)
do update set last_ts_utc = excluded.last_ts_utc,
              equity_currency = excluded.equity_currency,
              total_equity = excluded.total_equity,
              available_equity = excluded.available_equity,
              unrealized_pnl = excluded.unrealized_pnl,
              margin_ratio = excluded.margin_ratio,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("day_utc", dayUtc);
        cmd.Parameters.AddWithValue("last_ts_utc", tsUtc);
        cmd.Parameters.AddWithValue("equity_currency", NormalizeEquityCurrency(equityCurrency));
        cmd.Parameters.AddWithValue("total_equity", totalEquity);
        cmd.Parameters.AddWithValue("available_equity", availableEquity);
        cmd.Parameters.AddWithValue("unrealized_pnl", unrealizedPnl);
        cmd.Parameters.AddWithValue("margin_ratio", marginRatio.HasValue ? marginRatio.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertAssetMinuteEquityAsync(int accountId, DateTime minuteUtc, CoincallAssetEquityPoint point, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_asset_equity_minute (account_id, asset, minute_utc, equity_native, available_native, frozen_native, unrealized_pnl_native, equity_usdt, equity_usd, valuation_source, equity_native_source, available_native_source, frozen_native_source, unrealized_pnl_native_source, equity_usd_source, raw_json, created_at, updated_at)
values (@account_id, @asset, @minute_utc, @equity_native, @available_native, @frozen_native, @unrealized_pnl_native, @equity_usdt, @equity_usd, @valuation_source, @equity_native_source, @available_native_source, @frozen_native_source, @unrealized_pnl_native_source, @equity_usd_source, cast(@raw_json as jsonb), (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (account_id, asset, minute_utc)
do update set equity_native = excluded.equity_native,
              available_native = excluded.available_native,
              frozen_native = excluded.frozen_native,
              unrealized_pnl_native = excluded.unrealized_pnl_native,
              equity_usdt = excluded.equity_usdt,
              equity_usd = excluded.equity_usd,
              valuation_source = excluded.valuation_source,
              equity_native_source = excluded.equity_native_source,
              available_native_source = excluded.available_native_source,
              frozen_native_source = excluded.frozen_native_source,
              unrealized_pnl_native_source = excluded.unrealized_pnl_native_source,
              equity_usd_source = excluded.equity_usd_source,
              raw_json = excluded.raw_json,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        BindAssetEquity(cmd, accountId, point.asset, minuteUtc, point);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertAssetDailyEquityAsync(int accountId, DateOnly dayUtc, DateTime tsUtc, CoincallAssetEquityPoint point, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_asset_equity_daily (account_id, asset, day_utc, last_ts_utc, equity_native, available_native, frozen_native, unrealized_pnl_native, equity_usdt, equity_usd, valuation_source, equity_native_source, available_native_source, frozen_native_source, unrealized_pnl_native_source, equity_usd_source, raw_json, created_at, updated_at)
values (@account_id, @asset, @day_utc, @last_ts_utc, @equity_native, @available_native, @frozen_native, @unrealized_pnl_native, @equity_usdt, @equity_usd, @valuation_source, @equity_native_source, @available_native_source, @frozen_native_source, @unrealized_pnl_native_source, @equity_usd_source, cast(@raw_json as jsonb), (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (account_id, asset, day_utc)
do update set last_ts_utc = excluded.last_ts_utc,
              equity_native = excluded.equity_native,
              available_native = excluded.available_native,
              frozen_native = excluded.frozen_native,
              unrealized_pnl_native = excluded.unrealized_pnl_native,
              equity_usdt = excluded.equity_usdt,
              equity_usd = excluded.equity_usd,
              valuation_source = excluded.valuation_source,
              equity_native_source = excluded.equity_native_source,
              available_native_source = excluded.available_native_source,
              frozen_native_source = excluded.frozen_native_source,
              unrealized_pnl_native_source = excluded.unrealized_pnl_native_source,
              equity_usd_source = excluded.equity_usd_source,
              raw_json = excluded.raw_json,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        BindAssetEquity(cmd, accountId, point.asset, tsUtc, point);
        cmd.Parameters.AddWithValue("day_utc", dayUtc);
        cmd.Parameters.AddWithValue("last_ts_utc", tsUtc);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void BindAssetEquity(NpgsqlCommand cmd, int accountId, string asset, DateTime minuteUtc, CoincallAssetEquityPoint point)
    {
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("asset", asset.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("minute_utc", minuteUtc);
        AddNullableNumeric(cmd, "equity_native", point.equityNative);
        AddNullableNumeric(cmd, "available_native", point.availableNative);
        AddNullableNumeric(cmd, "frozen_native", point.frozenNative);
        AddNullableNumeric(cmd, "unrealized_pnl_native", point.unrealizedPnlNative);
        AddNullableNumeric(cmd, "equity_usdt", point.equityUsdt);
        AddNullableNumeric(cmd, "equity_usd", point.equityUsd);
        cmd.Parameters.AddWithValue("valuation_source", string.IsNullOrWhiteSpace(point.valuationSource) ? "none" : point.valuationSource);
        cmd.Parameters.AddWithValue("equity_native_source", string.IsNullOrWhiteSpace(point.equityNativeSource) ? DBNull.Value : point.equityNativeSource);
        cmd.Parameters.AddWithValue("available_native_source", string.IsNullOrWhiteSpace(point.availableNativeSource) ? DBNull.Value : point.availableNativeSource);
        cmd.Parameters.AddWithValue("frozen_native_source", string.IsNullOrWhiteSpace(point.frozenNativeSource) ? DBNull.Value : point.frozenNativeSource);
        cmd.Parameters.AddWithValue("unrealized_pnl_native_source", string.IsNullOrWhiteSpace(point.unrealizedPnlNativeSource) ? DBNull.Value : point.unrealizedPnlNativeSource);
        cmd.Parameters.AddWithValue("equity_usd_source", string.IsNullOrWhiteSpace(point.equityUsdSource) ? DBNull.Value : point.equityUsdSource);
        cmd.Parameters.AddWithValue("raw_json", NpgsqlDbType.Jsonb, string.IsNullOrWhiteSpace(point.rawJson) ? "{}" : point.rawJson);
    }

    public async Task<List<CoincallEquityPoint>> GetMinuteEquityAsync(int accountId, int hours, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_coincall_equity_minute
where account_id = @account_id
  and minute_utc >= ((now() at time zone 'utc') - make_interval(hours => @hours))
order by minute_utc;
";
        return await ReadEquityPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("hours", Math.Max(1, hours));
        }, ct);
    }

    public async Task<List<CoincallEquityPoint>> GetMinuteEquityFromAsync(int accountId, DateTime fromUtc, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_coincall_equity_minute
where account_id = @account_id
  and minute_utc >= @from_utc
order by minute_utc;
";
        return await ReadEquityPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("from_utc", fromUtc);
        }, ct);
    }

    public async Task<List<CoincallEquityPoint>> GetMinuteEquityForDailyDrawdownAsync(int accountId, int days, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_coincall_equity_minute
where account_id = @account_id
  and minute_utc >= (((now() at time zone 'utc')::date) - @days)
order by minute_utc;
";
        return await ReadEquityPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("days", Math.Max(1, days));
        }, ct);
    }

    public async Task<List<CoincallEquityPoint>> GetUsdMinuteEquitySnapshotsAsync(int accountId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_coincall_equity_minute
where account_id = @account_id
  and minute_utc >= @from_utc
  and minute_utc <= @to_utc
  and upper(equity_currency) in ('USD', 'USDT')
order by minute_utc;
";
        return await ReadEquityPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("from_utc", fromUtc);
            cmd.Parameters.AddWithValue("to_utc", toUtc);
        }, ct);
    }

    public async Task<List<CoincallEquityPoint>> GetDailyEquityAsync(int accountId, int days, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select last_ts_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_coincall_equity_daily
where account_id = @account_id
  and day_utc >= (((now() at time zone 'utc')::date) - @days)
order by day_utc;
";
        return await ReadEquityPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("days", Math.Max(1, days));
        }, ct);
    }

    public async Task<List<CoincallAssetEquityPoint>> GetAssetMinuteEquityAsync(int accountId, string asset, int hours, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select minute_utc, asset, equity_native, available_native, frozen_native, unrealized_pnl_native, equity_usdt, equity_usd, valuation_source,
       equity_native_source, available_native_source, frozen_native_source, unrealized_pnl_native_source, equity_usd_source, raw_json::text
from public.van_coincall_asset_equity_minute
where account_id = @account_id
  and upper(asset) = @asset
  and minute_utc >= ((now() at time zone 'utc') - make_interval(hours => @hours))
order by minute_utc;
";
        return await ReadAssetEquityPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("asset", NormalizeAsset(asset));
            cmd.Parameters.AddWithValue("hours", Math.Max(1, hours));
        }, ct);
    }

    public async Task<List<CoincallAssetEquityPoint>> GetAssetMinuteEquityForDailyDrawdownAsync(int accountId, string asset, int days, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select minute_utc, asset, equity_native, available_native, frozen_native, unrealized_pnl_native, equity_usdt, equity_usd, valuation_source,
       equity_native_source, available_native_source, frozen_native_source, unrealized_pnl_native_source, equity_usd_source, raw_json::text
from public.van_coincall_asset_equity_minute
where account_id = @account_id
  and upper(asset) = @asset
  and minute_utc >= (((now() at time zone 'utc')::date) - @days)
order by minute_utc;
";
        return await ReadAssetEquityPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("asset", NormalizeAsset(asset));
            cmd.Parameters.AddWithValue("days", Math.Max(1, days));
        }, ct);
    }

    public async Task<List<CoincallAssetEquityPoint>> GetAssetDailyEquityAsync(int accountId, string asset, int days, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select last_ts_utc, asset, equity_native, available_native, frozen_native, unrealized_pnl_native, equity_usdt, equity_usd, valuation_source,
       equity_native_source, available_native_source, frozen_native_source, unrealized_pnl_native_source, equity_usd_source, raw_json::text
from public.van_coincall_asset_equity_daily
where account_id = @account_id
  and upper(asset) = @asset
  and day_utc >= (((now() at time zone 'utc')::date) - @days)
order by day_utc;
";
        return await ReadAssetEquityPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("asset", NormalizeAsset(asset));
            cmd.Parameters.AddWithValue("days", Math.Max(1, days));
        }, ct);
    }

    private async Task<List<CoincallAssetEquityPoint>> ReadAssetEquityPointsAsync(string sql, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        var list = new List<CoincallAssetEquityPoint>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CoincallAssetEquityPoint
            {
                tsUtc = reader.GetDateTime(0),
                asset = reader.GetString(1),
                equityNative = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                availableNative = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                frozenNative = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                unrealizedPnlNative = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                equityUsdt = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                equityUsd = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                valuationSource = reader.IsDBNull(8) ? "none" : reader.GetString(8),
                equityNativeSource = reader.IsDBNull(9) ? null : reader.GetString(9),
                availableNativeSource = reader.IsDBNull(10) ? null : reader.GetString(10),
                frozenNativeSource = reader.IsDBNull(11) ? null : reader.GetString(11),
                unrealizedPnlNativeSource = reader.IsDBNull(12) ? null : reader.GetString(12),
                equityUsdSource = reader.IsDBNull(13) ? null : reader.GetString(13),
                rawJson = reader.IsDBNull(14) ? "{}" : reader.GetString(14)
            });
        }
        return list;
    }

    private async Task<List<CoincallEquityPoint>> ReadEquityPointsAsync(string sql, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        var list = new List<CoincallEquityPoint>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CoincallEquityPoint
            {
                tsUtc = reader.GetDateTime(0),
                totalEquity = reader.GetDecimal(1),
                availableEquity = reader.GetDecimal(2),
                unrealizedPnl = reader.GetDecimal(3),
                marginRatio = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                equityCurrency = NormalizeEquityCurrency(reader.IsDBNull(5) ? "USD" : reader.GetString(5))
            });
        }
        return list;
    }

    public async Task UpsertSpotMinuteCandleAsync(CoincallMinuteCandle candle, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
	insert into public.van_coincall_spot_candle_minute (symbol, base_currency, quote_currency, minute_utc, open, high, low, close, volume, quote_volume, best_bid_close, best_ask_close, index_price_close, created_at, updated_at)
	values (@symbol, @base_currency, @quote_currency, @minute_utc, @open, @high, @low, @close, @volume, @quote_volume, @best_bid_close, @best_ask_close, @index_price_close, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (symbol, minute_utc)
do update set base_currency = excluded.base_currency,
              quote_currency = excluded.quote_currency,
              open = excluded.open,
              high = excluded.high,
              low = excluded.low,
	              close = excluded.close,
	              volume = excluded.volume,
	              quote_volume = excluded.quote_volume,
	              best_bid_close = coalesce(excluded.best_bid_close, public.van_coincall_spot_candle_minute.best_bid_close),
	              best_ask_close = coalesce(excluded.best_ask_close, public.van_coincall_spot_candle_minute.best_ask_close),
	              index_price_close = coalesce(excluded.index_price_close, public.van_coincall_spot_candle_minute.index_price_close),
	              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        BindMinuteCandle(cmd, candle);
        AddNullableNumeric(cmd, "best_bid_close", candle.bestBidClose);
        AddNullableNumeric(cmd, "best_ask_close", candle.bestAskClose);
        AddNullableNumeric(cmd, "index_price_close", candle.indexPriceClose);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertFuturesMinuteCandleAsync(CoincallMinuteCandle candle, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_futures_candle_minute (symbol, display_name, ticker_id, base_currency, quote_currency, product_type, minute_utc, open, high, low, close, volume, quote_volume, mark_price_close, index_price_close, best_bid_close, best_ask_close, created_at, updated_at)
values (@symbol, @display_name, @ticker_id, @base_currency, @quote_currency, @product_type, @minute_utc, @open, @high, @low, @close, @volume, @quote_volume, @mark_price_close, @index_price_close, @best_bid_close, @best_ask_close, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (symbol, minute_utc)
do update set display_name = excluded.display_name,
              ticker_id = excluded.ticker_id,
              base_currency = excluded.base_currency,
              quote_currency = excluded.quote_currency,
              product_type = excluded.product_type,
              open = coalesce(excluded.open, public.van_coincall_futures_candle_minute.open),
              high = coalesce(excluded.high, public.van_coincall_futures_candle_minute.high),
              low = coalesce(excluded.low, public.van_coincall_futures_candle_minute.low),
              close = coalesce(excluded.close, public.van_coincall_futures_candle_minute.close),
              volume = coalesce(excluded.volume, public.van_coincall_futures_candle_minute.volume),
              quote_volume = coalesce(excluded.quote_volume, public.van_coincall_futures_candle_minute.quote_volume),
              mark_price_close = coalesce(excluded.mark_price_close, public.van_coincall_futures_candle_minute.mark_price_close),
              index_price_close = coalesce(excluded.index_price_close, public.van_coincall_futures_candle_minute.index_price_close),
              best_bid_close = excluded.best_bid_close,
              best_ask_close = excluded.best_ask_close,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        BindMinuteCandle(cmd, candle);
        cmd.Parameters.AddWithValue("display_name", candle.displayName ?? string.Empty);
        cmd.Parameters.AddWithValue("ticker_id", candle.tickerId ?? string.Empty);
        cmd.Parameters.AddWithValue("product_type", candle.productType ?? string.Empty);
        AddNullableNumeric(cmd, "mark_price_close", candle.markPriceClose);
        AddNullableNumeric(cmd, "index_price_close", candle.indexPriceClose);
        AddNullableNumeric(cmd, "best_bid_close", candle.bestBidClose);
        AddNullableNumeric(cmd, "best_ask_close", candle.bestAskClose);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> TryPatchFuturesMinuteCloseSnapshotAsync(string symbol, DateTime minuteUtc, decimal? markPriceClose, decimal? indexPriceClose, decimal? bestBidClose, decimal? bestAskClose, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        if (!markPriceClose.HasValue && !indexPriceClose.HasValue && !bestBidClose.HasValue && !bestAskClose.HasValue) return false;

        await EnsureSchemaAsync(ct);
        const string sql = @"
update public.van_coincall_futures_candle_minute as t
set mark_price_close = coalesce(@mark_price_close, t.mark_price_close),
    index_price_close = coalesce(@index_price_close, t.index_price_close),
    best_bid_close = coalesce(@best_bid_close, t.best_bid_close),
    best_ask_close = coalesce(@best_ask_close, t.best_ask_close),
    updated_at = (now() at time zone 'utc')
where t.symbol = @symbol
  and t.minute_utc = @minute_utc
  and (
      (@mark_price_close is not null and t.mark_price_close is distinct from @mark_price_close)
      or (@index_price_close is not null and t.index_price_close is distinct from @index_price_close)
      or (@best_bid_close is not null and t.best_bid_close is distinct from @best_bid_close)
      or (@best_ask_close is not null and t.best_ask_close is distinct from @best_ask_close)
  );
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol", symbol.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("minute_utc", TruncateToMinuteUtc(minuteUtc));
        AddNullableNumeric(cmd, "mark_price_close", markPriceClose);
        AddNullableNumeric(cmd, "index_price_close", indexPriceClose);
        AddNullableNumeric(cmd, "best_bid_close", bestBidClose);
        AddNullableNumeric(cmd, "best_ask_close", bestAskClose);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task UpsertFuturesSpreadMinuteRowsAsync(IEnumerable<CoincallFuturesSpreadMinuteRow> rows, CancellationToken ct = default)
    {
        var list = rows
            .Where(r => r != null && !string.IsNullOrWhiteSpace(r.spreadSymbol))
            .ToList();
        if (list.Count == 0) return;

        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_futures_spread_minute (spread_symbol, base_currency, row_symbol, row_display_name, col_symbol, col_display_name, minute_utc, mark_spread_close, bid_spread_close, ask_spread_close, mid_spread_close, created_at, updated_at)
values (@spread_symbol, @base_currency, @row_symbol, @row_display_name, @col_symbol, @col_display_name, @minute_utc, @mark_spread_close, @bid_spread_close, @ask_spread_close, @mid_spread_close, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (spread_symbol, minute_utc)
do update set base_currency = excluded.base_currency,
              row_symbol = excluded.row_symbol,
              row_display_name = excluded.row_display_name,
              col_symbol = excluded.col_symbol,
              col_display_name = excluded.col_display_name,
              mark_spread_close = coalesce(excluded.mark_spread_close, public.van_coincall_futures_spread_minute.mark_spread_close),
              bid_spread_close = excluded.bid_spread_close,
              ask_spread_close = excluded.ask_spread_close,
              mid_spread_close = excluded.mid_spread_close,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        foreach (var row in list)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            BindFuturesSpreadMinute(cmd, row);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<int> UpsertFuturesSpreadMinutesFromCandlesAsync(IEnumerable<DateTime> minuteUtcValues, CancellationToken ct = default)
    {
        var minutes = minuteUtcValues
            .Select(TruncateToMinuteUtc)
            .Distinct()
            .ToArray();
        if (minutes.Length == 0) return 0;

        await EnsureSchemaAsync(ct);
        const string sql = @"
with minutes as (
    select unnest(@minutes) as minute_utc
),
legs as (
    select
        c.symbol,
        c.display_name,
        c.base_currency,
        c.minute_utc,
        coalesce(c.mark_price_close, c.close) as mark_close,
        c.best_bid_close,
        c.best_ask_close
    from public.van_coincall_futures_candle_minute c
    join minutes m on m.minute_utc = c.minute_utc
    where c.base_currency in ('BTC','ETH')
),
spreads as (
    select
        (col_leg.symbol || '_' || row_leg.symbol) as spread_symbol,
        row_leg.base_currency,
        row_leg.symbol as row_symbol,
        coalesce(nullif(row_leg.display_name, ''), row_leg.symbol) as row_display_name,
        col_leg.symbol as col_symbol,
        coalesce(nullif(col_leg.display_name, ''), col_leg.symbol) as col_display_name,
        row_leg.minute_utc,
        case when col_leg.mark_close is not null and row_leg.mark_close is not null then col_leg.mark_close - row_leg.mark_close else null end as mark_spread_close,
        case when col_leg.best_bid_close is not null and row_leg.best_ask_close is not null then col_leg.best_bid_close - row_leg.best_ask_close else null end as bid_spread_close,
        case when col_leg.best_ask_close is not null and row_leg.best_bid_close is not null then col_leg.best_ask_close - row_leg.best_bid_close else null end as ask_spread_close
    from legs row_leg
    join legs col_leg
      on col_leg.base_currency = row_leg.base_currency
     and col_leg.minute_utc = row_leg.minute_utc
     and col_leg.symbol <> row_leg.symbol
)
insert into public.van_coincall_futures_spread_minute (spread_symbol, base_currency, row_symbol, row_display_name, col_symbol, col_display_name, minute_utc, mark_spread_close, bid_spread_close, ask_spread_close, mid_spread_close, created_at, updated_at)
select
    spread_symbol,
    base_currency,
    row_symbol,
    row_display_name,
    col_symbol,
    col_display_name,
    minute_utc,
    mark_spread_close,
    bid_spread_close,
    ask_spread_close,
    case when bid_spread_close is not null and ask_spread_close is not null then (bid_spread_close + ask_spread_close) / 2 else null end as mid_spread_close,
    (now() at time zone 'utc'),
    (now() at time zone 'utc')
from spreads
where mark_spread_close is not null or (bid_spread_close is not null and ask_spread_close is not null)
on conflict (spread_symbol, minute_utc)
do update set base_currency = excluded.base_currency,
              row_symbol = excluded.row_symbol,
              row_display_name = excluded.row_display_name,
              col_symbol = excluded.col_symbol,
              col_display_name = excluded.col_display_name,
              mark_spread_close = coalesce(excluded.mark_spread_close, public.van_coincall_futures_spread_minute.mark_spread_close),
              bid_spread_close = excluded.bid_spread_close,
              ask_spread_close = excluded.ask_spread_close,
              mid_spread_close = excluded.mid_spread_close,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("minutes", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = minutes });
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<CoincallFuturesSpreadChartRow>> GetFuturesSpreadMinuteRowsAsync(string rowSymbol, string colSymbol, int limit, DateTime? beforeUtc = null, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        string spreadSymbol = ((colSymbol ?? string.Empty).Trim().ToUpperInvariant() + "_" + (rowSymbol ?? string.Empty).Trim().ToUpperInvariant()).Trim('_');
        const string sql = @"
select minute_utc, mark_spread_close, bid_spread_close, ask_spread_close, mid_spread_close
from public.van_coincall_futures_spread_minute
where spread_symbol = @spread_symbol
  and (@before_utc is null or minute_utc < @before_utc)
order by minute_utc desc
limit @limit;
";
        var list = new List<CoincallFuturesSpreadChartRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("spread_symbol", spreadSymbol);
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 129600));
        cmd.Parameters.Add(new NpgsqlParameter("before_utc", NpgsqlDbType.TimestampTz) { Value = beforeUtc.HasValue ? beforeUtc.Value : DBNull.Value });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CoincallFuturesSpreadChartRow
            {
                minuteUtc = reader.GetDateTime(0),
                markSpreadClose = reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                bidSpreadClose = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                askSpreadClose = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                midSpreadClose = reader.IsDBNull(4) ? null : reader.GetDecimal(4)
            });
        }
        list.Reverse();
        return list;
    }

    public async Task UpsertFuturesFundingHistoryAsync(CoincallFuturesFundingRow row, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_futures_funding_history
    (symbol, display_name, record_key, record_id, observed_minute_utc, record_time_utc, trade_side, qty, fund_fee, fund_rate, interest_8h, raw_json, created_at, updated_at)
values
    (@symbol, @display_name, @record_key, @record_id, @observed_minute_utc, @record_time_utc, @trade_side, @qty, @fund_fee, @fund_rate, @interest_8h, cast(@raw_json as jsonb), (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (symbol, record_key)
do update set display_name = excluded.display_name,
              record_id = excluded.record_id,
              observed_minute_utc = least(public.van_coincall_futures_funding_history.observed_minute_utc, excluded.observed_minute_utc),
              record_time_utc = coalesce(excluded.record_time_utc, public.van_coincall_futures_funding_history.record_time_utc),
              trade_side = coalesce(excluded.trade_side, public.van_coincall_futures_funding_history.trade_side),
              qty = coalesce(excluded.qty, public.van_coincall_futures_funding_history.qty),
              fund_fee = coalesce(excluded.fund_fee, public.van_coincall_futures_funding_history.fund_fee),
              fund_rate = coalesce(excluded.fund_rate, public.van_coincall_futures_funding_history.fund_rate),
              interest_8h = coalesce(excluded.interest_8h, public.van_coincall_futures_funding_history.interest_8h),
              raw_json = excluded.raw_json,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        BindFuturesFunding(cmd, row);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<CoincallFuturesFundingRow>> GetFuturesFundingHistoryAsync(string symbol, int limit, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select symbol, display_name, record_key, record_id, observed_minute_utc, record_time_utc, trade_side, qty, fund_fee, fund_rate, interest_8h, raw_json::text, created_at, updated_at
from public.van_coincall_futures_funding_history
where symbol = @symbol
order by coalesce(record_time_utc, observed_minute_utc) desc, updated_at desc
limit @limit;
";
        var list = new List<CoincallFuturesFundingRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol", (symbol ?? string.Empty).Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 5000));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CoincallFuturesFundingRow
            {
                symbol = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                displayName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                recordKey = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                recordId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                observedMinuteUtc = reader.GetDateTime(4),
                recordTimeUtc = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                tradeSide = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                qty = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                fundFee = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                fundRate = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                interest8h = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                rawJson = reader.IsDBNull(11) ? "{}" : reader.GetString(11),
                createdAtUtc = reader.GetDateTime(12),
                updatedAtUtc = reader.GetDateTime(13)
            });
        }
        list.Reverse();
        return list;
    }

    public async Task UpsertFuturesFundingMinuteAsync(CoincallFuturesFundingRow row, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_futures_funding_minute
    (symbol, display_name, minute_utc, fund_rate, interest_8h, fund_fee, record_time_utc, trade_side, qty, raw_json, created_at, updated_at)
values
    (@symbol, @display_name, @observed_minute_utc, @fund_rate, @interest_8h, @fund_fee, @record_time_utc, @trade_side, @qty, cast(@raw_json as jsonb), (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (symbol, minute_utc)
do update set display_name = excluded.display_name,
              fund_rate = excluded.fund_rate,
              interest_8h = excluded.interest_8h,
              fund_fee = excluded.fund_fee,
              record_time_utc = excluded.record_time_utc,
              trade_side = excluded.trade_side,
              qty = excluded.qty,
              raw_json = excluded.raw_json,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        BindFuturesFunding(cmd, row);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<CoincallFuturesFundingRow>> GetFuturesFundingMinuteAsync(string symbol, int limit, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select symbol, display_name, ('minute:' || extract(epoch from minute_utc)::bigint::text) as record_key, '' as record_id, minute_utc, record_time_utc, trade_side, qty, fund_fee, fund_rate, interest_8h, raw_json::text, created_at, updated_at
from public.van_coincall_futures_funding_minute
where symbol = @symbol
order by minute_utc desc
limit @limit;
";
        var list = new List<CoincallFuturesFundingRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol", (symbol ?? string.Empty).Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 129600));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CoincallFuturesFundingRow
            {
                symbol = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                displayName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                recordKey = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                recordId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                observedMinuteUtc = reader.GetDateTime(4),
                recordTimeUtc = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                tradeSide = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                qty = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                fundFee = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                fundRate = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                interest8h = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                rawJson = reader.IsDBNull(11) ? "{}" : reader.GetString(11),
                createdAtUtc = reader.GetDateTime(12),
                updatedAtUtc = reader.GetDateTime(13)
            });
        }
        list.Reverse();
        return list;
    }

    private static void BindMinuteCandle(NpgsqlCommand cmd, CoincallMinuteCandle candle)
    {
        cmd.Parameters.AddWithValue("symbol", candle.symbol);
        cmd.Parameters.AddWithValue("base_currency", candle.baseCurrency);
        cmd.Parameters.AddWithValue("quote_currency", candle.quoteCurrency);
        cmd.Parameters.AddWithValue("minute_utc", TruncateToMinuteUtc(candle.minuteUtc));
        AddNullableNumeric(cmd, "open", candle.open);
        AddNullableNumeric(cmd, "high", candle.high);
        AddNullableNumeric(cmd, "low", candle.low);
        AddNullableNumeric(cmd, "close", candle.close);
        AddNullableNumeric(cmd, "volume", candle.volume);
        AddNullableNumeric(cmd, "quote_volume", candle.quoteVolume);
    }

    private static void AddNullableNumeric(NpgsqlCommand cmd, string name, decimal? value)
    {
        var p = new NpgsqlParameter(name, NpgsqlDbType.Numeric);
        p.Value = value.HasValue ? value.Value : DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static void BindFuturesSpreadMinute(NpgsqlCommand cmd, CoincallFuturesSpreadMinuteRow row)
    {
        cmd.Parameters.AddWithValue("spread_symbol", row.spreadSymbol.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("base_currency", row.baseCurrency.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("row_symbol", row.rowSymbol.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("row_display_name", row.rowDisplayName ?? string.Empty);
        cmd.Parameters.AddWithValue("col_symbol", row.colSymbol.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("col_display_name", row.colDisplayName ?? string.Empty);
        cmd.Parameters.AddWithValue("minute_utc", TruncateToMinuteUtc(row.minuteUtc));
        AddNullableNumeric(cmd, "mark_spread_close", row.markSpreadClose);
        AddNullableNumeric(cmd, "bid_spread_close", row.bidSpreadClose);
        AddNullableNumeric(cmd, "ask_spread_close", row.askSpreadClose);
        AddNullableNumeric(cmd, "mid_spread_close", row.midSpreadClose);
    }

    private static void BindFuturesFunding(NpgsqlCommand cmd, CoincallFuturesFundingRow row)
    {
        cmd.Parameters.AddWithValue("symbol", (row.symbol ?? string.Empty).Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("display_name", row.displayName ?? string.Empty);
        cmd.Parameters.AddWithValue("record_key", row.recordKey ?? string.Empty);
        cmd.Parameters.AddWithValue("record_id", row.recordId ?? string.Empty);
        cmd.Parameters.AddWithValue("observed_minute_utc", TruncateToMinuteUtc(row.observedMinuteUtc));
        var recordTime = new NpgsqlParameter("record_time_utc", NpgsqlDbType.TimestampTz) { Value = row.recordTimeUtc.HasValue ? row.recordTimeUtc.Value : DBNull.Value };
        cmd.Parameters.Add(recordTime);
        var tradeSide = new NpgsqlParameter("trade_side", NpgsqlDbType.Integer) { Value = row.tradeSide.HasValue ? row.tradeSide.Value : DBNull.Value };
        cmd.Parameters.Add(tradeSide);
        AddNullableNumeric(cmd, "qty", row.qty);
        AddNullableNumeric(cmd, "fund_fee", row.fundFee);
        AddNullableNumeric(cmd, "fund_rate", row.fundRate);
        AddNullableNumeric(cmd, "interest_8h", row.interest8h);
        cmd.Parameters.Add(new NpgsqlParameter("raw_json", NpgsqlDbType.Jsonb) { Value = string.IsNullOrWhiteSpace(row.rawJson) ? "{}" : row.rawJson });
    }

    public static DateTime TruncateToMinuteUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    public static string MaskApiKey(string? value)
    {
        string v = value ?? string.Empty;
        if (v.Length <= 8) return string.IsNullOrWhiteSpace(v) ? "" : "••••";
        return v[..4] + "…" + v[^4..];
    }

    public async Task<Dictionary<DateOnly, decimal>> GetSpotDailyCloseByClearingDayAsync(string symbol, DateOnly fromDayUtc, DateOnly toDayUtc, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select distinct on (((minute_utc - interval '8 hours')::date))
       ((minute_utc - interval '8 hours')::date) as clearing_day,
       close
from public.van_coincall_spot_candle_minute
where symbol = @symbol
  and ((minute_utc - interval '8 hours')::date) >= @from_day
  and ((minute_utc - interval '8 hours')::date) <= @to_day
  and close is not null
order by ((minute_utc - interval '8 hours')::date), minute_utc desc;
";
        var result = new Dictionary<DateOnly, decimal>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol", (symbol ?? string.Empty).Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("from_day", fromDayUtc);
        cmd.Parameters.AddWithValue("to_day", toDayUtc);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result[DateOnly.FromDateTime(reader.GetDateTime(0))] = reader.GetDecimal(1);
        }
        return result;
    }

    public async Task<List<CoincallChartMinuteRow>> GetSpotMinuteCandlesAsync(string symbol, int limit, DateTime? beforeUtc = null, CancellationToken ct = default)
        => await GetMinuteCandlesAsync("public.van_coincall_spot_candle_minute", symbol, limit, beforeUtc, ct);

    public async Task<List<CoincallChartMinuteRow>> GetFuturesMinuteCandlesAsync(string symbol, int limit, DateTime? beforeUtc = null, CancellationToken ct = default)
        => await GetMinuteCandlesAsync("public.van_coincall_futures_candle_minute", symbol, limit, beforeUtc, ct);

    private async Task<List<CoincallChartMinuteRow>> GetMinuteCandlesAsync(string tableName, string symbol, int limit, DateTime? beforeUtc, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        string safeTable = tableName switch
        {
            "public.van_coincall_spot_candle_minute" => tableName,
            "public.van_coincall_futures_candle_minute" => tableName,
            _ => throw new InvalidOperationException("Unsupported CoinCall candle table")
        };
        string selectMark = safeTable == "public.van_coincall_futures_candle_minute"
            ? "mark_price_close"
            : "null::numeric as mark_price_close";
        string selectIndex = "index_price_close";
        string sql = $@"
select minute_utc, open, high, low, close, volume, {selectMark}, {selectIndex}, best_bid_close, best_ask_close
from {safeTable}
where symbol = @symbol
  and (@before_utc is null or minute_utc < @before_utc)
order by minute_utc desc
limit @limit;
";
        var list = new List<CoincallChartMinuteRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol", (symbol ?? string.Empty).Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 129600));
        var beforeParam = new NpgsqlParameter("before_utc", NpgsqlTypes.NpgsqlDbType.TimestampTz);
        beforeParam.Value = beforeUtc.HasValue ? beforeUtc.Value : DBNull.Value;
        cmd.Parameters.Add(beforeParam);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CoincallChartMinuteRow
            {
                minuteUtc = reader.GetDateTime(0),
                open = reader.IsDBNull(1) ? null : reader.GetDecimal(1),
                high = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                low = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                close = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                volume = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                markPriceClose = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                indexPriceClose = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                bestBidClose = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                bestAskClose = reader.IsDBNull(9) ? null : reader.GetDecimal(9)
            });
        }
        list.Reverse();
        return list;
    }

    public async Task<List<CoincallStoredSymbolRow>> GetStoredFuturesSymbolsAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select symbol,
       coalesce(nullif(max(display_name), ''), symbol) as display_name
from public.van_coincall_futures_candle_minute
group by symbol
order by
    case
        when strpos(symbol, '-') = 0 then 0
        when split_part(symbol, '-', 2) ~ '^[0-9]{2}[A-Z]{3}[0-9]{2}$' then 1
        else 2
    end,
    case
        when split_part(symbol, '-', 2) ~ '^[0-9]{2}[A-Z]{3}[0-9]{2}$'
            then to_date(split_part(symbol, '-', 2), 'DDMONYY')
        else null
    end nulls last,
    symbol;
";
        var list = new List<CoincallStoredSymbolRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CoincallStoredSymbolRow
            {
                symbol = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                displayName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1)
            });
        }
        return list;
    }
}
