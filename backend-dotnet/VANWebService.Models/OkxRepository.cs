using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Npgsql;
using VANWebService.Security;

namespace VANWebService.Models;

public sealed class OkxAccountRecord
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

public sealed class OkxSnapshotPoint
{
    public DateTime tsUtc { get; set; }
    public decimal totalEquityUsdt { get; set; }
    public decimal availableEquityUsdt { get; set; }
    public decimal unrealizedPnlUsdt { get; set; }
    public decimal? marginRatio { get; set; }
}

public sealed class OkxEquityPoint
{
    public DateTime tsUtc { get; set; }
    public decimal totalEquity { get; set; }
    public decimal availableEquity { get; set; }
    public decimal unrealizedPnl { get; set; }
    public decimal? marginRatio { get; set; }
    public string equityCurrency { get; set; } = "USD";
}

public sealed class OkxFundingRow
{
    public string instId { get; set; } = "";
    public string displayName { get; set; } = "";
    public DateTime minuteUtc { get; set; }
    public DateTime? fundingTimeUtc { get; set; }
    public DateTime? nextFundingTimeUtc { get; set; }
    public decimal? currentFunding { get; set; }
    public decimal? interest8h { get; set; }
    public decimal? settledFunding { get; set; }
    public string settState { get; set; } = "";
    public string source { get; set; } = "";
    public string rawJson { get; set; } = "{}";
    public DateTime createdAtUtc { get; set; }
    public DateTime updatedAtUtc { get; set; }
}

public sealed class OkxRepository
{
    private readonly string _connectionString;
    private readonly Enums.LogAction _log;

