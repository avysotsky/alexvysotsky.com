using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using Npgsql;
using NpgsqlTypes;

namespace VANWebService.Models;

public sealed class AccountDailyEquityRepository
{
	public sealed class AccountForDailyEquity
	{
		public int AccId { get; init; }

		public DateOnly StartDateUtc { get; init; }

		public bool IncUsdc { get; init; }

		public bool IncEth { get; init; }

		public bool IncBtc { get; init; }

		public string ApiPublicKey { get; init; } = "";

		public string ApiSecretPlain { get; init; } = "";
	}

	public sealed record DailyRow(DateOnly DateUtc, decimal? UsdcEquity, decimal? EthEquity, decimal? BtcEquity, decimal? EthPrice, decimal? BtcPrice, decimal? EthEquityUsd, decimal? BtcEquityUsd, decimal? DayMaxDrawdownPct = null);

	public sealed record MinuteUsdSnapshot(DateTime TimestampUtc, decimal? UsdcEquityUsd, decimal? EthEquityUsd, decimal? BtcEquityUsd, decimal? TotalEquityUsd);

	private static class ApiSecretCrypto
	{
		private const string EnvVarName = "VAN_API_SECRET_ENCRYPTION_KEY";

		private static string GetKeyMaterial()
		{
			string text = Environment.GetEnvironmentVariable("VAN_API_SECRET_ENCRYPTION_KEY");
			if (string.IsNullOrWhiteSpace(text))
			{
				text = "CHANGE_ME_TO_LONG_RANDOM_SECRET_KEY_32+CHARS";
			}
			return text;
		}

		public static string Decrypt(string cipherText)
		{
			return ApiSecretProtector.Decrypt(cipherText, GetKeyMaterial());
		}
	}

	private static class ApiSecretProtector
	{
		public static string Decrypt(string cipherTextBase64, string keyMaterial)
		{
			using Aes aes = Aes.Create();
			aes.Key = DeriveKey(keyMaterial);
			byte[] array = Convert.FromBase64String(cipherTextBase64);
			byte[] array2 = new byte[aes.BlockSize / 8];
			Buffer.BlockCopy(array, 0, array2, 0, array2.Length);
			aes.IV = array2;
			byte[] array3 = new byte[array.Length - array2.Length];
			Buffer.BlockCopy(array, array2.Length, array3, 0, array3.Length);
			using MemoryStream stream = new MemoryStream(array3);
			using CryptoStream stream2 = new CryptoStream(stream, aes.CreateDecryptor(), CryptoStreamMode.Read);
			using StreamReader streamReader = new StreamReader(stream2, Encoding.UTF8);
			return streamReader.ReadToEnd();
		}

		private static byte[] DeriveKey(string keyMaterial)
		{
			using SHA256 sHA = SHA256.Create();
			return sHA.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
		}
	}

	private readonly string _connStr;

	private readonly Enums.LogAction _log;

	public AccountDailyEquityRepository(VANWebServiceConfig cfg, Enums.LogAction log)
	{
		_connStr = cfg.AuthorityConnectionString;
		_log = log;
	}

