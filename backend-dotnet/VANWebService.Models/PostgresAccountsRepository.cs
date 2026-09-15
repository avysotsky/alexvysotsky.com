using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using CoreDTO.VANDTO;
using Npgsql;
using NpgsqlTypes;
using VANWebService.Security;

namespace VANWebService.Models;

public sealed class PostgresAccountsRepository : IAccountsRepository
{
	private readonly string connStr;

	private readonly CoreDTO.Logger.Enums.LogAction logAction;

	public PostgresAccountsRepository(string connStr, CoreDTO.Logger.Enums.LogAction logAction)
	{
		this.connStr = connStr;
		this.logAction = logAction;
	}

	public async Task<List<Account>> GetAccountsAsync(CancellationToken ct = default(CancellationToken))
	{
		List<Account> result = new List<Account>();
		List<Account> result2;
		await using (NpgsqlConnection conn = new NpgsqlConnection(connStr))
		{
			await conn.OpenAsync(ct);
			Dictionary<int, Account> accDict = new Dictionary<int, Account>();
			try
			{
				await using NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect acc_id, acc_name,\r\n       clearing_enabled,\r\n       clearing_period,\r\n       clearing_time,\r\n       clearing_day_of_week,\r\n       clearing_day_of_month,\r\n       nearest_clearing_timestamp_utc\r\nfrom van_account\r\norder by acc_id;\r\n", conn);
				await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
				while (await reader.ReadAsync(ct))
				{
					int @int = reader.GetInt32(0);
					string accName = reader.GetString(1);
					bool clearingEnabled = !reader.IsDBNull(2) && reader.GetBoolean(2);
					string clearingPeriod = (reader.IsDBNull(3) ? "None" : reader.GetString(3));
					string clearingTimeUtc = "00:00:00";
					if (!reader.IsDBNull(4))
					{
						clearingTimeUtc = reader.GetTimeSpan(4).ToString("hh\\:mm\\:ss");
					}
					int? clearingDayOfWeek = (reader.IsDBNull(5) ? ((int?)null) : new int?(reader.GetInt32(5)));
					int? clearingDayOfMonth = (reader.IsDBNull(6) ? ((int?)null) : new int?(reader.GetInt32(6)));
					DateTime? nearestClearingTimestampUtc = null;
					if (!reader.IsDBNull(7))
					{
						DateTime dateTime = reader.GetDateTime(7);
						nearestClearingTimestampUtc = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
					}
					accDict[@int] = new Account
					{
						accId = @int,
						accName = accName,
						clearingEnabled = clearingEnabled,
						clearingPeriod = clearingPeriod,
						clearingTimeUtc = clearingTimeUtc,
						clearingDayOfWeek = clearingDayOfWeek,
						clearingDayOfMonth = clearingDayOfMonth,
						nearestClearingTimestampUtc = nearestClearingTimestampUtc,
						listOfCIDs = new List<CID>(),
						listOfRecords = new List<Record>()
					};
				}
			}
			catch (PostgresException ex) when (ex.SqlState == "42703")
			{
				logAction("[PostgresAccountsRepository][GetAccountsAsync][WARNING] Missing clearing columns in van_account: " + ex.MessageText, CoreDTO.Logger.Enums.LogLevel.llExceptions);
				NpgsqlCommand cmd2 = new NpgsqlCommand("\r\nselect acc_id, acc_name\r\nfrom van_account\r\norder by acc_id;\r\n", conn);
				object obj = null;
				try
				{
					await using NpgsqlDataReader reader = await cmd2.ExecuteReaderAsync(ct);
					while (await reader.ReadAsync(ct))
					{
						int int2 = reader.GetInt32(0);
						string accName2 = reader.GetString(1);
						accDict[int2] = new Account
						{
							accId = int2,
							accName = accName2,
							listOfCIDs = new List<CID>(),
							listOfRecords = new List<Record>()
						};
					}
				}
				catch (Exception obj2)
				{
					obj = obj2;
				}
				if (cmd2 != null)
				{
					await cmd2.DisposeAsync();
				}
				if (obj != null)
				{
					throw;
				}
			}
			if (accDict.Count == 0)
			{
				result2 = result;
			}
			else
			{
				await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect acc_id, cid_id, cid_name, fee_rate_percent\r\nfrom van_cid\r\nwhere acc_id = ANY(@acc_ids)\r\norder by acc_id, cid_id;\r\n", conn))
				{
					cmd.Parameters.AddWithValue("acc_ids", accDict.Keys.ToArray());
					await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
					while (await reader.ReadAsync(ct))
					{
						int int3 = reader.GetInt32(0);
						long int4 = reader.GetInt64(1);
						string name = reader.GetString(2);
						decimal num = reader.GetDecimal(3);
						if (accDict.TryGetValue(int3, out Account value))
						{
							CID item = new CID
							{
								InputData = new CIDInputData
								{
									Id = int4,
									Name = name,
									FeeRatePercents = (double)num,
									ShowInTable = true
								},
								OutputData = new CIDOutputData()
							};
							value.listOfCIDs.Add(item);
						}
					}
				}
				result = accDict.Values.ToList();
				logAction($"[AccountsRepo][GetAccounts] loaded {result.Count} accounts", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				result2 = result;
			}
		}
		return result2;
	}

	public async Task<(bool Ok, string Message, int AccId)> CreateAccountAsync(string accName, string? apiPublicKey, string? apiSecretKey, CancellationToken ct = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(accName))
		{
			return (Ok: false, Message: "Account name is empty", AccId: -1);
		}
		await using (NpgsqlConnection conn = new NpgsqlConnection(connStr))
		{
			await conn.OpenAsync(ct);
			(bool Ok, string Message, int AccId) result;
			await using (NpgsqlTransaction tx = await conn.BeginTransactionAsync(ct))
			{
				try
				{
					string text = null;
					if (!string.IsNullOrWhiteSpace(apiSecretKey))
					{
						text = ApiSecretCrypto.Encrypt(apiSecretKey);
					}
					(bool Ok, string Message, int AccId) tuple;
					await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\ninsert into van_account (acc_name, api_public_key, api_secret_cipher, api_secret_hash)\r\nvalues (@name, @api_public_key, @api_secret_cipher, NULL)\r\nreturning acc_id;\r\n", conn, tx))
					{
						cmd.Parameters.AddWithValue("name", accName);
						cmd.Parameters.AddWithValue("api_public_key", string.IsNullOrWhiteSpace(apiPublicKey) ? ((IConvertible)DBNull.Value) : ((IConvertible)apiPublicKey));
						cmd.Parameters.AddWithValue("api_secret_cipher", string.IsNullOrWhiteSpace(text) ? ((IConvertible)DBNull.Value) : ((IConvertible)text));
						int accId = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
						await EnsureAccountEquityPartitionAsync(conn, tx, accId, ct);
						await tx.CommitAsync(ct);
						logAction($"[AccountsRepo][CreateAccount] #{accId} '{accName}' created + equity partition ensured", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
						tuple = (Ok: true, Message: "OK", AccId: accId);
					}
					result = tuple;
				}
				catch (Exception ex)
				{
					try
					{
						await tx.RollbackAsync(ct);
					}
					catch
					{
					}
					logAction($"[AccountsRepo][CreateAccount][ERROR] {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
					result = (Ok: false, Message: ex.Message, AccId: -1);
				}
				goto IL_06c8;
				IL_0606:;
			}
			goto end_IL_00dd;
			IL_06c8:
			return result;
			end_IL_00dd:;
		}
		(bool, string, int) result2;
		return result2;
	}

	public async Task<(bool Ok, string Message)> DeleteAccountAsync(int accId, CancellationToken ct = default(CancellationToken))
	{
		(bool Ok, string Message) result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(connStr))
		{
			await conn.OpenAsync(ct);
			(bool Ok, string Message) tuple;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("delete from van_account where acc_id = @id;", conn))
			{
				cmd.Parameters.AddWithValue("id", accId);
				int num = await cmd.ExecuteNonQueryAsync(ct);
				if (num == 0)
				{
					tuple = (Ok: false, Message: $"Account #{accId} not found");
				}
				else
				{
					logAction($"[AccountsRepo][DeleteAccount] #{accId} deleted (rows={num})", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
					tuple = (Ok: true, Message: "OK");
				}
			}
			result = tuple;
		}
		return result;
	}

	public async Task<(bool Ok, string Message)> CreateCidAsync(int accId, CID cid, CancellationToken ct = default(CancellationToken))
	{
		(bool Ok, string Message) result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(connStr))
		{
			await conn.OpenAsync(ct);
			(bool Ok, string Message) tuple;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\ninsert into van_cid (acc_id, cid_id, cid_name, fee_rate_percent)\r\nvalues (@acc_id, @cid_id, @name, @fee);\r\n", conn))
			{
				cmd.Parameters.AddWithValue("acc_id", accId);
				cmd.Parameters.AddWithValue("cid_id", cid.InputData.Id);
				cmd.Parameters.AddWithValue("name", cid.InputData.Name ?? "");
				cmd.Parameters.AddWithValue("fee", (decimal)cid.InputData.FeeRatePercents);
				await cmd.ExecuteNonQueryAsync(ct);
				logAction($"[AccountsRepo][CreateCid] acc={accId}, cid={cid.InputData.Id} '{cid.InputData.Name}'", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				tuple = (Ok: true, Message: "OK");
			}
			result = tuple;
		}
		return result;
	}

	public async Task<(bool Ok, string Message)> DeleteCidAsync(int accId, long cidId, CancellationToken ct = default(CancellationToken))
	{
		(bool Ok, string Message) result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(connStr))
		{
			await conn.OpenAsync(ct);
			(bool Ok, string Message) tuple;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("delete from van_cid where acc_id = @acc_id and cid_id = @cid_id;", conn))
			{
				cmd.Parameters.AddWithValue("acc_id", accId);
				cmd.Parameters.AddWithValue("cid_id", cidId);
				if (await cmd.ExecuteNonQueryAsync(ct) == 0)
				{
					tuple = (Ok: false, Message: $"CID {cidId} in account {accId} not found");
				}
				else
				{
					logAction($"[AccountsRepo][DeleteCid] acc={accId}, cid={cidId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
					tuple = (Ok: true, Message: "OK");
				}
			}
			result = tuple;
		}
		return result;
	}

