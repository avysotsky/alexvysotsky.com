using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Npgsql;
using NpgsqlTypes;

namespace VANWebService.Models;

public sealed record CoincallBackendSpreadBotStartRequest(
    int? accountId,
    string? pair,
    string? strategy,
    string? mode,
    JsonElement? config
);

public sealed record CoincallBackendSpreadBotStopRequest(bool? cancelOwnedOrders);

public sealed record CoincallBackendSpreadBotSettingsRequest(
    int? accountId,
    string? pair,
    string? strategy,
    string? mode,
    JsonElement? config
);

public sealed record CoincallBackendSpreadBotInstance(
    Guid id,
    int accountId,
    string accountName,
    string pair,
    string strategy,
    string mode,
    string status,
    string configJson,
    string stateJson,
    string lastError,
    DateTime createdAtUtc,
    DateTime updatedAtUtc,
    DateTime? startedAtUtc,
    DateTime? stoppedAtUtc
);

public sealed record CoincallBackendSpreadBotLogRow(
    long id,
    Guid instanceId,
    DateTime tsUtc,
    string state,
    string action,
    string message,
    string payloadJson
);

public sealed record CoincallBackendSpreadBotSettings(
    int accountId,
    string accountName,
    string pair,
    string strategy,
    string mode,
    string configJson,
    DateTime updatedAtUtc
);

public sealed class CoincallSpreadBotRepository
{
    private readonly string _connectionString;
    private readonly Enums.LogAction _log;
    private readonly CoincallBackendSpreadBotEventHub? _events;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaEnsured;

    public CoincallSpreadBotRepository(string connectionString, Enums.LogAction log, CoincallBackendSpreadBotEventHub? events = null)
    {
        _connectionString = connectionString;
        _log = log;
        _events = events;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (_schemaEnsured) return;
        await _schemaLock.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;
            const string sql = @"
create table if not exists public.van_coincall_spreadbot_instance (
    id uuid primary key,
    account_id integer not null references public.van_coincall_account(id) on delete restrict,
    pair text not null,
    strategy text not null default 'orders-net',
    mode text not null default 'LLF-BS',
    status text not null default 'stopped',
    config_json jsonb not null default '{}'::jsonb,
    state_json jsonb not null default '{}'::jsonb,
    last_error text not null default '',
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc'),
    started_at timestamptz null,
    stopped_at timestamptz null
);
alter table public.van_coincall_spreadbot_instance add column if not exists account_id integer not null references public.van_coincall_account(id) on delete restrict;
alter table public.van_coincall_spreadbot_instance add column if not exists pair text not null default '';
alter table public.van_coincall_spreadbot_instance add column if not exists strategy text not null default 'orders-net';
alter table public.van_coincall_spreadbot_instance add column if not exists mode text not null default 'LLF-BS';
alter table public.van_coincall_spreadbot_instance add column if not exists status text not null default 'stopped';
alter table public.van_coincall_spreadbot_instance add column if not exists config_json jsonb not null default '{}'::jsonb;
alter table public.van_coincall_spreadbot_instance add column if not exists state_json jsonb not null default '{}'::jsonb;
alter table public.van_coincall_spreadbot_instance add column if not exists last_error text not null default '';
alter table public.van_coincall_spreadbot_instance add column if not exists started_at timestamptz null;
alter table public.van_coincall_spreadbot_instance add column if not exists stopped_at timestamptz null;
create index if not exists van_coincall_spreadbot_instance_pair_idx on public.van_coincall_spreadbot_instance (account_id, pair, strategy);
create unique index if not exists van_coincall_spreadbot_instance_running_uq
    on public.van_coincall_spreadbot_instance (account_id, lower(pair), lower(strategy))
    where status in ('starting','running','stop_requested');

create table if not exists public.van_coincall_spreadbot_log (
    id bigserial primary key,
    instance_id uuid not null references public.van_coincall_spreadbot_instance(id) on delete cascade,
    ts_utc timestamptz not null default (now() at time zone 'utc'),
    state text not null default '',
    action text not null default '',
    message text not null default '',
    payload_json jsonb not null default '{}'::jsonb
);
create index if not exists van_coincall_spreadbot_log_instance_ts_idx on public.van_coincall_spreadbot_log (instance_id, ts_utc desc);

create table if not exists public.van_coincall_spreadbot_settings (
    id bigserial primary key,
    account_id integer not null references public.van_coincall_account(id) on delete restrict,
    pair text not null,
    pair_key text not null,
    strategy text not null default 'orders-net',
    strategy_key text not null default 'orders-net',
    mode text not null default 'LLF-BS',
    config_json jsonb not null default '{}'::jsonb,
    updated_at timestamptz not null default (now() at time zone 'utc')
);
alter table public.van_coincall_spreadbot_settings add column if not exists account_id integer not null references public.van_coincall_account(id) on delete restrict;
alter table public.van_coincall_spreadbot_settings add column if not exists pair text not null default '';
alter table public.van_coincall_spreadbot_settings add column if not exists pair_key text not null default '';
alter table public.van_coincall_spreadbot_settings add column if not exists strategy text not null default 'orders-net';
alter table public.van_coincall_spreadbot_settings add column if not exists strategy_key text not null default 'orders-net';
alter table public.van_coincall_spreadbot_settings add column if not exists mode text not null default 'LLF-BS';
alter table public.van_coincall_spreadbot_settings add column if not exists config_json jsonb not null default '{}'::jsonb;
alter table public.van_coincall_spreadbot_settings add column if not exists updated_at timestamptz not null default (now() at time zone 'utc');
create unique index if not exists van_coincall_spreadbot_settings_uq
    on public.van_coincall_spreadbot_settings (account_id, pair_key, strategy_key);
";
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(ct);
            _schemaEnsured = true;
        }
        catch (Exception ex)
        {
            _log("CoinCall spreadbot schema ensure failed: " + ex.Message, Enums.LogLevel.llExceptions);
            throw;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public async Task<CoincallBackendSpreadBotInstance> CreateInstanceAsync(int accountId, string pair, string strategy, string mode, string configJson, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        var id = Guid.NewGuid();
        const string sql = @"
insert into public.van_coincall_spreadbot_instance
    (id, account_id, pair, strategy, mode, status, config_json, state_json, last_error, started_at, stopped_at)
values
    (@id, @account_id, @pair, @strategy, @mode, 'running', @config_json::jsonb, '{}'::jsonb, '', now() at time zone 'utc', null)
returning id;";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("pair", pair);
            cmd.Parameters.AddWithValue("strategy", strategy);
            cmd.Parameters.AddWithValue("mode", mode);
            cmd.Parameters.AddWithValue("config_json", NpgsqlDbType.Jsonb, NormalizeJson(configJson));
            await cmd.ExecuteScalarAsync(ct);
        }
        return (await GetInstanceAsync(id, ct))!;
    }

