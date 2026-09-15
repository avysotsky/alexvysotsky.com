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

public sealed class BybitAccountRecord
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string apiKey { get; set; } = "";
    public string apiSecret { get; set; } = "";
    public string passphrase { get; set; } = "";
    public bool isActive { get; set; }
    public bool isDemo { get; set; }
    public bool storeMinuteEquity { get; set; }
    public string equityCurrency { get; set; } = "USD";
    public DateTime createdAtUtc { get; set; }
    public DateTime updatedAtUtc { get; set; }
}

public sealed class BybitEquityPoint
{
    public DateTime tsUtc { get; set; }
    public decimal totalEquity { get; set; }
    public decimal availableEquity { get; set; }
    public decimal unrealizedPnl { get; set; }
    public decimal? marginRatio { get; set; }
    public string equityCurrency { get; set; } = "USD";
}

public sealed class BybitCashBalancePoint
{
    public DateTime tsUtc { get; set; }
    public string currency { get; set; } = "";
    public decimal cashBalance { get; set; }
}

public sealed class BybitQuoteMinuteRow
{
    public string exchange { get; set; } = "Bybit";
    public string market { get; set; } = "";
    public string category { get; set; } = "";
    public string symbol { get; set; } = "";
    public string instrumentName { get; set; } = "";
    public string displayName { get; set; } = "";
    public string tickerId { get; set; } = "";
    public string baseCurrency { get; set; } = "";
    public string quoteCurrency { get; set; } = "";
    public string productType { get; set; } = "";
    public DateTime minuteUtc { get; set; }
    public decimal? last { get; set; }
    public decimal? mark { get; set; }
    public decimal? bid { get; set; }
    public decimal? ask { get; set; }
    public decimal? index { get; set; }
    public decimal? open { get; set; }
    public decimal? high { get; set; }
    public decimal? low { get; set; }
    public decimal? close { get; set; }
    public decimal? volume { get; set; }
    public decimal? volumeUsd { get; set; }
    public decimal? quoteVolume { get; set; }
    public decimal? currentFunding { get; set; }
    public decimal? interest8h { get; set; }
    public string rawJson { get; set; } = "{}";
}

public sealed class BybitRepository
{
    private readonly string _connectionString;
    private readonly Enums.LogAction _log;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaEnsured;