    public OkxRepository(string connectionString, Enums.LogAction log)
    {
        _connectionString = connectionString;
        _log = log;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        const string sql = @"
create table if not exists public.van_okx_account (
    id serial primary key,
    name text not null,
    api_key text not null default '',
    api_key_cipher text not null default '',
    api_secret_cipher text not null,
    passphrase_cipher text not null,
    is_active boolean not null default false,
    is_demo boolean not null default false,
    store_minute_equity boolean not null default false,
    equity_currency text not null default 'USD',
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc')
);

alter table public.van_okx_account add column if not exists api_key_cipher text not null default '';
alter table public.van_okx_account alter column api_key set default '';
alter table public.van_okx_account add column if not exists store_minute_equity boolean;
alter table public.van_okx_account add column if not exists equity_currency text;
update public.van_okx_account
set store_minute_equity = coalesce(store_minute_equity, store_minute_pnl, false)
where store_minute_equity is null;
update public.van_okx_account
set equity_currency = upper(coalesce(nullif(trim(equity_currency), ''), nullif(trim(pnl_currency), ''), 'USD'))
where equity_currency is null or btrim(equity_currency) = '';
alter table public.van_okx_account alter column store_minute_equity set default false;
alter table public.van_okx_account alter column store_minute_equity set not null;
alter table public.van_okx_account alter column equity_currency set default 'USD';
alter table public.van_okx_account alter column equity_currency set not null;
alter table public.van_okx_account drop constraint if exists van_okx_account_pnl_currency_chk;
alter table public.van_okx_account drop constraint if exists van_okx_account_equity_currency_chk;
alter table public.van_okx_account add constraint van_okx_account_equity_currency_chk check (equity_currency in ('USD','BTC'));

create unique index if not exists van_okx_account_name_uq on public.van_okx_account ((lower(name)));
create unique index if not exists van_okx_account_single_active_idx on public.van_okx_account ((is_active)) where is_active = true;

create table if not exists public.van_okx_equity_snapshot (
    id bigserial primary key,
    account_id integer not null references public.van_okx_account(id) on delete cascade,
    ts_utc timestamptz not null default (now() at time zone 'utc'),
    total_equity_usdt numeric(38,10) not null,
    available_equity_usdt numeric(38,10) not null,
    unrealized_pnl_usdt numeric(38,10) not null,
    margin_ratio numeric(38,10) null
);

create index if not exists van_okx_equity_snapshot_account_ts_idx on public.van_okx_equity_snapshot (account_id, ts_utc desc);

create table if not exists public.van_okx_equity_minute (
    account_id integer not null references public.van_okx_account(id) on delete cascade,
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
create index if not exists van_okx_equity_minute_account_ts_idx on public.van_okx_equity_minute (account_id, minute_utc desc);

create table if not exists public.van_okx_equity_daily (
    account_id integer not null references public.van_okx_account(id) on delete cascade,
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
create index if not exists van_okx_equity_daily_account_day_idx on public.van_okx_equity_daily (account_id, day_utc desc);

create table if not exists public.van_okx_futures_funding_minute (
    inst_id text not null,
    display_name text not null default '',
    minute_utc timestamptz not null,
    funding_time_utc timestamptz null,
    next_funding_time_utc timestamptz null,
    current_funding numeric(38,18) null,
    interest_8h numeric(38,18) null,
    settled_funding numeric(38,18) null,
    sett_state text not null default '',
    source text not null default 'okx:/api/v5/public/funding-rate',
    raw_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (inst_id, minute_utc)
);
create index if not exists van_okx_futures_funding_minute_inst_time_idx on public.van_okx_futures_funding_minute (inst_id, minute_utc desc);
alter table public.van_okx_futures_funding_minute add column if not exists display_name text not null default '';
alter table public.van_okx_futures_funding_minute add column if not exists funding_time_utc timestamptz null;
alter table public.van_okx_futures_funding_minute add column if not exists next_funding_time_utc timestamptz null;
alter table public.van_okx_futures_funding_minute add column if not exists current_funding numeric(38,18) null;
alter table public.van_okx_futures_funding_minute add column if not exists interest_8h numeric(38,18) null;
alter table public.van_okx_futures_funding_minute add column if not exists settled_funding numeric(38,18) null;
alter table public.van_okx_futures_funding_minute add column if not exists sett_state text not null default '';
alter table public.van_okx_futures_funding_minute add column if not exists source text not null default 'okx:/api/v5/public/funding-rate';
alter table public.van_okx_futures_funding_minute add column if not exists raw_json jsonb not null default '{}'::jsonb;

insert into public.van_okx_equity_minute (account_id, minute_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
select account_id, minute_utc, upper(coalesce(nullif(trim(pnl_currency), ''), 'USD')), total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at
from public.van_okx_pnl_minute
on conflict (account_id, minute_utc) do nothing;

insert into public.van_okx_equity_daily (account_id, day_utc, last_ts_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
select account_id, day_utc, last_ts_utc, upper(coalesce(nullif(trim(pnl_currency), ''), 'USD')), total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at
from public.van_okx_pnl_daily
on conflict (account_id, day_utc) do nothing;
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await BackfillApiKeyCipherAsync(conn, ct);
    }

    private static async Task BackfillApiKeyCipherAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string selectSql = @"
select id, api_key, api_key_cipher
from public.van_okx_account
where coalesce(api_key, '') <> '';
";
        var rows = new List<(int id, string apiKey, string apiKeyCipher)>();
        await using (var cmd = new NpgsqlCommand(selectSql, conn))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                rows.Add((
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2)
                ));
            }
        }

        foreach (var row in rows)
        {
            string plainApiKey = "";
            if (!string.IsNullOrWhiteSpace(row.apiKeyCipher))
            {
                try
                {
                    plainApiKey = ApiSecretCrypto.Decrypt(row.apiKeyCipher);
                }
                catch
                {
                    plainApiKey = row.apiKey;
                }
            }
            else
            {
                plainApiKey = row.apiKey;
            }

            if (string.IsNullOrWhiteSpace(plainApiKey)) continue;

            string encrypted = ApiSecretCrypto.Encrypt(plainApiKey.Trim());
            const string updateSql = @"
update public.van_okx_account
set api_key_cipher = @api_key_cipher,
    api_key = '',
    updated_at = (now() at time zone 'utc')
where id = @id;
";
            await using var updateCmd = new NpgsqlCommand(updateSql, conn);
            updateCmd.Parameters.AddWithValue("id", row.id);
            updateCmd.Parameters.AddWithValue("api_key_cipher", encrypted);
            await updateCmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<List<OkxAccountRecord>> GetAccountsAsync(bool includeSecrets, CancellationToken ct = default)
    {
        const string sql = @"
select id, name, api_key, api_key_cipher, api_secret_cipher, passphrase_cipher, is_active, is_demo, store_minute_equity, equity_currency, created_at, updated_at
from public.van_okx_account
order by is_active desc, lower(name), id;
";
        var list = new List<OkxAccountRecord>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string apiKey = ReadApiKey(reader, 2, 3);
            list.Add(new OkxAccountRecord
            {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                apiKey = apiKey,
                apiSecret = includeSecrets ? ApiSecretCrypto.Decrypt(reader.GetString(4)) : "",
                passphrase = includeSecrets ? ApiSecretCrypto.Decrypt(reader.GetString(5)) : "",
                isActive = reader.GetBoolean(6),
                isDemo = reader.GetBoolean(7),
                storeMinuteEquity = reader.GetBoolean(8),
                equityCurrency = NormalizeEquityCurrency(reader.IsDBNull(9) ? "USD" : reader.GetString(9)),
                createdAtUtc = reader.GetDateTime(10),
                updatedAtUtc = reader.GetDateTime(11)
            });
        }
        return list;
    }

    public async Task<OkxAccountRecord?> GetActiveAccountAsync(CancellationToken ct = default)
    {
        const string sql = @"
select id, name, api_key, api_key_cipher, api_secret_cipher, passphrase_cipher, is_active, is_demo, store_minute_equity, equity_currency, created_at, updated_at
from public.van_okx_account
where is_active = true
limit 1;
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new OkxAccountRecord
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            apiKey = ReadApiKey(reader, 2, 3),
            apiSecret = ApiSecretCrypto.Decrypt(reader.GetString(4)),
            passphrase = ApiSecretCrypto.Decrypt(reader.GetString(5)),
            isActive = reader.GetBoolean(6),
            isDemo = reader.GetBoolean(7),
            storeMinuteEquity = reader.GetBoolean(8),
            equityCurrency = NormalizeEquityCurrency(reader.IsDBNull(9) ? "USD" : reader.GetString(9)),
            createdAtUtc = reader.GetDateTime(10),
            updatedAtUtc = reader.GetDateTime(11)
        };
    }