    public async Task<CoincallBackendSpreadBotInstance?> GetRunningInstanceAsync(int accountId, string pair, string strategy, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select i.id, i.account_id, coalesce(a.name, '') as account_name, i.pair, i.strategy, i.mode, i.status,
       i.config_json::text, i.state_json::text, i.last_error, i.created_at, i.updated_at, i.started_at, i.stopped_at
from public.van_coincall_spreadbot_instance i
left join public.van_coincall_account a on a.id = i.account_id
where i.account_id = @account_id
  and lower(i.pair) = lower(@pair)
  and lower(i.strategy) = lower(@strategy)
  and i.status in ('starting','running','stop_requested')
order by i.updated_at desc
limit 1;";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("pair", pair ?? string.Empty);
        cmd.Parameters.AddWithValue("strategy", strategy ?? string.Empty);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadInstance(reader) : null;
    }

    public async Task<CoincallBackendSpreadBotSettings> UpsertSettingsAsync(int accountId, string pair, string strategy, string mode, string configJson, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_spreadbot_settings
    (account_id, pair, pair_key, strategy, strategy_key, mode, config_json, updated_at)
values
    (@account_id, @pair, lower(@pair), @strategy, lower(@strategy), @mode, @config_json::jsonb, now() at time zone 'utc')
on conflict (account_id, pair_key, strategy_key) do update
set pair = excluded.pair,
    strategy = excluded.strategy,
    mode = excluded.mode,
    config_json = excluded.config_json,
    updated_at = now() at time zone 'utc';";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using (var cmd = new NpgsqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("account_id", accountId);
            cmd.Parameters.AddWithValue("pair", pair ?? string.Empty);
            cmd.Parameters.AddWithValue("strategy", strategy ?? string.Empty);
            cmd.Parameters.AddWithValue("mode", mode ?? string.Empty);
            cmd.Parameters.AddWithValue("config_json", NpgsqlDbType.Jsonb, NormalizeJson(configJson));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return (await GetSettingsAsync(accountId, pair, strategy, ct))!;
    }

    public async Task<CoincallBackendSpreadBotSettings?> GetSettingsAsync(int accountId, string pair, string strategy, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select s.account_id, coalesce(a.name, '') as account_name, s.pair, s.strategy, s.mode, s.config_json::text, s.updated_at
from public.van_coincall_spreadbot_settings s
left join public.van_coincall_account a on a.id = s.account_id
where s.account_id = @account_id and s.pair_key = lower(@pair) and s.strategy_key = lower(@strategy)
limit 1;";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("account_id", accountId);
        cmd.Parameters.AddWithValue("pair", pair ?? string.Empty);
        cmd.Parameters.AddWithValue("strategy", strategy ?? string.Empty);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new CoincallBackendSpreadBotSettings(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetDateTime(6))
            : null;
    }

