using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Npgsql;

namespace VANWebService.Models;

public sealed class ArbitrageBidAskWatch
{
    public string exchangeId { get; set; } = "";
    public string symbol { get; set; } = "";
    public string apiSymbol { get; set; } = "";
}

public sealed class ArbitrageBidAskMinuteRow
{
    public string exchangeId { get; set; } = "";
    public string apiSymbol { get; set; } = "";
    public DateTime minuteUtc { get; set; }
    public decimal? bestBidClose { get; set; }
    public decimal? bestAskClose { get; set; }
    public string source { get; set; } = "";
}

public sealed class ArbitrageBidAskRepository
{
    private static readonly SemaphoreSlim SchemaLock = new SemaphoreSlim(1, 1);
    private static volatile bool SchemaReady;
    private readonly string _connectionString;
    private readonly Enums.LogAction _log;

    public ArbitrageBidAskRepository(string connectionString, Enums.LogAction log)
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
create table if not exists public.van_arbitrage_bidask_watch (
    exchange_id text not null,
    api_symbol text not null,
    symbol text not null default '',
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    last_error text not null default '',
    primary key (exchange_id, api_symbol)
);
create index if not exists van_arbitrage_bidask_watch_updated_idx on public.van_arbitrage_bidask_watch (updated_at desc);

create table if not exists public.van_arbitrage_bidask_minute (
    exchange_id text not null,
    api_symbol text not null,
    minute_utc timestamptz not null,
    best_bid_close numeric(38,10) null,
    best_ask_close numeric(38,10) null,
    source text not null default '',
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    primary key (exchange_id, api_symbol, minute_utc)
);
create index if not exists van_arbitrage_bidask_minute_symbol_time_idx on public.van_arbitrage_bidask_minute (exchange_id, api_symbol, minute_utc desc);
";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
            SchemaReady = true;
        }
        finally
        {
            SchemaLock.Release();
        }
    }

    public async Task RegisterWatchAsync(string exchangeId, string apiSymbol, string symbol, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        const string sql = @"
insert into public.van_arbitrage_bidask_watch (exchange_id, api_symbol, symbol, created_at, updated_at)
values (@exchange_id, @api_symbol, @symbol, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (exchange_id, api_symbol) do update set
    symbol = excluded.symbol,
    updated_at = (now() at time zone 'utc'),
    last_error = '';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("exchange_id", exchangeId);
        cmd.Parameters.AddWithValue("api_symbol", apiSymbol);
        cmd.Parameters.AddWithValue("symbol", symbol ?? "");
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetWatchErrorAsync(string exchangeId, string apiSymbol, string message, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("update public.van_arbitrage_bidask_watch set last_error=@last_error, updated_at=(now() at time zone 'utc') where exchange_id=@exchange_id and api_symbol=@api_symbol;", conn);
        cmd.Parameters.AddWithValue("exchange_id", exchangeId);
        cmd.Parameters.AddWithValue("api_symbol", apiSymbol);
        cmd.Parameters.AddWithValue("last_error", message.Length > 300 ? message.Substring(0, 300) : message);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ArbitrageBidAskWatch>> GetActiveWatchesAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var rows = new List<ArbitrageBidAskWatch>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        const string sql = @"
select exchange_id, api_symbol, symbol
from public.van_arbitrage_bidask_watch
where updated_at >= (now() at time zone 'utc') - interval '14 days'
order by updated_at desc
limit 200;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ArbitrageBidAskWatch
            {
                exchangeId = reader.GetString(0),
                apiSymbol = reader.GetString(1),
                symbol = reader.GetString(2)
            });
        }
        return rows;
    }

    public async Task UpsertMinuteAsync(string exchangeId, string apiSymbol, DateTime minuteUtc, decimal bestBid, decimal bestAsk, string source, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        const string sql = @"
insert into public.van_arbitrage_bidask_minute (exchange_id, api_symbol, minute_utc, best_bid_close, best_ask_close, source, created_at, updated_at)
values (@exchange_id, @api_symbol, @minute_utc, @best_bid_close, @best_ask_close, @source, (now() at time zone 'utc'), (now() at time zone 'utc'))
on conflict (exchange_id, api_symbol, minute_utc) do update set
    best_bid_close = excluded.best_bid_close,
    best_ask_close = excluded.best_ask_close,
    source = excluded.source,
    updated_at = (now() at time zone 'utc');";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("exchange_id", exchangeId);
        cmd.Parameters.AddWithValue("api_symbol", apiSymbol);
        cmd.Parameters.AddWithValue("minute_utc", minuteUtc);
        cmd.Parameters.AddWithValue("best_bid_close", bestBid);
        cmd.Parameters.AddWithValue("best_ask_close", bestAsk);
        cmd.Parameters.AddWithValue("source", source);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ArbitrageBidAskMinuteRow>> GetRowsAsync(string exchangeId, string apiSymbol, int limit, DateTime? beforeUtc = null, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        var rows = new List<ArbitrageBidAskMinuteRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        string sql = @"
select exchange_id, api_symbol, minute_utc, best_bid_close, best_ask_close, source
from public.van_arbitrage_bidask_minute
where exchange_id=@exchange_id and api_symbol=@api_symbol" + (beforeUtc.HasValue ? " and minute_utc < @before_utc" : "") + @"
order by minute_utc desc
limit @limit;";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("exchange_id", exchangeId);
        cmd.Parameters.AddWithValue("api_symbol", apiSymbol);
        cmd.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 20000));
        if (beforeUtc.HasValue) cmd.Parameters.AddWithValue("before_utc", beforeUtc.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ArbitrageBidAskMinuteRow
            {
                exchangeId = reader.GetString(0),
                apiSymbol = reader.GetString(1),
                minuteUtc = reader.GetDateTime(2),
                bestBidClose = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                bestAskClose = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                source = reader.IsDBNull(5) ? "" : reader.GetString(5)
            });
        }
        rows.Reverse();
        return rows;
    }
}
