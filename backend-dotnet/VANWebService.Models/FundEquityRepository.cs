using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using CoreDTO.VANDTO;
using Npgsql;
using NpgsqlTypes;

namespace VANWebService.Models;

public sealed class FundEquityRepository
{
	private readonly string _connStr;

	private readonly CoreDTO.Logger.Enums.LogAction _log;

	private static readonly DateOnly PatchAnchorBefore = new DateOnly(2024, 6, 27);

	private static readonly DateOnly PatchStart = new DateOnly(2024, 6, 28);

	private static readonly DateOnly PatchEnd = new DateOnly(2024, 7, 15);

	public FundEquityRepository(VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log)
	{
		_connStr = cfg.AuthorityConnectionString;
		_log = log;
	}

	public async Task<DateOnly?> GetMaxDateAsync()
	{
		DateOnly? result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync();
			DateOnly? dateOnly;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("select max(date_utc) from self_custody_fund_equity_curve;", conn))
			{
				object obj = await cmd.ExecuteScalarAsync();
				dateOnly = ((obj != null && !(obj is DBNull)) ? new DateOnly?(DateOnly.FromDateTime(((DateTime)obj).Date)) : ((DateOnly?)null));
			}
			result = dateOnly;
		}
		return result;
	}

	public async Task UpsertRangeAsync(IReadOnlyCollection<(DateOnly Date, double TotalEquityUsd, double EthUsd)> rows, CancellationToken ct = default(CancellationToken))
	{
		if (rows.Count == 0)
		{
			return;
		}
		await using NpgsqlConnection conn = new NpgsqlConnection(_connStr);
		await conn.OpenAsync(ct);
		List<(DateOnly Date, double TotalEquityUsd, double EthUsd)> list = rows.ToList();
		bool needPatch = false;
		await using (NpgsqlCommand checkCmd = new NpgsqlCommand("\r\nselect count(1)\r\nfrom self_custody_fund_equity_curve\r\nwhere date_utc >= @from and date_utc <= @to;\r\n", conn))
		{
			checkCmd.Parameters.AddWithValue("from", NpgsqlDbType.Date, PatchStart);
			checkCmd.Parameters.AddWithValue("to", NpgsqlDbType.Date, PatchEnd);
			long num = Convert.ToInt64((await checkCmd.ExecuteScalarAsync(ct)) ?? ((object)0L));
			needPatch = num == 0;
		}
		if (needPatch)
		{
			(DateOnly, double, double) tuple = list.FirstOrDefault(((DateOnly Date, double TotalEquityUsd, double EthUsd) r) => r.Date == PatchAnchorBefore);
			(DateOnly, double, double) tuple2 = list.FirstOrDefault(((DateOnly Date, double TotalEquityUsd, double EthUsd) r) => r.Date == PatchEnd);
			bool flag = list.Any(((DateOnly Date, double TotalEquityUsd, double EthUsd) r) => r.Date == PatchAnchorBefore);
			bool flag2 = list.Any(((DateOnly Date, double TotalEquityUsd, double EthUsd) r) => r.Date == PatchEnd);
			if (flag && flag2)
			{
				double item = tuple.Item2;
				double item2 = tuple.Item3;
				double item3 = tuple2.Item2;
				double item4 = tuple2.Item3;
				int num2 = PatchEnd.DayNumber - PatchAnchorBefore.DayNumber;
				if (num2 > 0)
				{
					_log($"[FundEquityRepo] Applying linear patch {PatchStart:yyyy-MM-dd}..{PatchEnd:yyyy-MM-dd} between {PatchAnchorBefore:yyyy-MM-dd} and {PatchEnd:yyyy-MM-dd}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
					Dictionary<DateOnly, (double, double)> dictionary = list.ToDictionary(((DateOnly Date, double TotalEquityUsd, double EthUsd) r) => r.Date, ((DateOnly Date, double TotalEquityUsd, double EthUsd) r) => (TotalEquityUsd: r.TotalEquityUsd, EthUsd: r.EthUsd));
					DateOnly dateOnly = PatchStart;
					while (dateOnly <= PatchEnd)
					{
						double num3 = (double)(dateOnly.DayNumber - PatchAnchorBefore.DayNumber) / (double)num2;
						double item5 = item + (item3 - item) * num3;
						double item6 = item2 + (item4 - item2) * num3;
						dictionary[dateOnly] = (item5, item6);
						dateOnly = dateOnly.AddDays(1);
					}
					list = (from kvp in dictionary
						select (Key: kvp.Key, kvp.Value.Item1, kvp.Value.Item2) into x
						orderby x.Key
						select x).ToList();
				}
			}
		}
		foreach (var (dateOnly2, num4, num5) in list)
		{
			await using NpgsqlCommand checkCmd = new NpgsqlCommand("\r\ninsert into self_custody_fund_equity_curve (date_utc, total_equity_usd, eth_usd)\r\nvalues (@d, @eq, @eth)\r\non conflict (date_utc) do update\r\nset total_equity_usd = excluded.total_equity_usd,\r\n    eth_usd          = excluded.eth_usd;\r\n", conn);
			checkCmd.Parameters.AddWithValue("@d", dateOnly2.ToDateTime(new TimeOnly(0, 0)));
			checkCmd.Parameters.AddWithValue("@eq", num4);
			checkCmd.Parameters.AddWithValue("@eth", num5);
			await checkCmd.ExecuteNonQueryAsync(ct);
		}
	}

	public async Task<List<FundEquityPointDTO>> GetLastDaysAsync(int days)
	{
		List<FundEquityPointDTO> result = new List<FundEquityPointDTO>();
		DateOnly from = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(1 - days);
		List<FundEquityPointDTO> result2;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync();
			List<FundEquityPointDTO> list2;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\n            select date_utc, total_equity_usd\r\n            from self_custody_fund_equity_curve\r\n            where date_utc >= @from\r\n            order by date_utc;\r\n        ", conn))
			{
				cmd.Parameters.AddWithValue("from", NpgsqlDbType.Date, from);
				List<FundEquityPointDTO> list;
				await using (NpgsqlDataReader rdr = await cmd.ExecuteReaderAsync())
				{
					while (await rdr.ReadAsync())
					{
						DateTime fieldValue = rdr.GetFieldValue<DateTime>(0);
						double num = rdr.GetDouble(1);
						DateOnly dateOnly = DateOnly.FromDateTime(fieldValue.Date);
						result.Add(new FundEquityPointDTO
						{
							dt = dateOnly.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
							equityUsd = (decimal)num
						});
					}
					list = result;
				}
				list2 = list;
			}
			result2 = list2;
		}
		return result2;
	}

	public async Task<DateOnly?> GetLastDateAsync(CancellationToken ct = default(CancellationToken))
	{
		DateOnly? result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync(ct);
			DateOnly? dateOnly;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\n        SELECT MAX(date_utc)\r\n        FROM self_custody_fund_equity_curve;\r\n    ", conn))
			{
				object obj = await cmd.ExecuteScalarAsync(ct);
				if (obj == null || obj is DBNull)
				{
					dateOnly = null;
				}
				else
				{
					DateOnly value = ((obj is DateOnly dateOnly2) ? dateOnly2 : ((!(obj is DateTime dateTime)) ? DateOnly.FromDateTime(Convert.ToDateTime(obj)) : DateOnly.FromDateTime(dateTime)));
					dateOnly = value;
				}
			}
			result = dateOnly;
		}
		return result;
	}

	public async Task<List<FundEquityPointDTO>> GetAllAsync(CancellationToken ct = default(CancellationToken))
	{
		List<FundEquityPointDTO> result = new List<FundEquityPointDTO>();
		List<FundEquityPointDTO> result2;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync(ct);
			List<FundEquityPointDTO> list2;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\n        select date_utc, total_equity_usd, eth_usd\r\n        from self_custody_fund_equity_curve\r\n        order by date_utc;\r\n    ", conn))
			{
				List<FundEquityPointDTO> list;
				await using (NpgsqlDataReader rdr = await cmd.ExecuteReaderAsync(ct))
				{
					while (await rdr.ReadAsync(ct))
					{
						DateTime fieldValue = rdr.GetFieldValue<DateTime>(0);
						double num = rdr.GetDouble(1);
						double? num2 = (rdr.IsDBNull(2) ? ((double?)null) : new double?(rdr.GetDouble(2)));
						result.Add(new FundEquityPointDTO
						{
							dt = fieldValue,
							equityUsd = (decimal)num,
							ethUsd = (num2.HasValue ? new decimal?((decimal)num2.Value) : ((decimal?)null))
						});
					}
					list = result;
				}
				list2 = list;
			}
			result2 = list2;
		}
		return result2;
	}
}