	public async Task<List<AccountForDailyEquity>> GetAccountsWithKeysAsync(CancellationToken ct = default(CancellationToken))
	{
		List<AccountForDailyEquity> result = new List<AccountForDailyEquity>();
		List<AccountForDailyEquity> result2;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync(ct);
			List<AccountForDailyEquity> list2;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect\r\n  acc_id,\r\n  created_at::date as start_date,\r\n  include_usdc_in_total,\r\n  include_eth_in_total,\r\n  include_btc_in_total,\r\n  api_public_key,\r\n  api_secret_cipher\r\nfrom public.van_account\r\nwhere api_public_key is not null\r\n  and api_secret_cipher is not null\r\norder by acc_id;\r\n", conn))
			{
				List<AccountForDailyEquity> list;
				await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
				{
					while (await r.ReadAsync(ct))
					{
						int @int = r.GetInt32(r.GetOrdinal("acc_id"));
						DateOnly fieldValue = r.GetFieldValue<DateOnly>(r.GetOrdinal("start_date"));
						bool boolean = r.GetBoolean(r.GetOrdinal("include_usdc_in_total"));
						bool boolean2 = r.GetBoolean(r.GetOrdinal("include_eth_in_total"));
						bool boolean3 = r.GetBoolean(r.GetOrdinal("include_btc_in_total"));
						string text = r.GetString(r.GetOrdinal("api_public_key"));
						string cipherText = r.GetString(r.GetOrdinal("api_secret_cipher"));
						string text2;
						try
						{
							text2 = ApiSecretCrypto.Decrypt(cipherText);
						}
						catch (Exception value)
						{
							_log($"[AccountDailyEquityRepo][WARN] decrypt failed for accId={@int}: {value}", Enums.LogLevel.llBaselogic);
							continue;
						}
						if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
						{
							result.Add(new AccountForDailyEquity
							{
								AccId = @int,
								StartDateUtc = fieldValue,
								IncUsdc = boolean,
								IncEth = boolean2,
								IncBtc = boolean3,
								ApiPublicKey = text.Trim(),
								ApiSecretPlain = text2.Trim()
							});
						}
					}
					list = result;
				}
				list2 = list;
			}
			result2 = list2;
		}
		return result2;
	}

	public async Task<DateOnly?> GetMaxDateAsync(int accId, CancellationToken ct = default(CancellationToken))
	{
		DateOnly? result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync(ct);
			DateOnly? dateOnly;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("select max(date_utc) from public.van_account_daily_equity where acc_id=@acc;", conn))
			{
				cmd.Parameters.AddWithValue("acc", accId);
				object obj = await cmd.ExecuteScalarAsync(ct);
				if (obj == null || obj is DBNull)
				{
					dateOnly = null;
				}
				else
				{
					DateOnly value = ((obj is DateOnly dateOnly2) ? dateOnly2 : ((!(obj is DateTime dateTime)) ? DateOnly.FromDateTime(Convert.ToDateTime(obj, CultureInfo.InvariantCulture)) : DateOnly.FromDateTime(dateTime)));
					dateOnly = value;
				}
			}
			result = dateOnly;
		}
		return result;
	}

	public async Task UpsertRangeAsync(int accId, IReadOnlyCollection<DailyRow> rows, bool incUsdc, bool incEth, bool incBtc, CancellationToken ct = default(CancellationToken))
	{
		if (rows.Count == 0)
		{
			return;
		}
		await using NpgsqlConnection conn = new NpgsqlConnection(_connStr);
		await conn.OpenAsync(ct);
		await using NpgsqlCommand cmd = new NpgsqlCommand("\r\ninsert into public.van_account_daily_equity\r\n(\r\n  acc_id, date_utc,\r\n  usdc_equity, eth_equity, btc_equity,\r\n  eth_price, btc_price,\r\n  eth_equity_usd, btc_equity_usd, day_max_drawdown_pct,\r\n  include_usdc_in_total, include_eth_in_total, include_btc_in_total\r\n)\r\nvalues\r\n(\r\n  @acc, @d,\r\n  @usdc_eq, @eth_eq, @btc_eq,\r\n  @eth_p, @btc_p,\r\n  @eth_usd, @btc_usd, @day_dd,\r\n  @inc_usdc, @inc_eth, @inc_btc\r\n)\r\non conflict (acc_id, date_utc) do update\r\nset\r\n  usdc_equity = excluded.usdc_equity,\r\n  eth_equity  = excluded.eth_equity,\r\n  btc_equity  = excluded.btc_equity,\r\n  eth_price   = excluded.eth_price,\r\n  btc_price   = excluded.btc_price,\r\n  eth_equity_usd = excluded.eth_equity_usd,\r\n  btc_equity_usd = excluded.btc_equity_usd,\r\n  day_max_drawdown_pct = excluded.day_max_drawdown_pct,\r\n  include_usdc_in_total = excluded.include_usdc_in_total,\r\n  include_eth_in_total  = excluded.include_eth_in_total,\r\n  include_btc_in_total  = excluded.include_btc_in_total;\r\n", conn);
		cmd.Parameters.Add(new NpgsqlParameter("acc", NpgsqlDbType.Integer)
		{
			Value = accId
		});
		cmd.Parameters.Add(new NpgsqlParameter("d", NpgsqlDbType.Date));
		cmd.Parameters.Add(new NpgsqlParameter("usdc_eq", NpgsqlDbType.Numeric));
		cmd.Parameters.Add(new NpgsqlParameter("eth_eq", NpgsqlDbType.Numeric));
		cmd.Parameters.Add(new NpgsqlParameter("btc_eq", NpgsqlDbType.Numeric));
		cmd.Parameters.Add(new NpgsqlParameter("eth_p", NpgsqlDbType.Numeric));
		cmd.Parameters.Add(new NpgsqlParameter("btc_p", NpgsqlDbType.Numeric));
		cmd.Parameters.Add(new NpgsqlParameter("eth_usd", NpgsqlDbType.Numeric));
		cmd.Parameters.Add(new NpgsqlParameter("btc_usd", NpgsqlDbType.Numeric));
		cmd.Parameters.Add(new NpgsqlParameter("day_dd", NpgsqlDbType.Numeric));
		cmd.Parameters.Add(new NpgsqlParameter("inc_usdc", NpgsqlDbType.Boolean)
		{
			Value = incUsdc
		});
		cmd.Parameters.Add(new NpgsqlParameter("inc_eth", NpgsqlDbType.Boolean)
		{
			Value = incEth
		});
		cmd.Parameters.Add(new NpgsqlParameter("inc_btc", NpgsqlDbType.Boolean)
		{
			Value = incBtc
		});
		foreach (DailyRow row in rows)
		{
			ct.ThrowIfCancellationRequested();
			cmd.Parameters["d"].Value = row.DateUtc;
			cmd.Parameters["usdc_eq"].Value = ((object)row.UsdcEquity) ?? DBNull.Value;
			cmd.Parameters["eth_eq"].Value = ((object)row.EthEquity) ?? DBNull.Value;
			cmd.Parameters["btc_eq"].Value = ((object)row.BtcEquity) ?? DBNull.Value;
			cmd.Parameters["eth_p"].Value = ((object)row.EthPrice) ?? DBNull.Value;
			cmd.Parameters["btc_p"].Value = ((object)row.BtcPrice) ?? DBNull.Value;
			cmd.Parameters["eth_usd"].Value = ((object)row.EthEquityUsd) ?? DBNull.Value;
			cmd.Parameters["btc_usd"].Value = ((object)row.BtcEquityUsd) ?? DBNull.Value;
			cmd.Parameters["day_dd"].Value = ((object)row.DayMaxDrawdownPct) ?? DBNull.Value;
			cmd.Parameters["inc_usdc"].Value = incUsdc;
			cmd.Parameters["inc_eth"].Value = incEth;
			cmd.Parameters["inc_btc"].Value = incBtc;
			await cmd.ExecuteNonQueryAsync(ct);
		}
	}


	public async Task<Dictionary<DateOnly, decimal>> GetIntradayDrawdownRangeAsync(int accId, DateOnly fromDateUtc, DateOnly toDateUtc, CancellationToken ct = default(CancellationToken))
	{
		Dictionary<DateOnly, decimal> result = new Dictionary<DateOnly, decimal>();
		await using NpgsqlConnection conn = new NpgsqlConnection(_connStr);
		await conn.OpenAsync(ct);
		await using NpgsqlCommand cmd = new NpgsqlCommand(@"
with min1 as (
    select
        date_trunc('day', ae.minute_utc)::date as date_utc,
        ae.minute_utc,
        ae.equity_total_usd as v,
        max(ae.equity_total_usd) over (
            order by ae.minute_utc
            rows between unbounded preceding and current row
        ) as peak
    from public.van_account_equity ae
    left join public.van_outage_intervals oi
        on ae.minute_utc >= oi.started_at and ae.minute_utc < oi.ended_at
    where ae.acc_id = @acc
      and ae.minute_utc < (@to_utc + interval '1 day')
      and oi.id is null
),
by_day as (
    select
        date_utc,
        max((peak - v) / peak * 100.0) as day_max_drawdown_pct
    from min1
    where date_utc >= @from_date
      and peak > 0
    group by date_utc
)
select date_utc, day_max_drawdown_pct
from by_day
order by date_utc;
", conn);
		cmd.Parameters.AddWithValue("acc", accId);
		cmd.Parameters.AddWithValue("from_date", fromDateUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
		cmd.Parameters.AddWithValue("to_utc", toDateUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
		await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct);
		while (await r.ReadAsync(ct))
		{
			DateOnly dateUtc = r.GetFieldValue<DateOnly>(0);
			decimal dd = r.IsDBNull(1) ? 0m : Convert.ToDecimal(r.GetValue(1), CultureInfo.InvariantCulture);
			result[dateUtc] = dd;
		}
		return result;
	}

	public async Task<AccountForDailyEquity?> GetAccountWithKeysAsync(int accId, CancellationToken ct = default(CancellationToken))
	{
		AccountForDailyEquity result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync(ct);
			AccountForDailyEquity accountForDailyEquity2;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect\r\n  acc_id,\r\n  created_at::date as start_date,\r\n  include_usdc_in_total,\r\n  include_eth_in_total,\r\n  include_btc_in_total,\r\n  api_public_key,\r\n  api_secret_cipher\r\nfrom public.van_account\r\nwhere acc_id = @acc\r\n  and api_public_key is not null\r\n  and api_secret_cipher is not null;\r\n", conn))
			{
				cmd.Parameters.AddWithValue("acc", accId);
				AccountForDailyEquity accountForDailyEquity;
				await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
				{
					if (!(await r.ReadAsync(ct)))
					{
						accountForDailyEquity = null;
					}
					else
					{
						DateOnly fieldValue = r.GetFieldValue<DateOnly>(r.GetOrdinal("start_date"));
						bool boolean = r.GetBoolean(r.GetOrdinal("include_usdc_in_total"));
						bool boolean2 = r.GetBoolean(r.GetOrdinal("include_eth_in_total"));
						bool boolean3 = r.GetBoolean(r.GetOrdinal("include_btc_in_total"));
						string text = r.GetString(r.GetOrdinal("api_public_key"));
						string cipherText = r.GetString(r.GetOrdinal("api_secret_cipher"));
						string text2;
						try
						{
							text2 = ApiSecretCrypto.Decrypt(cipherText);
						}
						catch
						{
							accountForDailyEquity = null;
							goto end_IL_01fe;
						}
						accountForDailyEquity = ((!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2)) ? new AccountForDailyEquity
						{
							AccId = accId,
							StartDateUtc = fieldValue,
							IncUsdc = boolean,
							IncEth = boolean2,
							IncBtc = boolean3,
							ApiPublicKey = text.Trim(),
							ApiSecretPlain = text2.Trim()
						} : null);
					}
					end_IL_01fe:;
				}
				accountForDailyEquity2 = accountForDailyEquity;
			}
			result = accountForDailyEquity2;
		}
		return result;
	}

	public async Task<MinuteUsdSnapshot?> GetMinuteSnapshotAtOrAfterAsync(int accId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
	{
		await using var conn = new NpgsqlConnection(_connStr);
		await conn.OpenAsync(ct);
		await using var cmd = new NpgsqlCommand(@"
select timestamp_utc, equity_usdc_usd, equity_eth_usd, equity_btc_usd, equity_total_usd
from public.van_account_equity
where acc_id = @acc
  and timestamp_utc >= @start_utc
  and timestamp_utc <= @end_utc
order by timestamp_utc asc
limit 1;", conn);
		cmd.Parameters.AddWithValue("acc", accId);
		cmd.Parameters.Add(new NpgsqlParameter("start_utc", NpgsqlDbType.TimestampTz) { Value = startUtc });
		cmd.Parameters.Add(new NpgsqlParameter("end_utc", NpgsqlDbType.TimestampTz) { Value = endUtc });
		await using var r = await cmd.ExecuteReaderAsync(ct);
		if (!await r.ReadAsync(ct))
			return null;
		static decimal? N(NpgsqlDataReader rdr, string col)
		{
			int ord = rdr.GetOrdinal(col);
			if (rdr.IsDBNull(ord))
			{
				return null;
			}
			object value = rdr.GetValue(ord);
			return value switch
			{
				decimal d => d,
				double d => Convert.ToDecimal(d, CultureInfo.InvariantCulture),
				float f => Convert.ToDecimal(f, CultureInfo.InvariantCulture),
				_ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
			};
		}
		return new MinuteUsdSnapshot(
			r.GetDateTime(r.GetOrdinal("timestamp_utc")),
			N(r, "equity_usdc_usd"),
			N(r, "equity_eth_usd"),
			N(r, "equity_btc_usd"),
			N(r, "equity_total_usd"));
	}

	/// <summary>
	/// Returns the most recent accepted daily row BEFORE <paramref name="beforeDate"/> for use as anomaly baseline.
	/// </summary>
	public async Task<DailyRow?> GetPriorDayRowAsync(int accId, DateOnly beforeDate, CancellationToken ct = default)
	{
		await using var conn = new NpgsqlConnection(_connStr);
		await conn.OpenAsync(ct);
		await using var cmd = new NpgsqlCommand(@"
select date_utc, usdc_equity, eth_equity, btc_equity,
       eth_price, btc_price, eth_equity_usd, btc_equity_usd
from public.van_account_daily_equity
where acc_id = @acc
  and date_utc < @before
order by date_utc desc
limit 1;", conn);
		cmd.Parameters.AddWithValue("acc", accId);
		cmd.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Date) { Value = beforeDate });
		await using var r = await cmd.ExecuteReaderAsync(ct);
		if (!await r.ReadAsync(ct))
			return null;
		static decimal? N(NpgsqlDataReader rdr, string col)
		{
			int ord = rdr.GetOrdinal(col);
			return rdr.IsDBNull(ord) ? (decimal?)null : rdr.GetDecimal(ord);
		}
		var date    = r.GetFieldValue<DateOnly>(r.GetOrdinal("date_utc"));
		return new DailyRow(
			date,
			N(r, "usdc_equity"),
			N(r, "eth_equity"),
			N(r, "btc_equity"),
			N(r, "eth_price"),
			N(r, "btc_price"),
			N(r, "eth_equity_usd"),
			N(r, "btc_equity_usd"));
	}
	/// <summary>
	/// Returns the daily equity row for the exact <paramref name="date"/> if it exists, null otherwise.
	/// Used by anomaly guard to detect if repair cron already wrote today's row.
	/// </summary>
	public async Task<DailyRow?> GetRowForDateAsync(int accId, DateOnly date, CancellationToken ct = default)
	{
		await using var conn = new NpgsqlConnection(_connStr);
		await conn.OpenAsync(ct);
		await using var cmd = new NpgsqlCommand(@"
select date_utc, usdc_equity, eth_equity, btc_equity,
       eth_price, btc_price, eth_equity_usd, btc_equity_usd
from public.van_account_daily_equity
where acc_id = @acc
  and date_utc = @date
limit 1;", conn);
		cmd.Parameters.AddWithValue("acc", accId);
		cmd.Parameters.Add(new NpgsqlParameter("date", NpgsqlDbType.Date) { Value = date });
		await using var r = await cmd.ExecuteReaderAsync(ct);
		if (!await r.ReadAsync(ct))
			return null;
		static decimal? N(NpgsqlDataReader rdr, string col)
		{
			int ord = rdr.GetOrdinal(col);
			return rdr.IsDBNull(ord) ? (decimal?)null : rdr.GetDecimal(ord);
		}
		return new DailyRow(
			r.GetFieldValue<DateOnly>(r.GetOrdinal("date_utc")),
			N(r, "usdc_equity"),
			N(r, "eth_equity"),
			N(r, "btc_equity"),
			N(r, "eth_price"),
			N(r, "btc_price"),
			N(r, "eth_equity_usd"),
			N(r, "btc_equity_usd"));
	}

}