    public BybitRepository(string connectionString, Enums.LogAction log)
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
create table if not exists public.van_bybit_account (
    id serial primary key,
    name text not null,
    api_key text not null default '',
    api_key_cipher text not null default '',
    api_secret_cipher text not null,
    passphrase_cipher text not null default '',
    is_active boolean not null default false,
    is_demo boolean not null default false,
    store_minute_equity boolean not null default false,
    equity_currency text not null default 'USD',
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc')
);
alter table public.van_bybit_account add column if not exists api_key text not null default '';
alter table public.van_bybit_account add column if not exists api_key_cipher text not null default '';
alter table public.van_bybit_account add column if not exists api_secret_cipher text not null default '';
alter table public.van_bybit_account add column if not exists passphrase_cipher text not null default '';
alter table public.van_bybit_account add column if not exists store_minute_equity boolean not null default false;
alter table public.van_bybit_account add column if not exists equity_currency text not null default 'USD';
alter table public.van_bybit_account drop constraint if exists van_bybit_account_equity_currency_chk;
alter table public.van_bybit_account add constraint van_bybit_account_equity_currency_chk check (equity_currency in ('USD','BTC'));
create unique index if not exists van_bybit_account_name_uq on public.van_bybit_account ((lower(name)));
create unique index if not exists van_bybit_account_single_active_idx on public.van_bybit_account ((is_active)) where is_active = true;

create table if not exists public.van_bybit_equity_minute (
    account_uid text not null,
    account_name text not null,
    minute_utc timestamptz not null,
    equity_currency text not null,
    total_equity numeric(38,10) not null,
    available_equity numeric(38,10) not null,
    unrealized_pnl numeric(38,10) not null,
    margin_ratio numeric(38,10) null,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (account_uid, minute_utc)
);
create index if not exists van_bybit_equity_minute_account_ts_idx on public.van_bybit_equity_minute (account_uid, minute_utc desc);

create table if not exists public.van_bybit_equity_daily (
    account_uid text not null,
    account_name text not null,
    day_utc date not null,
    last_ts_utc timestamptz not null,
    equity_currency text not null,
    total_equity numeric(38,10) not null,
    available_equity numeric(38,10) not null,
    unrealized_pnl numeric(38,10) not null,
    margin_ratio numeric(38,10) null,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (account_uid, day_utc)
);
create index if not exists van_bybit_equity_daily_account_day_idx on public.van_bybit_equity_daily (account_uid, day_utc desc);

create table if not exists public.van_bybit_cash_balance_daily (
    account_uid text not null,
    account_name text not null,
    day_utc date not null,
    currency text not null,
    last_ts_utc timestamptz not null,
    cash_balance numeric(38,10) not null,
    source text not null default 'bybit_transaction_log',
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (account_uid, day_utc, currency)
);
create index if not exists van_bybit_cash_balance_daily_account_day_idx on public.van_bybit_cash_balance_daily (account_uid, day_utc desc, currency);

create table if not exists public.van_bybit_quote_minute (
    exchange text not null default 'Bybit',
    market text not null,
    category text not null,
    symbol text not null,
    instrument_name text not null,
    display_name text not null default '',
    ticker_id text not null default '',
    base_currency text not null,
    quote_currency text not null,
    product_type text not null default '',
    minute_utc timestamptz not null,
    last_price numeric(38,10) null,
    mark_price numeric(38,10) null,
    bid_price numeric(38,10) null,
    ask_price numeric(38,10) null,
    index_price numeric(38,10) null,
    open numeric(38,10) null,
    high numeric(38,10) null,
    low numeric(38,10) null,
    close numeric(38,10) null,
    volume numeric(38,10) null,
    volume_usd numeric(38,10) null,
    quote_volume numeric(38,10) null,
    current_funding numeric(38,18) null,
    interest_8h numeric(38,18) null,
    raw_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (market, category, symbol, minute_utc)
);
create index if not exists van_bybit_quote_minute_ts_idx on public.van_bybit_quote_minute (minute_utc desc, market, symbol);
create index if not exists van_bybit_quote_minute_symbol_ts_idx on public.van_bybit_quote_minute (symbol, minute_utc desc);
alter table public.van_bybit_quote_minute add column if not exists exchange text not null default 'Bybit';
alter table public.van_bybit_quote_minute add column if not exists display_name text not null default '';
alter table public.van_bybit_quote_minute add column if not exists ticker_id text not null default '';
alter table public.van_bybit_quote_minute add column if not exists product_type text not null default '';
alter table public.van_bybit_quote_minute add column if not exists volume_usd numeric(38,10) null;
alter table public.van_bybit_quote_minute add column if not exists current_funding numeric(38,18) null;
alter table public.van_bybit_quote_minute add column if not exists interest_8h numeric(38,18) null;
alter table public.van_bybit_quote_minute add column if not exists raw_json jsonb not null default '{}'::jsonb;
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


