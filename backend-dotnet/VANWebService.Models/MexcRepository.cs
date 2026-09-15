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

public sealed class MexcAccountRecord
{
    public int id { get; set; }
    public string name { get; set; } = "";
    public string apiKey { get; set; } = "";
    public string apiSecret { get; set; } = "";
    public bool isActive { get; set; }
    public DateTime createdAtUtc { get; set; }
    public DateTime updatedAtUtc { get; set; }
}

public sealed class MexcRepository
{
    private readonly string _connectionString;
    private readonly Enums.LogAction _log;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaEnsured;

    public MexcRepository(string connectionString, Enums.LogAction log)
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
create table if not exists public.van_mexc_account (
    id serial primary key,
    name text not null,
    api_key_cipher text not null default '',
    api_secret_cipher text not null default '',
    is_active boolean not null default false,
    created_at timestamptz not null default (now() at time zone 'utc'),
    updated_at timestamptz not null default (now() at time zone 'utc')
);
alter table public.van_mexc_account add column if not exists api_key_cipher text not null default '';
alter table public.van_mexc_account add column if not exists api_secret_cipher text not null default '';
alter table public.van_mexc_account add column if not exists is_active boolean not null default false;
alter table public.van_mexc_account add column if not exists created_at timestamptz not null default (now() at time zone 'utc');
alter table public.van_mexc_account add column if not exists updated_at timestamptz not null default (now() at time zone 'utc');
create unique index if not exists van_mexc_account_name_uq on public.van_mexc_account ((lower(name)));
create unique index if not exists van_mexc_account_single_active_idx on public.van_mexc_account ((is_active)) where is_active = true;
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

    public async Task<List<MexcAccountRecord>> GetAccountsAsync(bool includeSecrets, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"select id, name, api_key_cipher, api_secret_cipher, is_active, created_at, updated_at from public.van_mexc_account order by is_active desc, lower(name), id;";
        var list = new List<MexcAccountRecord>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new MexcAccountRecord
            {
                id = r.GetInt32(0),
                name = r.GetString(1),
                apiKey = includeSecrets ? ApiSecretCrypto.Decrypt(r.GetString(2)) : "",
                apiSecret = includeSecrets ? ApiSecretCrypto.Decrypt(r.GetString(3)) : "",
                isActive = r.GetBoolean(4),
                createdAtUtc = r.GetDateTime(5),
                updatedAtUtc = r.GetDateTime(6)
            });
        }
        return list;
    }

    public async Task<MexcAccountRecord?> GetAccountByIdAsync(int id, CancellationToken ct = default)
    {
        var accounts = await GetAccountsAsync(true, ct);
        return accounts.FirstOrDefault(a => a.id == id);
    }

    public async Task<MexcAccountRecord?> GetActiveAccountAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        const string sql = @"select id, name, api_key_cipher, api_secret_cipher, is_active, created_at, updated_at from public.van_mexc_account where is_active = true limit 1;";
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new MexcAccountRecord
        {
            id = r.GetInt32(0),
            name = r.GetString(1),
            apiKey = ApiSecretCrypto.Decrypt(r.GetString(2)),
            apiSecret = ApiSecretCrypto.Decrypt(r.GetString(3)),
            isActive = r.GetBoolean(4),
            createdAtUtc = r.GetDateTime(5),
            updatedAtUtc = r.GetDateTime(6)
        };
    }

    public async Task<int> CreateAccountAsync(string name, string apiKey, string apiSecret, bool setActive, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (setActive)
        {
            await using var clear = new NpgsqlCommand("update public.van_mexc_account set is_active=false, updated_at=(now() at time zone 'utc') where is_active=true;", conn, tx);
            await clear.ExecuteNonQueryAsync(ct);
        }

        const string sql = @"insert into public.van_mexc_account (name, api_key_cipher, api_secret_cipher, is_active, created_at, updated_at) values (@name, @api_key_cipher, @api_secret_cipher, @is_active, (now() at time zone 'utc'), (now() at time zone 'utc')) returning id;";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("name", name.Trim());
        cmd.Parameters.AddWithValue("api_key_cipher", ApiSecretCrypto.Encrypt(apiKey.Trim()));
        cmd.Parameters.AddWithValue("api_secret_cipher", ApiSecretCrypto.Encrypt(apiSecret.Trim()));
        cmd.Parameters.AddWithValue("is_active", setActive);
        int id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAccountAsync(int id, string name, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("update public.van_mexc_account set name=@name, updated_at=(now() at time zone 'utc') where id=@id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", name.Trim());
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> SetActiveAccountAsync(int id, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var clear = new NpgsqlCommand("update public.van_mexc_account set is_active=false, updated_at=(now() at time zone 'utc') where is_active=true;", conn, tx))
        {
            await clear.ExecuteNonQueryAsync(ct);
        }
        await using var cmd = new NpgsqlCommand("update public.van_mexc_account set is_active=true, updated_at=(now() at time zone 'utc') where id=@id;", conn, tx);
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
        await using var cmd = new NpgsqlCommand("delete from public.van_mexc_account where id=@id;", conn);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public static string MaskApiKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string v = value.Trim();
        if (v.Length <= 8) return new string('*', v.Length);
        return v[..4] + new string('*', Math.Max(4, v.Length - 8)) + v[^4..];
    }
}
