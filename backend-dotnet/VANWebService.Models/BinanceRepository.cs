using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Npgsql;
using VANWebService.Security;

namespace VANWebService.Models;

public sealed class BinanceFundingRow
{
    public string symbol { get; set; } = "";
    public string displayName { get; set; } = "";
    public DateTime minuteUtc { get; set; }
    public DateTime? fundingTimeUtc { get; set; }
    public DateTime? nextFundingTimeUtc { get; set; }
    public decimal? currentFunding { get; set; }
    public decimal? interest8h { get; set; }
    public decimal? markPrice { get; set; }
    public decimal? indexPrice { get; set; }
    public string source { get; set; } = "";
    public string rawJson { get; set; } = "{}";
    public DateTime createdAtUtc { get; set; }
    public DateTime updatedAtUtc { get; set; }
}

public sealed class BinanceEquityPoint
{
    public DateTime tsUtc { get; set; }
    public decimal totalEquity { get; set; }
    public decimal availableEquity { get; set; }
    public decimal unrealizedPnl { get; set; }
    public decimal? marginRatio { get; set; }
    public string equityCurrency { get; set; } = "USD";
}

public sealed class BinanceAccountRecord
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

public sealed class BinanceRepository
{
    private static readonly SemaphoreSlim SchemaLock = new SemaphoreSlim(1, 1);
    private static volatile bool SchemaReady;
    private readonly string _connectionString;
    private readonly Enums.LogAction _log;

    public BinanceRepository(string connectionString, Enums.LogAction log)
    {
        _connectionString = connectionString;
        _log = log;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (SchemaReady) return;
        await SchemaLock.WaitAsync(ct);
        try
        {
            if (SchemaReady) return;
        const string sql = @"
create table if not exists public.van_binance_account (
    id serial primary key,
    name text not null,
    api_key_cipher text not null,
    api_secret_cipher text not null,
    is_active boolean not null default false,
    store_minute_equity boolean not null default false,
    equity_currency text not null default 'USD',
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc')
);
create unique index if not exists van_binance_account_name_uq on public.van_binance_account ((lower(name)));
create unique index if not exists van_binance_account_single_active_idx on public.van_binance_account ((is_active)) where is_active = true;
alter table public.van_binance_account add column if not exists api_key_cipher text not null default '';
alter table public.van_binance_account add column if not exists api_secret_cipher text not null default '';
alter table public.van_binance_account add column if not exists store_minute_equity boolean not null default false;
alter table public.van_binance_account add column if not exists equity_currency text not null default 'USD';

create table if not exists public.van_binance_futures_funding_minute (
    symbol text not null,
    display_name text not null default '',
    minute_utc timestamptz not null,
    funding_time_utc timestamptz null,
    next_funding_time_utc timestamptz null,
    current_funding numeric(38,18) null,
    interest_8h numeric(38,18) null,
    mark_price numeric(38,18) null,
    index_price numeric(38,18) null,
    source text not null default 'binance:/fapi/v1/premiumIndex',
    raw_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (symbol, minute_utc)
);
create index if not exists van_binance_futures_funding_minute_symbol_time_idx on public.van_binance_futures_funding_minute (symbol, minute_utc desc);

create table if not exists public.van_binance_equity_minute (
    account_id integer not null,
    account_name text not null,
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
create index if not exists van_binance_equity_minute_account_ts_idx on public.van_binance_equity_minute (account_id, minute_utc desc);

create table if not exists public.van_binance_equity_daily (
    account_id integer not null,
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
    primary key (account_id, day_utc)
);
create index if not exists van_binance_equity_daily_account_day_idx on public.van_binance_equity_daily (account_id, day_utc desc);
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await cmd.ExecuteNonQueryAsync(ct);
                SchemaReady = true;
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == "40P01" && attempt < 2)
            {
                await Task.Delay(100 * (attempt + 1), ct);
            }
        }
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task<List<BinanceAccountRecord>> GetAccountsAsync(bool includeSecrets, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select id, name, api_key_cipher, api_secret_cipher, is_active, store_minute_equity, equity_currency, created_at, updated_at
from public.van_binance_account
order by is_active desc, id;
";
        var list = new List<BinanceAccountRecord>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string apiKey = DecryptOrEmpty(reader.IsDBNull(2) ? "" : reader.GetString(2));
            string apiSecret = includeSecrets ? DecryptOrEmpty(reader.IsDBNull(3) ? "" : reader.GetString(3)) : "";
            list.Add(new BinanceAccountRecord
            {
                id = reader.GetInt32(0),
                name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                apiKey = apiKey,
                apiSecret = apiSecret,
                isActive = reader.GetBoolean(4),
                storeMinuteEquity = reader.GetBoolean(5),
                equityCurrency = NormalizeEquityCurrency(reader.IsDBNull(6) ? "USD" : reader.GetString(6)),
                createdAtUtc = reader.GetDateTime(7),
                updatedAtUtc = reader.GetDateTime(8)
            });
        }
        return list;
    }