    public async Task<OkxAccountRecord?> GetAccountByIdAsync(int id, CancellationToken ct = default)
    {
        const string sql = @"
select id, name, api_key, api_key_cipher, api_secret_cipher, passphrase_cipher, is_active, is_demo, store_minute_equity, equity_currency, created_at, updated_at
from public.van_okx_account
where id = @id
limit 1;
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new OkxAccountRecord
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            apiKey = ReadApiKey(reader, 2, 3),
            apiSecret = ApiSecretCrypto.Decrypt(reader.GetString(4)),
            passphrase = ApiSecretCrypto.Decrypt(reader.GetString(5)),
            isActive = reader.GetBoolean(6),
            isDemo = reader.GetBoolean(7),
            storeMinuteEquity = reader.GetBoolean(8),
            equityCurrency = NormalizeEquityCurrency(reader.IsDBNull(9) ? "USD" : reader.GetString(9)),
            createdAtUtc = reader.GetDateTime(10),
            updatedAtUtc = reader.GetDateTime(11)
        };
    }

    public async Task<int> CreateAccountAsync(string name, string apiKey, string apiSecret, string passphrase, bool isDemo, bool setActive, bool storeMinuteEquity, string equityCurrency, CancellationToken ct = default)
    {
        string apiKeyCipher = ApiSecretCrypto.Encrypt(apiKey.Trim());
        string secretCipher = ApiSecretCrypto.Encrypt(apiSecret.Trim());
        string passphraseCipher = ApiSecretCrypto.Encrypt(passphrase.Trim());
        string normalizedCurrency = NormalizeEquityCurrency(equityCurrency);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (setActive)
        {
            await using var clearCmd = new NpgsqlCommand("update public.van_okx_account set is_active = false, updated_at = (now() at time zone 'utc') where is_active = true;", conn, tx);
            await clearCmd.ExecuteNonQueryAsync(ct);
        }
        const string sql = @"
insert into public.van_okx_account (name, api_key, api_key_cipher, api_secret_cipher, passphrase_cipher, is_active, is_demo, store_minute_equity, equity_currency, created_at, updated_at)
values (@name, '', @api_key_cipher, @api_secret_cipher, @passphrase_cipher, @is_active, @is_demo, @store_minute_equity, @equity_currency, (now() at time zone 'utc'), (now() at time zone 'utc'))
returning id;
";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("name", name.Trim());
        cmd.Parameters.AddWithValue("api_key_cipher", apiKeyCipher);
        cmd.Parameters.AddWithValue("api_secret_cipher", secretCipher);
        cmd.Parameters.AddWithValue("passphrase_cipher", passphraseCipher);
        cmd.Parameters.AddWithValue("is_active", setActive);
        cmd.Parameters.AddWithValue("is_demo", isDemo);
        cmd.Parameters.AddWithValue("store_minute_equity", storeMinuteEquity);
        cmd.Parameters.AddWithValue("equity_currency", normalizedCurrency);
        var id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAccountSettingsAsync(int id, bool storeMinuteEquity, string equityCurrency, CancellationToken ct = default)
    {
        string normalizedCurrency = NormalizeEquityCurrency(equityCurrency);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        const string sql = @"
update public.van_okx_account
set store_minute_equity = @store_minute_equity,
    equity_currency = @equity_currency,
    updated_at = (now() at time zone 'utc')
where id = @id;
";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("store_minute_equity", storeMinuteEquity);
        cmd.Parameters.AddWithValue("equity_currency", normalizedCurrency);
        int rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<bool> SetActiveAccountAsync(int id, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var clearCmd = new NpgsqlCommand("update public.van_okx_account set is_active = false, updated_at = (now() at time zone 'utc') where is_active = true;", conn, tx))
        {
            await clearCmd.ExecuteNonQueryAsync(ct);
        }
        await using var cmd = new NpgsqlCommand("update public.van_okx_account set is_active = true, updated_at = (now() at time zone 'utc') where id = @id;", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        int rows = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return rows > 0;
    }

    public async Task<bool> DeleteAccountAsync(int id, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("delete from public.van_okx_account where id = @id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task AddSnapshotAsync(int accountId, decimal totalEquityUsdt, decimal availableEquityUsdt, decimal unrealizedPnlUsdt, decimal? marginRatio, CancellationToken ct = default)
    {
        const string sql = @"
insert into public.van_okx_equity_snapshot (account_id, ts_utc, total_equity_usdt, available_equity_usdt, unrealized_pnl_usdt, margin_ratio)
values (@account_id, (now() at time zone 'utc'), @total_equity_usdt, @available_equity_usdt, @unrealized_pnl_usdt, @margin_ratio);
";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("total_equity_usdt", totalEquityUsdt);
        cmd.Parameters.AddWithValue("available_equity_usdt", availableEquityUsdt);
        cmd.Parameters.AddWithValue("unrealized_pnl_usdt", unrealizedPnlUsdt);
        cmd.Parameters.AddWithValue("margin_ratio", marginRatio.HasValue ? marginRatio.Value : DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertMinuteEquityAsync(int accountId, DateTime minuteUtc, string equityCurrency, decimal totalEquity, decimal availableEquity, decimal unrealizedPnl, decimal? marginRatio, CancellationToken ct = default)
    {
        const string sql = @"
insert into public.van_okx_equity_minute (account_id, minute_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
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
        const string sql = @"
insert into public.van_okx_equity_daily (account_id, day_utc, last_ts_utc, equity_currency, total_equity, available_equity, unrealized_pnl, margin_ratio, created_at, updated_at)
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

    public async Task<List<OkxSnapshotPoint>> GetSnapshotsAsync(int accountId, int days, CancellationToken ct = default)
    {
        const string sql = @"
select ts_utc, total_equity_usdt, available_equity_usdt, unrealized_pnl_usdt, margin_ratio
from public.van_okx_equity_snapshot
where account_id = @account_id
  and ts_utc >= ((now() at time zone 'utc') - make_interval(days => @days))
order by ts_utc;
";
        var list = new List<OkxSnapshotPoint>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("days", Math.Max(1, days));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OkxSnapshotPoint
            {
                tsUtc = reader.GetDateTime(0),
                totalEquityUsdt = reader.GetDecimal(1),
                availableEquityUsdt = reader.GetDecimal(2),
                unrealizedPnlUsdt = reader.GetDecimal(3),
                marginRatio = reader.IsDBNull(4) ? null : reader.GetDecimal(4)
            });
        }
        return list;
    }

    public async Task<List<OkxEquityPoint>> GetMinuteEquityAsync(int accountId, int hours, CancellationToken ct = default)
    {
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_okx_equity_minute
where account_id = @account_id
  and minute_utc >= ((now() at time zone 'utc') - make_interval(hours => @hours))
order by minute_utc;
";
        return await ReadPnlPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("hours", Math.Max(1, hours));
        }, ct);
    }

    public async Task<List<OkxEquityPoint>> GetMinuteEquityForDailyDrawdownAsync(int accountId, int days, CancellationToken ct = default)
    {
        const string sql = @"
select minute_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_okx_equity_minute
where account_id = @account_id
  and minute_utc >= (((now() at time zone 'utc')::date) - @days)
order by minute_utc;
";
        return await ReadPnlPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("days", Math.Max(1, days));
        }, ct);
    }

    public async Task<List<OkxEquityPoint>> GetDailyEquityAsync(int accountId, int days, CancellationToken ct = default)
    {
        const string sql = @"
select last_ts_utc, total_equity, available_equity, unrealized_pnl, margin_ratio, equity_currency
from public.van_okx_equity_daily
where account_id = @account_id
  and day_utc >= (((now() at time zone 'utc')::date) - @days)
order by day_utc;
";
        return await ReadPnlPointsAsync(sql, cmd =>
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("days", Math.Max(1, days));
        }, ct);
    }

    public async Task UpsertFundingMinuteAsync(OkxFundingRow row, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_okx_futures_funding_minute
    (inst_id, display_name, minute_utc, funding_time_utc, next_funding_time_utc, current_funding, interest_8h, settled_funding, sett_state, source, raw_json, created_at, updated_at)
values
    (@inst_id, @display_name, @minute_utc, @funding_time_utc, @next_funding_time_utc, @current_funding, @interest_8h, @settled_funding, @sett_state, @source, cast(@raw_json as jsonb), (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (inst_id, minute_utc)
do update set display_name = excluded.display_name,
              funding_time_utc = excluded.funding_time_utc,
              next_funding_time_utc = excluded.next_funding_time_utc,
              current_funding = excluded.current_funding,
              interest_8h = excluded.interest_8h,
              settled_funding = excluded.settled_funding,
              sett_state = excluded.sett_state,
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

    public async Task<List<OkxFundingRow>> GetFundingMinuteAsync(string instId, int limit, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select inst_id, display_name, minute_utc, funding_time_utc, next_funding_time_utc, current_funding, interest_8h, settled_funding, sett_state, source, raw_json::text, created_at, updated_at
from public.van_okx_futures_funding_minute
where inst_id = @inst_id
order by minute_utc desc
limit @limit;
";
        var list = new List<OkxFundingRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("inst_id", NormalizeFundingInstId(instId));
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 10000));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OkxFundingRow
            {
                instId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                displayName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                minuteUtc = reader.GetDateTime(2),
                fundingTimeUtc = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                nextFundingTimeUtc = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                currentFunding = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                interest8h = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                settledFunding = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                settState = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                source = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                rawJson = reader.IsDBNull(10) ? "{}" : reader.GetString(10),
                createdAtUtc = reader.GetDateTime(11),
                updatedAtUtc = reader.GetDateTime(12)
            });
        }
        list.Reverse();
        return list;
    }

    private static void BindFunding(NpgsqlCommand cmd, OkxFundingRow row)
    {
        cmd.Parameters.AddWithValue("inst_id", NormalizeFundingInstId(row.instId));
        cmd.Parameters.AddWithValue("display_name", string.IsNullOrWhiteSpace(row.displayName) ? NormalizeFundingInstId(row.instId) : row.displayName);
        cmd.Parameters.AddWithValue("minute_utc", row.minuteUtc);
        cmd.Parameters.AddWithValue("funding_time_utc", row.fundingTimeUtc.HasValue ? row.fundingTimeUtc.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("next_funding_time_utc", row.nextFundingTimeUtc.HasValue ? row.nextFundingTimeUtc.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("current_funding", row.currentFunding.HasValue ? row.currentFunding.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("interest_8h", row.interest8h.HasValue ? row.interest8h.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("settled_funding", row.settledFunding.HasValue ? row.settledFunding.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("sett_state", row.settState ?? string.Empty);
        cmd.Parameters.AddWithValue("source", string.IsNullOrWhiteSpace(row.source) ? "okx:/api/v5/public/funding-rate" : row.source);
        cmd.Parameters.AddWithValue("raw_json", string.IsNullOrWhiteSpace(row.rawJson) ? "{}" : row.rawJson);
    }

    public static string NormalizeFundingInstId(string? instId)
    {
        string normalized = (instId ?? string.Empty).Trim().ToUpperInvariant();
        normalized = normalized.Replace("_", "-");
        if (normalized == "BTCUSDT" || normalized == "BTC-USDT") return "BTC-USDT-SWAP";
        if (normalized == "ETHUSDT" || normalized == "ETH-USDT") return "ETH-USDT-SWAP";
        return string.IsNullOrWhiteSpace(normalized) ? "BTC-USDT-SWAP" : normalized;
    }

    private async Task<List<OkxEquityPoint>> ReadPnlPointsAsync(string sql, Action<NpgsqlCommand> bind, CancellationToken ct)
    {
        var list = new List<OkxEquityPoint>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        bind(cmd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OkxEquityPoint
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

    private static string ReadApiKey(NpgsqlDataReader reader, int apiKeyOrdinal, int apiKeyCipherOrdinal)
    {
        string apiKeyCipher = reader.IsDBNull(apiKeyCipherOrdinal) ? "" : reader.GetString(apiKeyCipherOrdinal);
        if (!string.IsNullOrWhiteSpace(apiKeyCipher))
        {
            try
            {
                return ApiSecretCrypto.Decrypt(apiKeyCipher);
            }
            catch
            {
            }
        }

        return reader.IsDBNull(apiKeyOrdinal) ? "" : reader.GetString(apiKeyOrdinal);
    }

    public static string NormalizeEquityCurrency(string? equityCurrency)
    {
        string normalized = (equityCurrency ?? "USD").Trim().ToUpperInvariant();
        return normalized == "BTC" ? "BTC" : "USD";
    }

    public static DateTime TruncateToMinuteUtc(DateTime utc)
    {
        var normalized = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc);
        return new DateTime(normalized.Year, normalized.Month, normalized.Day, normalized.Hour, normalized.Minute, 0, DateTimeKind.Utc);
    }

    public static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return "";
        string trimmed = apiKey.Trim();
        if (trimmed.Length <= 8) return trimmed;
        return trimmed.Substring(0, 4) + "..." + trimmed.Substring(trimmed.Length - 4);
    }
}