    public async Task<List<BybitAccountRecord>> GetAccountsAsync(bool includeSecrets, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"select id, name, api_key_cipher, api_secret_cipher, passphrase_cipher, is_active, is_demo, store_minute_equity, equity_currency, created_at, updated_at from public.van_bybit_account order by is_active desc, lower(name), id;";
        var list = new List<BybitAccountRecord>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new BybitAccountRecord { id=r.GetInt32(0), name=r.GetString(1), apiKey=includeSecrets?ApiSecretCrypto.Decrypt(r.GetString(2)):"", apiSecret=includeSecrets?ApiSecretCrypto.Decrypt(r.GetString(3)):"", passphrase=includeSecrets && !r.IsDBNull(4) && !string.IsNullOrWhiteSpace(r.GetString(4)) ? ApiSecretCrypto.Decrypt(r.GetString(4)) : "", isActive=r.GetBoolean(5), isDemo=r.GetBoolean(6), storeMinuteEquity=r.GetBoolean(7), equityCurrency=NormalizeEquityCurrency(r.GetString(8)), createdAtUtc=r.GetDateTime(9), updatedAtUtc=r.GetDateTime(10) });
        }
        return list;
    }

    public async Task<BybitAccountRecord?> GetAccountByIdAsync(int id, CancellationToken ct = default)
    {
        var accounts = await GetAccountsAsync(true, ct);
        return accounts.FirstOrDefault(a => a.id == id);
    }

    public async Task<int> CreateAccountAsync(string name, string apiKey, string apiSecret, string passphrase, bool isDemo, bool setActive, bool storeMinuteEquity, string equityCurrency, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (setActive)
        {
            await using var clear = new NpgsqlCommand("update public.van_bybit_account set is_active=false, updated_at=(now() at time zone 'utc') where is_active=true;", conn, tx);
            await clear.ExecuteNonQueryAsync(ct);
        }
        const string sql = @"insert into public.van_bybit_account (name, api_key, api_key_cipher, api_secret_cipher, passphrase_cipher, is_active, is_demo, store_minute_equity, equity_currency, created_at, updated_at) values (@name, '', @api_key_cipher, @api_secret_cipher, @passphrase_cipher, @is_active, @is_demo, @store_minute_equity, @equity_currency, (now() at time zone 'utc'), (now() at time zone 'utc')) returning id;";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("name", name.Trim());
        cmd.Parameters.AddWithValue("api_key_cipher", ApiSecretCrypto.Encrypt(apiKey.Trim()));
        cmd.Parameters.AddWithValue("api_secret_cipher", ApiSecretCrypto.Encrypt(apiSecret.Trim()));
        cmd.Parameters.AddWithValue("passphrase_cipher", string.IsNullOrWhiteSpace(passphrase) ? string.Empty : ApiSecretCrypto.Encrypt(passphrase.Trim()));
        cmd.Parameters.AddWithValue("is_active", setActive);
        cmd.Parameters.AddWithValue("is_demo", isDemo);
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
        await using var cmd = new NpgsqlCommand("update public.van_bybit_account set store_minute_equity=@store_minute_equity, equity_currency=@equity_currency, updated_at=(now() at time zone 'utc') where id=@id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("store_minute_equity", storeMinuteEquity);
        cmd.Parameters.AddWithValue("equity_currency", NormalizeEquityCurrency(equityCurrency));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> SetActiveAccountAsync(int id, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var clear = new NpgsqlCommand("update public.van_bybit_account set is_active=false, updated_at=(now() at time zone 'utc') where is_active=true;", conn, tx)) await clear.ExecuteNonQueryAsync(ct);
        await using var cmd = new NpgsqlCommand("update public.van_bybit_account set is_active=true, updated_at=(now() at time zone 'utc') where id=@id;", conn, tx);
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
        await using var cmd = new NpgsqlCommand("delete from public.van_bybit_account where id=@id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task UpsertEquityAsync(string accountUid, string accountName, DateTime tsUtc, string equityCurrency, decimal totalEquity, decimal availableEquity, decimal unrealizedPnl, decimal? marginRatio, bool storeMinuteEquity, CancellationToken ct = default)
    {
        DateTime normalizedTs = tsUtc.Kind == DateTimeKind.Utc ? tsUtc : tsUtc.ToUniversalTime();
        DateTime minuteUtc = new DateTime(normalizedTs.Year, normalizedTs.Month, normalizedTs.Day, normalizedTs.Hour, normalizedTs.Minute, 0, DateTimeKind.Utc);
        var dayUtc = DateOnly.FromDateTime(normalizedTs);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string dailySql = @"
insert into public.van_bybit_equity_daily (account_uid, account_name, day_utc, last_ts_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
values (@account_uid, @account_name, @day_utc, @last_ts_utc, @equity_currency, @total_equity, @available_equity, @unrealized_pnl, @margin_ratio, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (account_uid, day_utc)
do update set account_name = excluded.account_name,
              last_ts_utc = excluded.last_ts_utc,
              equity_currency = excluded.equity_currency,
              total_equity = excluded.total_equity,
              available_equity = excluded.available_equity,
              unrealized_pnl = excluded.unrealized_pnl,
              margin_ratio = excluded.margin_ratio,
              updated_at = (now() at time zone 'utc');
";
        await using (var cmd = new NpgsqlCommand(dailySql, conn, tx))
        {
            BindEquity(cmd, accountUid, accountName, equityCurrency, totalEquity, availableEquity, unrealizedPnl, marginRatio);
            cmd.Parameters.AddWithValue("day_utc", dayUtc);
            cmd.Parameters.AddWithValue("last_ts_utc", minuteUtc);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (storeMinuteEquity)
        {
            const string minuteSql = @"
insert into public.van_bybit_equity_minute (account_uid, account_name, minute_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
values (@account_uid, @account_name, @minute_utc, @equity_currency, @total_equity, @available_equity, @unrealized_pnl, @margin_ratio, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (account_uid, minute_utc)
do update set account_name = excluded.account_name,
              equity_currency = excluded.equity_currency,
              total_equity = excluded.total_equity,
              available_equity = excluded.available_equity,
              unrealized_pnl = excluded.unrealized_pnl,
              margin_ratio = excluded.margin_ratio,
              updated_at = (now() at time zone 'utc');
";
            await using var cmd = new NpgsqlCommand(minuteSql, conn, tx);
            BindEquity(cmd, accountUid, accountName, equityCurrency, totalEquity, availableEquity, unrealizedPnl, marginRatio);
            cmd.Parameters.AddWithValue("minute_utc", minuteUtc);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task UpsertQuoteMinuteAsync(BybitQuoteMinuteRow row, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_bybit_quote_minute (
    exchange, market, category, symbol, instrument_name, display_name, ticker_id, base_currency, quote_currency, product_type, minute_utc,
    last_price, mark_price, bid_price, ask_price, index_price, open, high, low, close, volume, volume_usd, quote_volume, current_funding, interest_8h, raw_json,
    created_at, updated_at)
values (
    @exchange, @market, @category, @symbol, @instrument_name, @display_name, @ticker_id, @base_currency, @quote_currency, @product_type, @minute_utc,
    @last_price, @mark_price, @bid_price, @ask_price, @index_price, @open, @high, @low, @close, @volume, @volume_usd, @quote_volume, @current_funding, @interest_8h, cast(@raw_json as jsonb),
    (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (market, category, symbol, minute_utc)
do update set exchange = excluded.exchange,
              instrument_name = excluded.instrument_name,
              display_name = excluded.display_name,
              ticker_id = excluded.ticker_id,
              base_currency = excluded.base_currency,
              quote_currency = excluded.quote_currency,
              product_type = excluded.product_type,
              last_price = coalesce(excluded.last_price, public.van_bybit_quote_minute.last_price),
              mark_price = coalesce(excluded.mark_price, public.van_bybit_quote_minute.mark_price),
              bid_price = coalesce(excluded.bid_price, public.van_bybit_quote_minute.bid_price),
              ask_price = coalesce(excluded.ask_price, public.van_bybit_quote_minute.ask_price),
              index_price = coalesce(excluded.index_price, public.van_bybit_quote_minute.index_price),
              open = coalesce(excluded.open, public.van_bybit_quote_minute.open),
              high = coalesce(excluded.high, public.van_bybit_quote_minute.high),
              low = coalesce(excluded.low, public.van_bybit_quote_minute.low),
              close = coalesce(excluded.close, public.van_bybit_quote_minute.close),
              volume = coalesce(excluded.volume, public.van_bybit_quote_minute.volume),
              volume_usd = coalesce(excluded.volume_usd, public.van_bybit_quote_minute.volume_usd),
              quote_volume = coalesce(excluded.quote_volume, public.van_bybit_quote_minute.quote_volume),
              current_funding = coalesce(excluded.current_funding, public.van_bybit_quote_minute.current_funding),
              interest_8h = coalesce(excluded.interest_8h, public.van_bybit_quote_minute.interest_8h),
              raw_json = excluded.raw_json,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        BindQuoteMinute(cmd, row);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void BindQuoteMinute(NpgsqlCommand cmd, BybitQuoteMinuteRow row)
    {
        cmd.Parameters.AddWithValue("exchange", string.IsNullOrWhiteSpace(row.exchange) ? "Bybit" : row.exchange.Trim());
        cmd.Parameters.AddWithValue("market", row.market.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("category", row.category.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("symbol", row.symbol.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("instrument_name", string.IsNullOrWhiteSpace(row.instrumentName) ? row.symbol.Trim().ToUpperInvariant() : row.instrumentName.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("display_name", row.displayName ?? string.Empty);
        cmd.Parameters.AddWithValue("ticker_id", string.IsNullOrWhiteSpace(row.tickerId) ? row.symbol.Trim().ToUpperInvariant() : row.tickerId.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("base_currency", row.baseCurrency.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("quote_currency", row.quoteCurrency.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("product_type", row.productType ?? string.Empty);
        cmd.Parameters.AddWithValue("minute_utc", TruncateToMinuteUtc(row.minuteUtc));
        AddNullableNumeric(cmd, "last_price", row.last);
        AddNullableNumeric(cmd, "mark_price", row.mark);
        AddNullableNumeric(cmd, "bid_price", row.bid);
        AddNullableNumeric(cmd, "ask_price", row.ask);
        AddNullableNumeric(cmd, "index_price", row.index);
        AddNullableNumeric(cmd, "open", row.open);
        AddNullableNumeric(cmd, "high", row.high);
        AddNullableNumeric(cmd, "low", row.low);
        AddNullableNumeric(cmd, "close", row.close);
        AddNullableNumeric(cmd, "volume", row.volume);
        AddNullableNumeric(cmd, "volume_usd", row.volumeUsd);
        AddNullableNumeric(cmd, "quote_volume", row.quoteVolume);
        AddNullableNumeric(cmd, "current_funding", row.currentFunding);
        AddNullableNumeric(cmd, "interest_8h", row.interest8h);
        cmd.Parameters.AddWithValue("raw_json", string.IsNullOrWhiteSpace(row.rawJson) ? "{}" : row.rawJson);
    }

    private static void AddNullableNumeric(NpgsqlCommand cmd, string name, decimal? value)
    {
        var p = new NpgsqlParameter(name, NpgsqlDbType.Numeric);
        p.Value = value.HasValue ? value.Value : DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static void BindEquity(NpgsqlCommand cmd, string accountUid, string accountName, string equityCurrency, decimal totalEquity, decimal availableEquity, decimal unrealizedPnl, decimal? marginRatio)
    {
        cmd.Parameters.AddWithValue("account_uid", accountUid);
        cmd.Parameters.AddWithValue("account_name", accountName);
        cmd.Parameters.AddWithValue("equity_currency", NormalizeEquityCurrency(equityCurrency));
        cmd.Parameters.AddWithValue("total_equity", totalEquity);
        cmd.Parameters.AddWithValue("available_equity", availableEquity);
        cmd.Parameters.AddWithValue("unrealized_pnl", unrealizedPnl);
        cmd.Parameters.AddWithValue("margin_ratio", marginRatio.HasValue ? marginRatio.Value : DBNull.Value);
    }

    public Task<List<BybitEquityPoint>> GetDailyEquityAsync(string accountUid, int days, CancellationToken ct = default)
    {
        const string sql = @"
select last_ts_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_bybit_equity_daily
where account_uid = @account_uid
  and day_utc >= (((now() at time zone 'utc')::date) - @days)
order by day_utc;
";
        return ReadPointsAsync(sql, accountUid, cmd => cmd.Parameters.AddWithValue("days", Math.Max(1, days)), ct);
    }

    public Task<List<BybitEquityPoint>> GetMinuteEquityAsync(string accountUid, int hours, CancellationToken ct = default)
    {
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_bybit_equity_minute
where account_uid = @account_uid
  and minute_utc >= ((now() at time zone 'utc') - make_interval(hours => @hours))
order by minute_utc;
";
        return ReadPointsAsync(sql, accountUid, cmd => cmd.Parameters.AddWithValue("hours", Math.Max(1, hours)), ct);
    }

    public Task<List<BybitEquityPoint>> GetMinuteEquityForDailyDrawdownAsync(string accountUid, int days, CancellationToken ct = default)
    {
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_bybit_equity_minute
where account_uid = @account_uid
  and minute_utc >= (((now() at time zone 'utc')::date) - @days)
order by minute_utc;
";
        return ReadPointsAsync(sql, accountUid, cmd => cmd.Parameters.AddWithValue("days", Math.Max(1, days)), ct);
    }

    private async Task<List<BybitEquityPoint>> ReadPointsAsync(string sql, string accountUid, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        var list = new List<BybitEquityPoint>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("account_uid", accountUid);
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new BybitEquityPoint
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


    public Task<List<BybitCashBalancePoint>> GetDailyCashBalanceAsync(string accountUid, int days, CancellationToken ct = default)
    {
        const string sql = @"
select last_ts_utc, currency, cash_balance
from public.van_bybit_cash_balance_daily
where account_uid = @account_uid
  and day_utc >= (((now() at time zone 'utc')::date) - @days)
order by day_utc, currency;
";
        return ReadCashBalancePointsAsync(sql, accountUid, cmd => cmd.Parameters.AddWithValue("days", Math.Max(1, days)), ct);
    }

    private async Task<List<BybitCashBalancePoint>> ReadCashBalancePointsAsync(string sql, string accountUid, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        var list = new List<BybitCashBalancePoint>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("account_uid", accountUid);
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new BybitCashBalancePoint
            {
                tsUtc = reader.GetDateTime(0),
                currency = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                cashBalance = reader.GetDecimal(2)
            });
        }
        return list;
    }


    public async Task<List<BybitQuoteMinuteRow>> GetFundingMinuteAsync(string symbol, int limit, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select exchange, market, category, symbol, instrument_name, display_name, ticker_id, base_currency, quote_currency, product_type, minute_utc,
       last_price, mark_price, bid_price, ask_price, index_price, open, high, low, close, volume, volume_usd, quote_volume, current_funding, interest_8h, raw_json::text
from public.van_bybit_quote_minute
where market = 'futures'
  and symbol = @symbol
  and (current_funding is not null or interest_8h is not null)
order by minute_utc desc
limit @limit;
";
        var list = new List<BybitQuoteMinuteRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol", (symbol ?? string.Empty).Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 10000));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new BybitQuoteMinuteRow
            {
                exchange = reader.IsDBNull(0) ? "Bybit" : reader.GetString(0),
                market = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                category = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                symbol = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                instrumentName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                displayName = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                tickerId = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                baseCurrency = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                quoteCurrency = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                productType = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                minuteUtc = reader.GetDateTime(10),
                last = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                mark = reader.IsDBNull(12) ? null : reader.GetDecimal(12),
                bid = reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                ask = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
                index = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
                open = reader.IsDBNull(16) ? null : reader.GetDecimal(16),
                high = reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                low = reader.IsDBNull(18) ? null : reader.GetDecimal(18),
                close = reader.IsDBNull(19) ? null : reader.GetDecimal(19),
                volume = reader.IsDBNull(20) ? null : reader.GetDecimal(20),
                volumeUsd = reader.IsDBNull(21) ? null : reader.GetDecimal(21),
                quoteVolume = reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                currentFunding = reader.IsDBNull(23) ? null : reader.GetDecimal(23),
                interest8h = reader.IsDBNull(24) ? null : reader.GetDecimal(24),
                rawJson = reader.IsDBNull(25) ? "{}" : reader.GetString(25)
            });
        }
        list.Reverse();
        return list;
    }

    public static string BuildAccountUid(string? accountId, string? apiKey, bool isDemo)
    {
        string raw = !string.IsNullOrWhiteSpace(accountId) ? accountId!.Trim() : (apiKey ?? string.Empty).Trim() + "|" + isDemo.ToString();
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    public static DateTime TruncateToMinuteUtc(DateTime utc)
    {
        var normalized = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc);
        return new DateTime(normalized.Year, normalized.Month, normalized.Day, normalized.Hour, normalized.Minute, 0, DateTimeKind.Utc);
    }

    public static string NormalizeEquityCurrency(string? value)
    {
        string v = (value ?? "USD").Trim().ToUpperInvariant();
        return v == "BTC" ? "BTC" : "USD";
    }
}