    public async Task SetStatusAsync(Guid id, string status, string stateJson, string lastError, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
update public.van_coincall_spreadbot_instance
set status = @status,
    state_json = @state_json::jsonb,
    last_error = @last_error,
    updated_at = now() at time zone 'utc',
    started_at = case when @status = 'running' and started_at is null then now() at time zone 'utc' else started_at end,
    stopped_at = case when @status in ('stopped','failed') then now() at time zone 'utc' else stopped_at end
where id = @id;";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("state_json", NpgsqlDbType.Jsonb, NormalizeJson(stateJson));
        cmd.Parameters.AddWithValue("last_error", lastError ?? string.Empty);
        await cmd.ExecuteNonQueryAsync(ct);
        _events?.PublishInstance(id, new
        {
            item = new
            {
                id,
                status,
                stateJson = NormalizeJson(stateJson),
                lastError = lastError ?? string.Empty,
                updatedAtUtc = DateTime.UtcNow
            }
        });
    }

    public async Task<CoincallBackendSpreadBotInstance?> GetInstanceAsync(Guid id, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select i.id, i.account_id, coalesce(a.name, '') as account_name, i.pair, i.strategy, i.mode, i.status,
       i.config_json::text, i.state_json::text, i.last_error, i.created_at, i.updated_at, i.started_at, i.stopped_at
from public.van_coincall_spreadbot_instance i
left join public.van_coincall_account a on a.id = i.account_id
where i.id = @id;";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadInstance(reader) : null;
    }

    public async Task<List<CoincallBackendSpreadBotInstance>> ListInstancesAsync(CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
select i.id, i.account_id, coalesce(a.name, '') as account_name, i.pair, i.strategy, i.mode, i.status,
       i.config_json::text, i.state_json::text, i.last_error, i.created_at, i.updated_at, i.started_at, i.stopped_at
from public.van_coincall_spreadbot_instance i
left join public.van_coincall_account a on a.id = i.account_id
order by i.updated_at desc
limit 500;";
        var rows = new List<CoincallBackendSpreadBotInstance>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) rows.Add(ReadInstance(reader));
        return rows;
    }

    public async Task AppendLogAsync(Guid instanceId, string state, string action, string message, string payloadJson, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"
insert into public.van_coincall_spreadbot_log (instance_id, state, action, message, payload_json)
values (@instance_id, @state, @action, @message, @payload_json::jsonb)
returning id, ts_utc;";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("instance_id", instanceId);
        cmd.Parameters.AddWithValue("state", state ?? string.Empty);
        cmd.Parameters.AddWithValue("action", action ?? string.Empty);
        cmd.Parameters.AddWithValue("message", message ?? string.Empty);
        cmd.Parameters.AddWithValue("payload_json", NpgsqlDbType.Jsonb, NormalizeJson(payloadJson));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var row = new CoincallBackendSpreadBotLogRow(
                reader.GetInt64(0),
                instanceId,
                reader.GetDateTime(1),
                state ?? string.Empty,
                action ?? string.Empty,
                message ?? string.Empty,
                NormalizeJson(payloadJson)
            );
            _events?.PublishLog(instanceId, new { item = row });
        }
    }

    public async Task<List<CoincallBackendSpreadBotLogRow>> GetLogsAsync(Guid instanceId, int limit, CancellationToken ct)
    {
        await EnsureSchemaAsync(ct);
        limit = Math.Clamp(limit, 1, 1000);
        const string sql = @"
select id, instance_id, ts_utc, state, action, message, payload_json::text
from public.van_coincall_spreadbot_log
where instance_id = @instance_id
order by ts_utc desc, id desc
limit @limit;";
        var rows = new List<CoincallBackendSpreadBotLogRow>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("instance_id", instanceId);
        cmd.Parameters.AddWithValue("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new CoincallBackendSpreadBotLogRow(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetDateTime(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6)
            ));
        }
        return rows;
    }

    private static CoincallBackendSpreadBotInstance ReadInstance(NpgsqlDataReader reader)
    {
        return new CoincallBackendSpreadBotInstance(
            reader.GetGuid(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetDateTime(10),
            reader.GetDateTime(11),
            reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            reader.IsDBNull(13) ? null : reader.GetDateTime(13)
        );
    }

    private static string NormalizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{}";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? doc.RootElement.GetRawText() : "{}";
        }
        catch (JsonException)
        {
            return "{}";
        }
    }
}