    public async Task<BinanceAccountRecord?> GetAccountByIdAsync(int id, bool includeSecrets, CancellationToken ct = default)
    {
        var accounts = await GetAccountsAsync(includeSecrets, ct);
        return accounts.Find(a => a.id == id);
    }

    public async Task<int> CreateAccountAsync(string name, string apiKey, string apiSecret, bool setActive, bool storeMinuteEquity, string equityCurrency, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (setActive)
        {
            await using var clear = new NpgsqlCommand("update public.van_binance_account set is_active = false, updated_at = (now() at time zone 'utc');", conn, tx);
            await clear.ExecuteNonQueryAsync(ct);
        }
        const string sql = @"
insert into public.van_binance_account
    (name, api_key_cipher, api_secret_cipher, is_active, store_minute_equity, equity_currency, created_at, updated_at)
values
    (@name, @api_key_cipher, @api_secret_cipher, @is_active, @store_minute_equity, @equity_currency, (now() at time zone 'utc'), (now() at time zone 'utc'))
returning id;
";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("name", (name ?? string.Empty).Trim());
        cmd.Parameters.AddWithValue("api_key_cipher", ApiSecretCrypto.Encrypt((apiKey ?? string.Empty).Trim()));
        cmd.Parameters.AddWithValue("api_secret_cipher", ApiSecretCrypto.Encrypt((apiSecret ?? string.Empty).Trim()));
        cmd.Parameters.AddWithValue("is_active", setActive);
        cmd.Parameters.AddWithValue("store_minute_equity", storeMinuteEquity);
        cmd.Parameters.AddWithValue("equity_currency", NormalizeEquityCurrency(equityCurrency));
        int id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAccountSettingsAsync(int id, bool storeMinuteEquity, string equityCurrency, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
update public.van_binance_account
set store_minute_equity = @store_minute_equity,
    equity_currency = @equity_currency,
    updated_at = (now() at time zone 'utc')
where id = @id;
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
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
        await using (var clear = new NpgsqlCommand("update public.van_binance_account set is_active = false, updated_at = (now() at time zone 'utc');", conn, tx))
        {
            await clear.ExecuteNonQueryAsync(ct);
        }
        await using var cmd = new NpgsqlCommand("update public.van_binance_account set is_active = true, updated_at = (now() at time zone 'utc') where id = @id;", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        bool ok = await cmd.ExecuteNonQueryAsync(ct) > 0;
        await tx.CommitAsync(ct);
        return ok;
    }

    public async Task<bool> DeleteAccountAsync(int id, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("delete from public.van_binance_account where id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task UpsertFundingMinuteAsync(BinanceFundingRow row, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_binance_futures_funding_minute
    (symbol, display_name, minute_utc, funding_time_utc, next_funding_time_utc, current_funding, interest_8h, mark_price, index_price, source, raw_json, created_at, updated_at)
values
    (@symbol, @display_name, @minute_utc, @funding_time_utc, @next_funding_time_utc, @current_funding, @interest_8h, @mark_price, @index_price, @source, cast(@raw_json as jsonb), (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (symbol, minute_utc)
do update set display_name = excluded.display_name,
              funding_time_utc = excluded.funding_time_utc,
              next_funding_time_utc = excluded.next_funding_time_utc,
              current_funding = excluded.current_funding,
              interest_8h = excluded.interest_8h,
              mark_price = excluded.mark_price,
              index_price = excluded.index_price,
              source = excluded.source,
              raw_json = excluded.raw_json,
              updated_at = (now() at time zone 'utc');
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        BindFunding(cmd, row);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertEquityAsync(int accountId, string accountName, DateTime tsUtc, string equityCurrency, decimal totalEquity, decimal availableEquity, decimal unrealizedPnl, decimal? marginRatio, bool storeMinuteEquity, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        DateTime normalizedTs = tsUtc.Kind == DateTimeKind.Utc ? tsUtc : tsUtc.ToUniversalTime();
        DateTime minuteUtc = TruncateToMinuteUtc(normalizedTs);
        var dayUtc = DateOnly.FromDateTime(normalizedTs);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        const string dailySql = @"
insert into public.van_binance_equity_daily (account_id, account_name, day_utc, last_ts_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
values (@account_id, @account_name, @day_utc, @last_ts_utc, @equity_currency, @total_equity, @available_equity, @unrealized_pnl, @margin_ratio, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (account_id, day_utc)
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
            BindEquity(cmd, accountId, accountName, equityCurrency, totalEquity, availableEquity, unrealizedPnl, marginRatio);
            cmd.Parameters.AddWithValue("day_utc", dayUtc);
            cmd.Parameters.AddWithValue("last_ts_utc", minuteUtc);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (storeMinuteEquity)
        {
            const string minuteSql = @"
insert into public.van_binance_equity_minute (account_id, account_name, minute_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
values (@account_id, @account_name, @minute_utc, @equity_currency, @total_equity, @available_equity, @unrealized_pnl, @margin_ratio, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (account_id, minute_utc)
do update set account_name = excluded.account_name,
              equity_currency = excluded.equity_currency,
              total_equity = excluded.total_equity,
              available_equity = excluded.available_equity,
              unrealized_pnl = excluded.unrealized_pnl,
              margin_ratio = excluded.margin_ratio,
              updated_at = (now() at time zone 'utc');
";
            await using var cmd = new NpgsqlCommand(minuteSql, conn, tx);
            BindEquity(cmd, accountId, accountName, equityCurrency, totalEquity, availableEquity, unrealizedPnl, marginRatio);
            cmd.Parameters.AddWithValue("minute_utc", minuteUtc);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public Task<List<BinanceEquityPoint>> GetDailyEquityAsync(int accountId, int days, CancellationToken ct = default)
    {
        const string sql = @"
select last_ts_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_binance_equity_daily
where account_id = @account_id
  and day_utc >= (((now() at time zone 'utc')::date) - @days)
order by day_utc;
";
        return ReadEquityPointsAsync(sql, accountId, cmd => cmd.Parameters.AddWithValue("days", Math.Max(1, days)), ct);
    }

    public Task<List<BinanceEquityPoint>> GetMinuteEquityAsync(int accountId, int hours, CancellationToken ct = default)
    {
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_binance_equity_minute
where account_id = @account_id
  and minute_utc >= ((now() at time zone 'utc') - make_interval(hours => @hours))
order by minute_utc;
";
        return ReadEquityPointsAsync(sql, accountId, cmd => cmd.Parameters.AddWithValue("hours", Math.Max(1, hours)), ct);
    }

    public Task<List<BinanceEquityPoint>> GetMinuteEquityForDailyDrawdownAsync(int accountId, int days, CancellationToken ct = default)
    {
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_binance_equity_minute
where account_id = @account_id
  and minute_utc >= (((now() at time zone 'utc')::date) - @days)
order by minute_utc;
";
        return ReadEquityPointsAsync(sql, accountId, cmd => cmd.Parameters.AddWithValue("days", Math.Max(1, days)), ct);
    }

    public async Task<List<BinanceFundingRow>> GetFundingMinuteAsync(string symbol, int limit, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select symbol, display_name, minute_utc, funding_time_utc, next_funding_time_utc, current_funding, interest_8h, mark_price, index_price, source, raw_json::text, created_at, updated_at
from public.van_binance_futures_funding_minute
where symbol = @symbol
order by minute_utc desc
limit @limit;
";
        var list = new List<BinanceFundingRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("symbol", NormalizeFundingSymbol(symbol));
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 129600));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new BinanceFundingRow
            {
                symbol = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                displayName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                minuteUtc = reader.GetDateTime(2),
                fundingTimeUtc = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                nextFundingTimeUtc = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                currentFunding = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                interest8h = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                markPrice = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                indexPrice = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                source = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                rawJson = reader.IsDBNull(10) ? "{}" : reader.GetString(10),
                createdAtUtc = reader.GetDateTime(11),
                updatedAtUtc = reader.GetDateTime(12)
            });
        }
        list.Reverse();
        return list;
    }

    private static void BindFunding(NpgsqlCommand cmd, BinanceFundingRow row)
    {
        string symbol = NormalizeFundingSymbol(row.symbol);
        cmd.Parameters.AddWithValue("symbol", symbol);
        cmd.Parameters.AddWithValue("display_name", string.IsNullOrWhiteSpace(row.displayName) ? DisplayName(symbol) : row.displayName);
        cmd.Parameters.AddWithValue("minute_utc", row.minuteUtc);
        cmd.Parameters.AddWithValue("funding_time_utc", row.fundingTimeUtc.HasValue ? row.fundingTimeUtc.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("next_funding_time_utc", row.nextFundingTimeUtc.HasValue ? row.nextFundingTimeUtc.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("current_funding", row.currentFunding.HasValue ? row.currentFunding.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("interest_8h", row.interest8h.HasValue ? row.interest8h.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("mark_price", row.markPrice.HasValue ? row.markPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("index_price", row.indexPrice.HasValue ? row.indexPrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("source", string.IsNullOrWhiteSpace(row.source) ? "binance:/fapi/v1/premiumIndex" : row.source);
        cmd.Parameters.AddWithValue("raw_json", string.IsNullOrWhiteSpace(row.rawJson) ? "{}" : row.rawJson);
    }

    private static void BindEquity(NpgsqlCommand cmd, int accountId, string accountName, string equityCurrency, decimal totalEquity, decimal availableEquity, decimal unrealizedPnl, decimal? marginRatio)
    {
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("account_name", accountName ?? string.Empty);
        cmd.Parameters.AddWithValue("equity_currency", NormalizeEquityCurrency(equityCurrency));
        cmd.Parameters.AddWithValue("total_equity", totalEquity);
        cmd.Parameters.AddWithValue("available_equity", availableEquity);
        cmd.Parameters.AddWithValue("unrealized_pnl", unrealizedPnl);
        cmd.Parameters.AddWithValue("margin_ratio", marginRatio.HasValue ? marginRatio.Value : DBNull.Value);
    }

    private async Task<List<BinanceEquityPoint>> ReadEquityPointsAsync(string sql, int accountId, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        var list = new List<BinanceEquityPoint>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("account_id", accountId);
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new BinanceEquityPoint
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

    public static string NormalizeFundingSymbol(string? symbol)
    {
        string normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant().Replace("-", "").Replace("_", "");
        if (normalized == "BTCUSD") return "BTCUSDT";
        if (normalized == "ETHUSD") return "ETHUSDT";
        return string.IsNullOrWhiteSpace(normalized) ? "BTCUSDT" : normalized;
    }

    public static string DisplayName(string symbol) => NormalizeFundingSymbol(symbol) switch
    {
        "BTCUSDT" => "BTCUSDT Perp",
        "ETHUSDT" => "ETHUSDT Perp",
        var s => s
    };

    public static DateTime TruncateToMinuteUtc(DateTime utc)
    {
        var normalized = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc);
        return new DateTime(normalized.Year, normalized.Month, normalized.Day, normalized.Hour, normalized.Minute, 0, DateTimeKind.Utc);
    }

    public static string NormalizeEquityCurrency(string? equityCurrency)
    {
        string normalized = (equityCurrency ?? "USD").Trim().ToUpperInvariant();
        return normalized == "BTC" ? "BTC" : "USD";
    }

    public static string MaskApiKey(string? apiKey)
    {
        string v = apiKey ?? string.Empty;
        if (v.Length <= 8) return new string('*', Math.Max(0, v.Length));
        return v[..4] + "..." + v[^4..];
    }

    private static string DecryptOrEmpty(string cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher)) return string.Empty;
        try { return ApiSecretCrypto.Decrypt(cipher); }
        catch { return string.Empty; }
    }
}
