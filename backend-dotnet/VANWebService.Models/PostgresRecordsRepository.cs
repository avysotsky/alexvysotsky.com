using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using CoreDTO.VANDTO;
using Npgsql;

namespace VANWebService.Models;

public sealed class PostgresRecordsRepository : IRecordsRepository
{
	private readonly string _connStr;

	private readonly CoreDTO.Logger.Enums.LogAction _log;

	public PostgresRecordsRepository(string connStr, CoreDTO.Logger.Enums.LogAction log)
	{
		_connStr = connStr;
		_log = log;
	}

	public async Task<long> SaveRecordAsync(Record record, CancellationToken ct = default(CancellationToken))
	{
		long result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync(ct);
			long num;
			await using (NpgsqlTransaction tx = await conn.BeginTransactionAsync(ct))
			{
				long recordId;
				await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\ninsert into van_clearing_record\r\n(acc_id, cid_id, dt_utc, equity_usd, type, new_tot_inv)\r\nvalues (@acc_id, @cid_id, @dt, @equity, @type, @tot_inv)\r\nreturning record_id;\r\n", conn, tx))
				{
					cmd.Parameters.AddWithValue("acc_id", record.accId);
					if (record.cidId >= 0)
					{
						cmd.Parameters.AddWithValue("cid_id", (long)record.cidId);
					}
					else
					{
						cmd.Parameters.AddWithValue("cid_id", DBNull.Value);
					}
					DateTime dateTime = ((record.date.Kind == DateTimeKind.Utc) ? record.date : DateTime.SpecifyKind(record.date, DateTimeKind.Utc));
					cmd.Parameters.AddWithValue("dt", dateTime);
					cmd.Parameters.AddWithValue("equity", (decimal)record.equityUSD);
					cmd.Parameters.AddWithValue("type", record.type.ToString());
					cmd.Parameters.AddWithValue("tot_inv", (decimal)record.newTotallyInvestedUSD);
					recordId = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
				}
				foreach (CID listOfCID in record.listOfCIDs)
				{
					CIDInputData inputData = listOfCID.InputData;
					CIDOutputData outputData = listOfCID.OutputData;
					await using NpgsqlCommand cmd = new NpgsqlCommand("\r\ninsert into van_clearing_record_cid\r\n(record_id, acc_id, deposit_withdrawal_usd,\r\n cid_id, cid_name,\r\n investment_usd, reinvested_usd, client_share_pct,\r\n equity_usd, separator,\r\n pnl_usd, fee_percents, fee_usd, profit_usd,\r\n new_invest_value_usd, new_client_share_percents,\r\n show_in_table,\r\n last_mutation_pnl_percents)\r\nvalues\r\n(@rec_id, @acc_id, @dep_wdr,\r\n @cid_id, @name,\r\n @inv, @reinv, @share,\r\n @equity, @sep,\r\n @pnl, @fee_pct, @fee, @profit,\r\n @new_inv, @new_share,\r\n @show_in_table,\r\n @last_mut_pnl);\r\n", conn, tx);
					cmd.Parameters.AddWithValue("rec_id", recordId);
					cmd.Parameters.AddWithValue("acc_id", record.accId);
					cmd.Parameters.AddWithValue("dep_wdr", (decimal)inputData.DepositWithdrawalValueUSD);
					cmd.Parameters.AddWithValue("cid_id", inputData.Id);
					cmd.Parameters.AddWithValue("name", inputData.Name ?? string.Empty);
					cmd.Parameters.AddWithValue("inv", (decimal)inputData.InvestmentUSD);
					cmd.Parameters.AddWithValue("reinv", (decimal)inputData.ReinvestedUSD);
					cmd.Parameters.AddWithValue("share", inputData.ClientSharePercents);
					cmd.Parameters.AddWithValue("equity", (decimal)outputData.ClientEquityUSD);
					cmd.Parameters.AddWithValue("sep", DBNull.Value);
					cmd.Parameters.AddWithValue("pnl", (decimal)outputData.PnLUSD);
					cmd.Parameters.AddWithValue("fee_pct", (decimal)inputData.FeeRatePercents);
					cmd.Parameters.AddWithValue("fee", (decimal)outputData.FeeUSD);
					cmd.Parameters.AddWithValue("profit", (decimal)outputData.ProfitUSD);
					cmd.Parameters.AddWithValue("new_inv", (decimal)outputData.NewInvestmentValueUSD);
					cmd.Parameters.AddWithValue("new_share", outputData.NewClientSharePercents);
					cmd.Parameters.AddWithValue("show_in_table", inputData.ShowInTable);
					if (inputData.LastMutationPnLPercents.HasValue)
					{
						cmd.Parameters.AddWithValue("last_mut_pnl", (decimal)inputData.LastMutationPnLPercents.Value);
					}
					else
					{
						cmd.Parameters.AddWithValue("last_mut_pnl", DBNull.Value);
					}
					await cmd.ExecuteNonQueryAsync(ct);
				}
				await tx.CommitAsync(ct);
				_log($"[RecordsRepo][SaveRecord] rec={recordId}, acc={record.accId}, cids={record.listOfCIDs.Count}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				num = recordId;
			}
			result = num;
		}
		return result;
	}