	public async Task<(bool Ok, string Message, DateTime? NearestClearingTimestampUtc)> SetAccountClearingScheduleAsync(int accId, bool clearingEnabled, string clearingPeriod, TimeSpan clearingTimeUtc, int? clearingDayOfWeek, int? clearingDayOfMonth, DateTime? nearestClearingTimestampUtc, CancellationToken ct = default(CancellationToken))
	{
		(bool, string, DateTime?) result;
		await using (NpgsqlConnection conn = new NpgsqlConnection(connStr))
		{
			await conn.OpenAsync(ct);
			DateTime? nearestUtc = NormalizeUtcForTimestampWithoutTz(nearestClearingTimestampUtc);
			(bool, string, DateTime?) tuple;
			await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nupdate van_account\r\nset\r\n    clearing_enabled               = @clearing_enabled,\r\n    clearing_period                = @clearing_period,\r\n    clearing_time                  = @clearing_time,\r\n    clearing_day_of_week           = @clearing_day_of_week,\r\n    clearing_day_of_month          = @clearing_day_of_month,\r\n    nearest_clearing_timestamp_utc = @nearest_clearing_timestamp_utc\r\nwhere acc_id = @acc_id;\r\n", conn))
			{
				cmd.Parameters.AddWithValue("acc_id", accId);
				cmd.Parameters.AddWithValue("clearing_enabled", clearingEnabled);
				cmd.Parameters.AddWithValue("clearing_period", clearingPeriod ?? "None");
				cmd.Parameters.AddWithValue("clearing_time", NpgsqlDbType.Time, clearingTimeUtc);
				cmd.Parameters.AddWithValue("clearing_day_of_week", ((object)clearingDayOfWeek) ?? DBNull.Value);
				cmd.Parameters.AddWithValue("clearing_day_of_month", ((object)clearingDayOfMonth) ?? DBNull.Value);
				NpgsqlParameter npgsqlParameter = cmd.Parameters.Add("nearest_clearing_timestamp_utc", NpgsqlDbType.Timestamp);
				npgsqlParameter.Value = ((object)nearestUtc) ?? DBNull.Value;
				if (await cmd.ExecuteNonQueryAsync(ct) == 0)
				{
					tuple = (false, $"Account {accId} not found", null);
				}
				else
				{
					logAction($"[AccountsRepo][SetAccountClearingSchedule] acc={accId}, enabled={clearingEnabled}, period={clearingPeriod}, timeUtc={clearingTimeUtc}, dow={clearingDayOfWeek}, dom={clearingDayOfMonth}, nearestUtc={(nearestUtc.HasValue ? nearestUtc.Value.ToString("yyyy-MM-dd HH:mm:ss.fff") : "null")}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
					tuple = (true, "OK", nearestUtc);
				}
			}
			result = tuple;
		}
		return result;
	}

	private async Task EnsureAccountEquityPartitionAsync(NpgsqlConnection conn, NpgsqlTransaction tx, int accId, CancellationToken ct)
	{
		if (accId <= 0)
		{
			throw new ArgumentOutOfRangeException("accId");
		}
		string text = $"van_account_equity_a{accId}";
		string partQualified = "public." + text;
		string idxName = text + "_acc_ts_desc";
		await using (NpgsqlCommand cmd = new NpgsqlCommand("select to_regclass(@t) is not null;", conn, tx))
		{
			cmd.Parameters.AddWithValue("t", partQualified);
			object obj = await cmd.ExecuteScalarAsync(ct);
			bool b = default(bool);
			int num;
			if (obj is bool)
			{
				b = (bool)obj;
				num = 1;
			}
			else
			{
				num = 0;
			}
			if (((uint)num & (b ? 1u : 0u)) != 0)
			{
				logAction("[AccountsRepo][EnsurePartition] already exists " + partQualified, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return;
			}
		}
		string cmdText = $"\r\nCREATE TABLE IF NOT EXISTS {partQualified}\r\nPARTITION OF public.van_account_equity\r\nFOR VALUES IN ({accId});\r\n\r\nCREATE INDEX IF NOT EXISTS {idxName}\r\nON {partQualified} USING btree (acc_id, timestamp_utc DESC);\r\n";
		await using (NpgsqlCommand cmd = new NpgsqlCommand(cmdText, conn, tx))
		{
			await cmd.ExecuteNonQueryAsync(ct);
		}
		logAction("[AccountsRepo][EnsurePartition] created " + partQualified, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
	}

	private static DateTime? NormalizeUtcForTimestampWithoutTz(DateTime? dt)
	{
		if (!dt.HasValue)
		{
			return null;
		}
		DateTime value = dt.Value;
		if (value.Kind == DateTimeKind.Local)
		{
			value = value.ToUniversalTime();
		}
		if (value.Kind == DateTimeKind.Unspecified)
		{
			value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
		}
		return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
	}
}