	public async Task<List<EquityPoint>> GetAccountEquityCurveAsync(int accId, DateTime? fromUtc = null, CancellationToken ct = default(CancellationToken))
	{
		List<EquityPoint> result = new List<EquityPoint>();
		List<EquityPoint> result2;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync(ct);
			string text = "\r\nselect dt_utc, equity_usd\r\nfrom van_clearing_record\r\nwhere acc_id = @acc_id\r\n";
			if (fromUtc.HasValue)
			{
				text += "  and dt_utc >= @from_utc\n";
			}
			text += "order by dt_utc;";
			List<EquityPoint> list2;
			await using (NpgsqlCommand cmd = new NpgsqlCommand(text, conn))
			{
				cmd.Parameters.AddWithValue("acc_id", accId);
				if (fromUtc.HasValue)
				{
					cmd.Parameters.AddWithValue("from_utc", fromUtc.Value);
				}
				List<EquityPoint> list;
				await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct))
				{
					while (await reader.ReadAsync(ct))
					{
						DateTime dateTime = reader.GetDateTime(0);
						DateTime dtUtc = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
						double equityUsd = (double)reader.GetDecimal(1);
						result.Add(new EquityPoint
						{
							DtUtc = dtUtc,
							EquityUsd = equityUsd
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

	public async Task<List<EquityPoint>> GetFundEquityCurveAsync(DateTime? fromUtc = null, CancellationToken ct = default(CancellationToken))
	{
		List<EquityPoint> result = new List<EquityPoint>();
		List<EquityPoint> result2;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync(ct);
			string text = "\r\nselect dt_utc,\r\n       sum(equity_usd) as equity_total\r\nfrom van_clearing_record\r\n";
			if (fromUtc.HasValue)
			{
				text += "where dt_utc >= @from_utc\n";
			}
			text += "group by dt_utc\norder by dt_utc;";
			List<EquityPoint> list2;
			await using (NpgsqlCommand cmd = new NpgsqlCommand(text, conn))
			{
				if (fromUtc.HasValue)
				{
					cmd.Parameters.AddWithValue("from_utc", fromUtc.Value);
				}
				List<EquityPoint> list;
				await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct))
				{
					while (await reader.ReadAsync(ct))
					{
						DateTime dateTime = reader.GetDateTime(0);
						DateTime dtUtc = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
						double equityUsd = (double)reader.GetDecimal(1);
						result.Add(new EquityPoint
						{
							DtUtc = dtUtc,
							EquityUsd = equityUsd
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

	public async Task<List<Record>> GetAccountRecordsAsync(int accId, CancellationToken ct = default(CancellationToken))
	{
		List<Record> result = new List<Record>();
		List<Record> result3;
		await using (NpgsqlConnection conn = new NpgsqlConnection(_connStr))
		{
			await conn.OpenAsync(ct);
			Dictionary<long, Record> recordsById = new Dictionary<long, Record>();
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect record_id, acc_id, cid_id, dt_utc, equity_usd, type, new_tot_inv\r\nfrom van_clearing_record\r\nwhere acc_id = @acc_id\r\norder by dt_utc, record_id;\r\n", conn))
			{
				cmd.Parameters.AddWithValue("acc_id", accId);
				await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
				while (await reader.ReadAsync(ct))
				{
					long @int = reader.GetInt64(0);
					int int2 = reader.GetInt32(1);
					int cidId = (reader.IsDBNull(2) ? (-1) : checked((int)reader.GetInt64(2)));
					DateTime dateTime = reader.GetDateTime(3);
					DateTime date = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
					double equityUSD = (double)reader.GetDecimal(4);
					string value = reader.GetString(5);
					double newTotallyInvestedUSD = (double)reader.GetDecimal(6);
					if (!Enum.TryParse<MutationType>(value, ignoreCase: true, out var result2))
					{
						result2 = MutationType.Idle;
					}
					recordsById[@int] = new Record
					{
						accId = int2,
						cidId = cidId,
						date = date,
						equityUSD = equityUSD,
						type = result2,
						newTotallyInvestedUSD = newTotallyInvestedUSD,
						listOfCIDs = new List<CID>()
					};
				}
			}
			if (recordsById.Count == 0)
			{
				_log($"[RecordsRepo][GetAccountRecordsAsync] no records for accId={accId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				result3 = result;
			}
			else
			{
				await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect record_id,\r\n       cid_id,\r\n       cid_name,\r\n       investment_usd,\r\n       reinvested_usd,\r\n       client_share_pct,\r\n       equity_usd,\r\n       pnl_usd,\r\n       fee_percents,\r\n       fee_usd,\r\n       profit_usd,\r\n       deposit_withdrawal_usd,\r\n       new_invest_value_usd,\r\n       new_client_share_percents,\r\n       show_in_table,\r\n       last_mutation_pnl_percents\r\nfrom van_clearing_record_cid\r\nwhere acc_id = @acc_id\r\norder by record_id, cid_id;\r\n", conn))
				{
					cmd.Parameters.AddWithValue("acc_id", accId);
					await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
					while (await reader.ReadAsync(ct))
					{
						long int3 = reader.GetInt64(0);
						if (recordsById.TryGetValue(int3, out Record value2))
						{
							long int4 = reader.GetInt64(1);
							string name = (reader.IsDBNull(2) ? null : reader.GetString(2));
							double num = (double)reader.GetDecimal(3);
							double num2 = (double)reader.GetDecimal(4);
							decimal num3 = reader.GetDecimal(5);
							double num4 = (double)reader.GetDecimal(6);
							double pnLUSD = (double)reader.GetDecimal(7);
							double feeRatePercents = (reader.IsDBNull(8) ? 0.0 : ((double)reader.GetDecimal(8)));
							double feeUSD = (double)reader.GetDecimal(9);
							double profitUSD = (reader.IsDBNull(10) ? 0.0 : ((double)reader.GetDecimal(10)));
							double depositWithdrawalValueUSD = (double)reader.GetDecimal(11);
							double newInvestmentValueUSD = (reader.IsDBNull(12) ? num4 : ((double)reader.GetDecimal(12)));
							decimal newClientSharePercents = (reader.IsDBNull(13) ? num3 : reader.GetDecimal(13));
							bool showInTable = reader.IsDBNull(14) || reader.GetBoolean(14);
							double? lastMutationPnLPercents = (reader.IsDBNull(15) ? ((double?)null) : new double?((double)reader.GetDecimal(15)));
							CID item = new CID
							{
								InputData = new CIDInputData
								{
									Id = int4,
									Name = name,
									InvestmentUSD = num,
									ReinvestedUSD = num2,
									ClientSharePercents = num3,
									DepositWithdrawalValueUSD = depositWithdrawalValueUSD,
									FeeRatePercents = feeRatePercents,
									ShowInTable = showInTable,
									LastMutationPnLPercents = lastMutationPnLPercents
								},
								OutputData = new CIDOutputData
								{
									ClientEquityUSD = num4,
									PnLUSD = pnLUSD,
									FeeUSD = feeUSD,
									ProfitUSD = profitUSD,
									NewInvestmentValueUSD = newInvestmentValueUSD,
									NewClientSharePercents = newClientSharePercents,
									TotallyInvestedUSD = num + num2
								}
							};
							value2.listOfCIDs.Add(item);
						}
					}
				}
				result = (from kv in recordsById
					orderby kv.Value.date
					select kv.Value).ToList();
				_log($"[RecordsRepo][GetAccountRecordsAsync] loaded {result.Count} records for accId={accId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				result3 = result;
			}
		}
		return result3;
	}
}
