using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using CoreDTO.VANDTO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;
using VANWebService.Auth;
using VANWebService.Models;
using VANWebService.Security;

namespace VANWebService;

public static class CoreEndpoints
{
	public static void MapCoreEndpoints(this WebApplication app)
	{
		app.MapGet("ping", (Func<HttpContext, CoreDTO.Logger.Enums.LogAction, Task>)async delegate(HttpContext context, CoreDTO.Logger.Enums.LogAction logAction)
		{
			logAction("[GET] /ping", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			var value = new
			{
				version = Assembly.GetExecutingAssembly().GetName().Version.ToString()
			};
			await context.Response.WriteAsJsonAsync(value);
		});
		app.MapGet("getListOfAccounts", (Func<HttpContext, Orchestrator, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext ctx, Orchestrator orchestrator, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			List<Account> accounts = orchestrator.GetAccounts().ToList();
			int? activeMappedAccId = null;
			UserSession userSession = UserSessionTools.GetUserSession(ctx);
			if (userSession != null)
			{
				activeMappedAccId = await GetMappedDeribitAccountIdAsync(cfg, userSession, ctx.RequestAborted);
			}
			if (accounts.Count == 0)
			{
				return Results.Json(new GetListOfAccountsResponseDTO
				{
					listOfAccounts = accounts
				});
			}
			try
			{
				int[] accIds = accounts.Select((Account a) => a.accId).Distinct().ToArray();
				Dictionary<int, (bool usdc, bool btc, bool eth)> flagsByAccId = new Dictionary<int, (bool, bool, bool)>();
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync(ctx.RequestAborted);
				await using NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect\r\n  acc_id,\r\n  include_usdc_in_total,\r\n  include_btc_in_total,\r\n  include_eth_in_total\r\nfrom public.van_account\r\nwhere acc_id = any(@acc_ids);\r\n", conn);
				cmd.Parameters.AddWithValue("acc_ids", accIds);
				await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ctx.RequestAborted);
				int ordAccId = r.GetOrdinal("acc_id");
				int ordUsdc = r.GetOrdinal("include_usdc_in_total");
				int ordBtc = r.GetOrdinal("include_btc_in_total");
				int ordEth = r.GetOrdinal("include_eth_in_total");
				while (await r.ReadAsync(ctx.RequestAborted))
				{
					int @int = r.GetInt32(ordAccId);
					bool item = r.IsDBNull(ordUsdc) || r.GetBoolean(ordUsdc);
					bool item2 = r.IsDBNull(ordBtc) || r.GetBoolean(ordBtc);
					bool item3 = r.IsDBNull(ordEth) || r.GetBoolean(ordEth);
					flagsByAccId[@int] = (item, item2, item3);
				}
				foreach (Account item4 in accounts)
				{
					if (flagsByAccId.TryGetValue(item4.accId, out (bool, bool, bool) value))
					{
						(item4.includeUsdcInTotal, item4.includeBtcInTotal, item4.includeEthInTotal) = value;
					}
					else
					{
						item4.includeUsdcInTotal = true;
						item4.includeBtcInTotal = true;
						item4.includeEthInTotal = true;
					}
				}
			}
			catch (Exception value2)
			{
				foreach (Account item5 in accounts)
				{
					if (!item5.includeUsdcInTotal)
					{
						item5.includeUsdcInTotal = true;
					}
					if (!item5.includeBtcInTotal)
					{
						item5.includeBtcInTotal = true;
					}
					if (!item5.includeEthInTotal)
					{
						item5.includeEthInTotal = true;
					}
				}
				logAction($"[getListOfAccounts][WARN] include flags load failed: {value2}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			if (activeMappedAccId.HasValue)
			{
				accounts = accounts
					.OrderBy((Account a) => a.accId == activeMappedAccId.Value ? 0 : 1)
					.ThenBy((Account a) => a.accId)
					.ToList();
			}
			return Results.Json(new GetListOfAccountsResponseDTO
			{
				listOfAccounts = accounts
			});
		});
		app.MapGet("/api/getAccountRecords", (Func<HttpContext, IRecordsRepository, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, IRecordsRepository recordsRepo, CoreDTO.Logger.Enums.LogAction logAction)
		{
			if (!context.Request.Query.TryGetValue("accId", out var value) || !int.TryParse(value, out var accId))
			{
				return Results.BadRequest("Query parameter 'accId' is required and must be an integer.");
			}
			try
			{
				List<Record> list = await recordsRepo.GetAccountRecordsAsync(accId, context.RequestAborted);
				GetAccountRecordsResponseDTO data = new GetAccountRecordsResponseDTO
				{
					accId = accId,
					records = list
				};
				logAction($"[GET] /api/getAccountRecords accId={accId} -> {list.Count} records", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Json(data);
			}
			catch (Exception value2)
			{
				logAction($"[GET] /api/getAccountRecords[ERROR] accId={accId}: {value2}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.Problem("Internal server error");
			}
		});
		app.MapPost("api/mutations", (Func<AccMutationDTO, Orchestrator, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(AccMutationDTO dto, Orchestrator orchestrator, CoreDTO.Logger.Enums.LogAction logAction)
		{
			logAction($"[POST] /{"api/mutations"} type = {dto.mutationType}, accId = {dto.acc?.accId}, cidId = {dto.cidId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			if (dto.mutationType == MutationType.Clearing)
			{
				try
				{
					string value = JsonSerializer.Serialize(dto, new JsonSerializerOptions
					{
						WriteIndented = false
					});
					logAction($"[ClearingRequest] accId={dto.accId}, accName='{dto.accName}', equityUsd={dto.equityUsd}, dt={dto.dt:O}, rawDto={value}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				}
				catch (Exception value2)
				{
					logAction($"[ClearingRequest][SerializeError] {value2}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				}
			}
			orchestrator.OnNext(dto);
			orchestrator.mutationState = CoreDTO.VANDTO.Enums.MutationState.Idle;
			while (orchestrator.mutationState != CoreDTO.VANDTO.Enums.MutationState.Completed)
			{
				Thread.Sleep(100);
			}
			Thread.Sleep(100);
			MutationResponseDTO mutationResponseDTO = new MutationResponseDTO
			{
				status = orchestrator.mutationResultStatus,
				resultText = orchestrator.mutationResultText,
				equityUSD = orchestrator.equityUSD
			};
			if (orchestrator.mutationResultStatus)
			{
				mutationResponseDTO.accName = dto.accName;
				mutationResponseDTO.cidId = dto.cidId;
				mutationResponseDTO.cidName = dto.cidName;
			}
			return Results.Json(mutationResponseDTO);
		});
		app.MapPost("api/createAccount", (Func<AccMutationDTO, IAccountsRepository, Orchestrator, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, IAccountDailyEquityBackfillQueue, HttpContext, Task<IResult>>)async delegate(AccMutationDTO dto, IAccountsRepository repo, Orchestrator orchestrator, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log, IAccountDailyEquityBackfillQueue dailyEqQueue, HttpContext http)
		{
			log($"[POST] /{"api/createAccount"}: accountName = {dto.accName}, apiPublicKey = {dto.apiPublicKey ?? "<null>"}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			var (flag, text, accId) = await repo.CreateAccountAsync(dto.accName ?? string.Empty, dto.apiPublicKey, dto.apiSecretKeyPlain, http.RequestAborted);
			if (!flag)
			{
				log("Account was NOT created. Reason: " + text + ".", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Conflict(new
				{
					message = text
				});
			}
			try
			{
				await Boot.EnsureAccountEquityPartitionAsync(accId, cfg.AuthorityConnectionString, log, http.RequestAborted);
				log($"[AccountEquity][Partition] ensured for accId={accId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			catch (Exception value)
			{
				log($"[AccountEquity][Partition][ERROR] accId={accId}: {value}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.Problem($"Failed to create van_account_equity partition for account {accId}: {value}");
			}
			if (!string.IsNullOrWhiteSpace(dto.apiPublicKey) && !string.IsNullOrWhiteSpace(dto.apiSecretKeyPlain))
			{
				try
				{
					await Boot.EnsureAccountDailyEquityPartitionAsync(accId, cfg.AuthorityConnectionString, log, http.RequestAborted);
					log($"[AccountDailyEquity][Partition] ensured for accId={accId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
					dailyEqQueue.Enqueue(accId);
					log($"[AccountDailyEquity][Backfill][Enqueue] accId={accId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				}
				catch (Exception value2)
				{
					log($"[AccountDailyEquity][Partition/Enqueue][ERROR] accId={accId}: {value2}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
					return Results.Problem($"Failed to create van_account_daily_equity partition / enqueue backfill for account {accId}: {value2}");
				}
			}
			Account account = new Account
			{
				accId = accId,
				accName = (dto.accName ?? string.Empty),
				listOfCIDs = new List<CID>(),
				listOfRecords = new List<Record>()
			};
			orchestrator.AddAccount(account);
			orchestrator.accountsDataCache.OnNext(new AccountInfoDTO
			{
				accId = account.accId,
				equityUSD = 0.0
			});
			log($"Account {dto.accName} with Id {account.accId} is created successfully.", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return Results.Json(new CreateAccountResponseDTO
			{
				status = true,
				accId = account.accId,
				accName = account.accName
			});
		});
		app.MapPost("api/admin/deribit/test-credentials", (Func<DeribitCredentialsRequest, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, HttpContext, Task<IResult>>)async delegate(DeribitCredentialsRequest dto, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log, HttpContext http)
		{
			string apiPublicKey = dto?.ApiPublicKeyValue ?? string.Empty;
			string apiSecretKeyPlain = dto?.ApiSecretValue ?? string.Empty;
			if ((string.IsNullOrWhiteSpace(apiPublicKey) || string.IsNullOrWhiteSpace(apiSecretKeyPlain)) && dto != null && dto.accId.HasValue)
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync(http.RequestAborted);
				await using NpgsqlCommand cmd = new NpgsqlCommand(@"
select api_public_key, api_secret_cipher
from public.van_account
where acc_id = @acc_id
limit 1;
", conn);
				cmd.Parameters.AddWithValue("acc_id", dto.accId.Value);
				await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(http.RequestAborted);
				if (!await reader.ReadAsync(http.RequestAborted))
				{
					return Results.NotFound(new { message = "Deribit account not found" });
				}
				apiPublicKey = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
				string secretCipher = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
				if (!string.IsNullOrWhiteSpace(secretCipher))
				{
					apiSecretKeyPlain = ApiSecretCrypto.Decrypt(secretCipher);
				}
			}
			if (string.IsNullOrWhiteSpace(apiPublicKey) || string.IsNullOrWhiteSpace(apiSecretKeyPlain))
			{
				return Results.BadRequest(new
				{
					message = "Deribit API key and secret are required"
				});
			}
			try
			{
				DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
				DeribitSettlementExporter exporter = new DeribitSettlementExporter(cfg.DeribitBaseUrl, apiPublicKey, apiSecretKeyPlain, today, today, "/tmp/van_deribit_credential_test.csv", log);
				await exporter.ValidateCredentialsAsync();
				log($"[POST] /api/admin/deribit/test-credentials: OK apiPublicKey={apiPublicKey}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Json(new
				{
					status = true,
					message = "Deribit credentials validated"
				});
			}
			catch (Exception ex)
			{
				string message = IsDeribitInvalidCredentials(ex) ? "Deribit rejected these credentials" : "Deribit credential validation failed: " + ex.Message;
				log($"[POST] /api/admin/deribit/test-credentials: failed apiPublicKey={apiPublicKey}: {ex}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.BadRequest(new
				{
					status = false,
					message
				});
			}
		});
		app.MapPut("api/admin/deribit/accounts/{id:int}/activate", (Func<int, VANWebServiceConfig, HttpContext, Task<IResult>>)async delegate(int id, VANWebServiceConfig cfg, HttpContext http)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			(bool ok, string message, int userId) = await SetActiveDeribitAccountAsync(cfg, session, id, http.RequestAborted);
			if (!ok)
			{
				return message == "Deribit account not found"
					? Results.NotFound(new { message })
					: Results.BadRequest(new { message });
			}
			return Results.Json(new { ok = true, accountId = id, userId });
		});
		app.MapGet("api/admin/deribit/summary", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext http, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			int? accountId = null;
			if (http.Request.Query.TryGetValue("accountId", out var accountIdValue) && int.TryParse(accountIdValue.ToString(), out int parsedAccountId))
			{
				accountId = parsedAccountId;
			}
			accountId ??= await GetMappedDeribitAccountIdAsync(cfg, session, http.RequestAborted);
			DeribitAccountCredentials? account = await LoadDeribitAccountCredentialsAsync(cfg, accountId, http.RequestAborted);
			if (account == null)
			{
				return Results.NotFound(new { message = "No Deribit account selected" });
			}
			if (string.IsNullOrWhiteSpace(account.ApiPublicKey) || string.IsNullOrWhiteSpace(account.ApiSecretCipher))
			{
				return Results.BadRequest(new { message = "Selected Deribit account has no API credentials", account = ToDeribitPublicAccount(account) });
			}
			string apiSecretPlain;
			try
			{
				apiSecretPlain = ApiSecretCrypto.Decrypt(account.ApiSecretCipher);
			}
			catch (Exception ex)
			{
				log($"[GET] /api/admin/deribit/summary: failed to decrypt account #{account.AccId}: {ex.Message}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Selected Deribit account secret cannot be decrypted", account = ToDeribitPublicAccount(account) });
			}
			try
			{
				DeribitAccountSummary summary = await FetchDeribitAccountSummaryAsync(cfg, account, apiSecretPlain, http.RequestAborted);
				return Results.Json(new
				{
					account = ToDeribitPublicAccount(account),
					data = summary.Rows,
					metrics = new
					{
						totalEquity = summary.TotalEquityUsd,
						availableEquity = summary.AvailableUsd,
						initialMargin = summary.InitialMarginUsd,
						maintenanceMargin = summary.MaintenanceMarginUsd,
						initialMarginRate = summary.TotalEquityUsd > 0 && summary.InitialMarginUsd.HasValue ? summary.InitialMarginUsd.Value / summary.TotalEquityUsd.Value : (double?)null,
						maintenanceMarginRate = summary.TotalEquityUsd > 0 && summary.MaintenanceMarginUsd.HasValue ? summary.MaintenanceMarginUsd.Value / summary.TotalEquityUsd.Value : (double?)null,
						marginMode = summary.PortfolioMarginingEnabled ? "PM" : "",
						marginModeSource = summary.PortfolioMarginingObserved ? "Deribit private/get_account_summary portfolio_margining_enabled" : "Deribit private/get_account_summary did not return portfolio_margining_enabled",
						equityCurrency = "USD"
					},
					fetchedAtUtc = DateTime.UtcNow,
					source = "Deribit private/get_account_summary extended=true"
				});
			}
			catch (Exception ex)
			{
				log($"[GET] /api/admin/deribit/summary: account #{account.AccId} failed: {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Deribit account summary failed: " + ex.Message, account = ToDeribitPublicAccount(account) });
			}
		});
		app.MapGet("api/admin/deribit/{market}/orders/open", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, string, Task<IResult>>)async delegate(HttpContext http, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log, string market)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			int? accountId = null;
			if (http.Request.Query.TryGetValue("accountId", out var accountIdValue) && int.TryParse(accountIdValue.ToString(), out int parsedAccountId))
			{
				accountId = parsedAccountId;
			}
			accountId ??= await GetMappedDeribitAccountIdAsync(cfg, session, http.RequestAborted);
			DeribitAccountCredentials? account = await LoadDeribitAccountCredentialsAsync(cfg, accountId, http.RequestAborted);
			if (account == null)
			{
				return Results.NotFound(new { message = "No Deribit account selected" });
			}
			if (string.IsNullOrWhiteSpace(account.ApiPublicKey) || string.IsNullOrWhiteSpace(account.ApiSecretCipher))
			{
				return Results.BadRequest(new { message = "Selected Deribit account has no API credentials", account = ToDeribitPublicAccount(account) });
			}
			string apiSecretPlain;
			try
			{
				apiSecretPlain = ApiSecretCrypto.Decrypt(account.ApiSecretCipher);
			}
			catch (Exception ex)
			{
				log($"[GET] /api/admin/deribit/{market}/orders/open: failed to decrypt account #{account.AccId}: {ex.Message}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Selected Deribit account secret cannot be decrypted", account = ToDeribitPublicAccount(account) });
			}
			try
			{
				string normalizedMarket = string.Equals(market, "spot", StringComparison.OrdinalIgnoreCase) ? "spot" : "futures";
				List<DeribitOpenOrderRow> orders = await FetchDeribitOpenOrdersAsync(cfg, account, apiSecretPlain, normalizedMarket, http.RequestAborted);
				return Results.Json(new
				{
					account = ToDeribitPublicAccount(account),
					market = normalizedMarket,
					data = orders,
					rows = orders,
					count = orders.Count,
					fetchedAtUtc = DateTime.UtcNow,
					source = "Deribit private/get_open_orders_by_currency"
				});
			}
			catch (Exception ex)
			{
				log($"[GET] /api/admin/deribit/{market}/orders/open: account #{account.AccId} failed: {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Deribit open orders failed: " + ex.Message, account = ToDeribitPublicAccount(account) });
			}
		});
		app.MapGet("api/admin/deribit/futures/orders/history", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext http, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null) return Results.Unauthorized();
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
			DeribitPrivateRequestContext request = await BuildDeribitPrivateRequestContextAsync(http, cfg, log, "orders/history");
			if (request.Result != null) return request.Result;
			try
			{
				string instrument = NormalizeDeribitFuturesInstrument(http.Request.Query["symbol"].ToString());
				int count = ReadDeribitQueryCount(http, 100, 1000);
				JsonElement result = await FetchDeribitOrderHistoryAsync(cfg, request.Account!, request.ApiSecretPlain!, instrument, count, http.RequestAborted);
				List<object> rows = new List<object>();
				if (result.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement order in result.EnumerateArray())
					{
						rows.Add(ToDeribitOpenOrderRow(order, "futures"));
					}
				}
				string source = "Deribit private/get_order_history_by_instrument";
				if (rows.Count == 0)
				{
					(long startMs, long endMs) = ReadDeribitRangeMs(http, TimeSpan.FromDays(90));
					List<JsonElement> tradeLogs = await FetchDeribitTradeTransactionLogsAsync(cfg, request.Account!, request.ApiSecretPlain!, instrument, count, startMs, endMs, http.RequestAborted);
					rows = tradeLogs.Select(logRow => (object)ToDeribitTransactionOrderHistoryRow(logRow, instrument)).ToList();
					if (rows.Count > 0)
					{
						source = "Deribit private/get_transaction_log query=trade fallback";
					}
				}
				return Results.Json(new { account = ToDeribitPublicAccount(request.Account!), market = "futures", instrument, data = rows, rows, count = rows.Count, fetchedAtUtc = DateTime.UtcNow, source });
			}
			catch (Exception ex)
			{
				log($"[GET] /api/admin/deribit/futures/orders/history: account #{request.Account!.AccId} failed: {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Deribit order history failed: " + ex.Message, account = ToDeribitPublicAccount(request.Account!) });
			}
		});
		app.MapGet("api/admin/deribit/futures/trades/history", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext http, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null) return Results.Unauthorized();
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
			DeribitPrivateRequestContext request = await BuildDeribitPrivateRequestContextAsync(http, cfg, log, "trades/history");
			if (request.Result != null) return request.Result;
			try
			{
				string instrument = NormalizeDeribitFuturesInstrument(http.Request.Query["symbol"].ToString());
				int count = ReadDeribitQueryCount(http, 100, 1000);
				JsonElement result = await FetchDeribitTradeHistoryAsync(cfg, request.Account!, request.ApiSecretPlain!, instrument, count, http.RequestAborted);
				List<object> rows = new List<object>();
				if (result.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement trade in result.EnumerateArray())
					{
						rows.Add(ToDeribitTradeHistoryRow(trade, instrument));
					}
				}
				string source = "Deribit private/get_user_trades_by_instrument";
				if (rows.Count == 0)
				{
					(long startMs, long endMs) = ReadDeribitRangeMs(http, TimeSpan.FromDays(90));
					List<JsonElement> tradeLogs = await FetchDeribitTradeTransactionLogsAsync(cfg, request.Account!, request.ApiSecretPlain!, instrument, count, startMs, endMs, http.RequestAborted);
					rows = tradeLogs.Select(logRow => ToDeribitTradeHistoryRow(logRow, instrument)).ToList();
					if (rows.Count > 0)
					{
						source = "Deribit private/get_transaction_log query=trade fallback";
					}
				}
				return Results.Json(new { account = ToDeribitPublicAccount(request.Account!), market = "futures", instrument, data = rows, rows, count = rows.Count, fetchedAtUtc = DateTime.UtcNow, source });
			}
			catch (Exception ex)
			{
				log($"[GET] /api/admin/deribit/futures/trades/history: account #{request.Account!.AccId} failed: {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Deribit trade history failed: " + ex.Message, account = ToDeribitPublicAccount(request.Account!) });
			}
		});
		app.MapGet("api/admin/deribit/futures/funding/history", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext http, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null) return Results.Unauthorized();
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
			DeribitPrivateRequestContext request = await BuildDeribitPrivateRequestContextAsync(http, cfg, log, "funding/history");
			if (request.Result != null) return request.Result;
			try
			{
				string instrument = NormalizeDeribitFuturesInstrument(http.Request.Query["symbol"].ToString());
				(long startMs, long endMs) = ReadDeribitRangeMs(http, TimeSpan.FromDays(30));
				List<object> rows = await FetchDeribitFundingHistoryAsync(cfg, request.Account!, request.ApiSecretPlain!, instrument, startMs, endMs, http.RequestAborted);
				return Results.Json(new { account = ToDeribitPublicAccount(request.Account!), market = "futures", instrument, data = rows, rows, count = rows.Count, startTimestamp = startMs, endTimestamp = endMs, fetchedAtUtc = DateTime.UtcNow, source = "Deribit private/get_transaction_log query=settlement filtered by instrument" });
			}
			catch (Exception ex)
			{
				log($"[GET] /api/admin/deribit/futures/funding/history: account #{request.Account!.AccId} failed: {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Deribit funding history failed: " + ex.Message, account = ToDeribitPublicAccount(request.Account!) });
			}
		});
		app.MapGet("api/admin/deribit/futures/positions", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext http, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null) return Results.Unauthorized();
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
			DeribitPrivateRequestContext request = await BuildDeribitPrivateRequestContextAsync(http, cfg, log, "positions");
			if (request.Result != null) return request.Result;
			try
			{
				string requestedSymbol = http.Request.Query["symbol"].ToString();
				string? instrument = string.IsNullOrWhiteSpace(requestedSymbol) ? null : NormalizeDeribitFuturesInstrument(requestedSymbol);
				List<object> rows = await FetchDeribitFuturesPositionsAsync(cfg, request.Account!, request.ApiSecretPlain!, instrument, http.RequestAborted);
				return Results.Json(new { account = ToDeribitPublicAccount(request.Account!), market = "futures", instrument, data = rows, rows, count = rows.Count, fetchedAtUtc = DateTime.UtcNow, source = "Deribit private/get_positions" });
			}
			catch (Exception ex)
			{
				log($"[GET] /api/admin/deribit/futures/positions: account #{request.Account!.AccId} failed: {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Deribit futures positions failed: " + ex.Message, account = ToDeribitPublicAccount(request.Account!) });
			}
		});
		app.MapGet("api/admin/deribit/futures/positions/history", (Func<HttpContext, Task<IResult>>)delegate(HttpContext http)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null) return Task.FromResult<IResult>(Results.Unauthorized());
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase)) return Task.FromResult<IResult>(Results.Forbid());
			string instrument;
			try { instrument = NormalizeDeribitFuturesInstrument(http.Request.Query["symbol"].ToString()); } catch { instrument = http.Request.Query["symbol"].ToString(); }
			return Task.FromResult<IResult>(Results.Json(new
			{
				market = "futures",
				instrument,
				data = Array.Empty<object>(),
				rows = Array.Empty<object>(),
				count = 0,
				fetchedAtUtc = DateTime.UtcNow,
				source = "Deribit API limitation: no direct private historical positions endpoint; no VAN stored position history table is wired here"
			}));
		});
		app.MapPost("api/admin/deribit/futures/orders/place", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, DeribitPlaceOrderRequest, Task<IResult>>)async delegate(HttpContext http, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log, DeribitPlaceOrderRequest dto)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			int? accountId = null;
			if (http.Request.Query.TryGetValue("accountId", out var accountIdValue) && int.TryParse(accountIdValue.ToString(), out int parsedAccountId))
			{
				accountId = parsedAccountId;
			}
			accountId ??= await GetMappedDeribitAccountIdAsync(cfg, session, http.RequestAborted);
			DeribitAccountCredentials? account = await LoadDeribitAccountCredentialsAsync(cfg, accountId, http.RequestAborted);
			if (account == null)
			{
				return Results.NotFound(new { message = "No Deribit account selected" });
			}
			if (string.IsNullOrWhiteSpace(account.ApiPublicKey) || string.IsNullOrWhiteSpace(account.ApiSecretCipher))
			{
				return Results.BadRequest(new { message = "Selected Deribit account has no API credentials", account = ToDeribitPublicAccount(account) });
			}
			string apiSecretPlain;
			try
			{
				apiSecretPlain = ApiSecretCrypto.Decrypt(account.ApiSecretCipher);
			}
			catch (Exception ex)
			{
				log($"[POST] /api/admin/deribit/futures/orders/place: failed to decrypt account #{account.AccId}: {ex.Message}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Selected Deribit account secret cannot be decrypted", account = ToDeribitPublicAccount(account) });
			}
			try
			{
				JsonElement result = await PlaceDeribitFuturesOrderAsync(cfg, account, apiSecretPlain, dto, http.RequestAborted);
				return Results.Json(new
				{
					code = "0",
					ok = true,
					account = ToDeribitPublicAccount(account),
					data = result,
					source = "Deribit private/" + (string.Equals(dto.side, "sell", StringComparison.OrdinalIgnoreCase) ? "sell" : "buy")
				});
			}
			catch (Exception ex)
			{
				log($"[POST] /api/admin/deribit/futures/orders/place: account #{account.AccId} failed: {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { ok = false, message = "Deribit order submit failed: " + ex.Message, account = ToDeribitPublicAccount(account) });
			}
		});
		app.MapPost("api/admin/deribit/futures/orders/cancel", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, DeribitCancelOrderRequest, Task<IResult>>)async delegate(HttpContext http, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log, DeribitCancelOrderRequest dto)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			int? accountId = null;
			if (http.Request.Query.TryGetValue("accountId", out var accountIdValue) && int.TryParse(accountIdValue.ToString(), out int parsedAccountId))
			{
				accountId = parsedAccountId;
			}
			accountId ??= await GetMappedDeribitAccountIdAsync(cfg, session, http.RequestAborted);
			DeribitAccountCredentials? account = await LoadDeribitAccountCredentialsAsync(cfg, accountId, http.RequestAborted);
			if (account == null)
			{
				return Results.NotFound(new { message = "No Deribit account selected" });
			}
			if (string.IsNullOrWhiteSpace(account.ApiPublicKey) || string.IsNullOrWhiteSpace(account.ApiSecretCipher))
			{
				return Results.BadRequest(new { message = "Selected Deribit account has no API credentials", account = ToDeribitPublicAccount(account) });
			}
			string apiSecretPlain;
			try
			{
				apiSecretPlain = ApiSecretCrypto.Decrypt(account.ApiSecretCipher);
			}
			catch (Exception ex)
			{
				log($"[POST] /api/admin/deribit/futures/orders/cancel: failed to decrypt account #{account.AccId}: {ex.Message}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { message = "Selected Deribit account secret cannot be decrypted", account = ToDeribitPublicAccount(account) });
			}
			try
			{
				JsonElement result = await CancelDeribitOrderAsync(cfg, account, apiSecretPlain, dto, http.RequestAborted);
				return Results.Json(new
				{
					code = "0",
					ok = true,
					account = ToDeribitPublicAccount(account),
					data = result,
					source = "Deribit private/cancel"
				});
			}
			catch (Exception ex)
			{
				log($"[POST] /api/admin/deribit/futures/orders/cancel: account #{account.AccId} failed: {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.BadRequest(new { ok = false, message = "Deribit order cancel failed: " + ex.Message, account = ToDeribitPublicAccount(account) });
			}
		});
		app.MapGet("api/admin/deribit/equity", (Func<HttpContext, VANWebServiceConfig, Task<IResult>>)async delegate(HttpContext http, VANWebServiceConfig cfg)
		{
			UserSession session = UserSessionTools.GetUserSession(http);
			if (session == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			int? accountId = null;
			if (http.Request.Query.TryGetValue("accountId", out var accountIdValue) && int.TryParse(accountIdValue.ToString(), out int parsedAccountId))
			{
				accountId = parsedAccountId;
			}
			accountId ??= await GetMappedDeribitAccountIdAsync(cfg, session, http.RequestAborted);
			DeribitAccountCredentials? account = await LoadDeribitAccountCredentialsAsync(cfg, accountId, http.RequestAborted);
			if (account == null)
			{
				return Results.NotFound(new { message = "No Deribit account selected" });
			}
			DeribitEquityPayload payload = await LoadDeribitEquityPayloadAsync(cfg, account, http.RequestAborted);
			return Results.Json(payload);
		});
		app.MapPost("api/deleteAccount", (Func<AccMutationDTO, IAccountsRepository, Orchestrator, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(AccMutationDTO dto, IAccountsRepository repo, Orchestrator orchestrator, CoreDTO.Logger.Enums.LogAction logAction)
		{
			logAction($"[POST] /{"api/deleteAccount"}: {dto.accId}:{dto.accName}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			var (flag, text) = await repo.DeleteAccountAsync(dto.accId);
			if (!flag)
			{
				logAction("Account was NOT deleted. Reason: " + text + ".", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return TypedResults.Conflict(new
				{
					message = text
				});
			}
			orchestrator.RemoveAccount(dto.accId);
			logAction($"Account {dto.accId}:{dto.accName} is deleted successfully from database.", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return Results.Json(new DeleteAccountResponseDTO
			{
				status = true,
				accId = dto.accId,
				accName = dto.accName
			});
		});
		app.MapPost("api/createCid", (Func<AccMutationDTO, IAccountsRepository, Orchestrator, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(AccMutationDTO dto, IAccountsRepository repo, Orchestrator orchestrator, CoreDTO.Logger.Enums.LogAction logAction)
		{
			Account acc = orchestrator.GetAccountById(dto.accId);
			if (acc == null)
			{
				string text = "[POST] /api/createCid: [ERROR] account is null! Request is ignored.";
				logAction(text, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Json(new CreateCidResponseDTO
				{
					status = false,
					resultText = text
				});
			}
			CID cid = new CID
			{
				InputData = new CIDInputData
				{
					Id = dto.cidId,
					Name = (dto.cidName ?? ""),
					FeeRatePercents = dto.feeRatePercents
				},
				OutputData = new CIDOutputData()
			};
			var (flag, text2) = await repo.CreateCidAsync(acc.accId, cid);
			if (!flag)
			{
				logAction("[DB][CreateCid][ERROR] " + text2, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Json(new CreateCidResponseDTO
				{
					status = false,
					resultText = text2
				});
			}
			acc.listOfCIDs.Add(cid);
			logAction($"[POST] /{"api/createCid"}: {dto.cidId}:'{dto.cidName}' in account #{dto.accId}:'{dto.accName}'", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return Results.Json(new CreateCidResponseDTO
			{
				status = true,
				resultText = "OK",
				accId = acc.accId,
				accName = dto.accName,
				cidId = dto.cidId,
				cidName = dto.cidName
			});
		});
		app.MapPost("api/deleteCid", (Func<AccMutationDTO, IAccountsRepository, Orchestrator, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(AccMutationDTO dto, IAccountsRepository repo, Orchestrator orchestrator, CoreDTO.Logger.Enums.LogAction logAction)
		{
			logAction($"[POST] /{"api/deleteCid"}: {dto.accId}:{dto.accName}::{dto.cidId}:{dto.cidName}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			var (flag, text) = await repo.DeleteCidAsync(dto.accId, dto.cidId);
			if (!flag)
			{
				logAction("[DB][DeleteCid][ERROR] " + text, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return TypedResults.Conflict(new
				{
					message = text
				});
			}
			Account accountById = orchestrator.GetAccountById(dto.accId);
			if (accountById != null)
			{
				CID cID = accountById.listOfCIDs.FirstOrDefault((CID c) => c.InputData.Id == dto.cidId);
				if (cID != null)
				{
					accountById.listOfCIDs.Remove(cID);
				}
			}
			logAction($"CID {dto.cidId}:{dto.cidName} is deleted from account {dto.accId}:{dto.accName}.", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return Results.Json(new DeleteAccountResponseDTO
			{
				status = true,
				accId = dto.accId,
				accName = dto.accName
			});
		});
		app.MapPost("/api/importRecordsFromCsv", (Func<ImportClearingRecordRequestDTO, IRecordsRepository, Orchestrator, CoreDTO.Logger.Enums.LogAction, CancellationToken, Task<IResult>>)async delegate(ImportClearingRecordRequestDTO dto, IRecordsRepository recordsRepo, Orchestrator orchestrator, CoreDTO.Logger.Enums.LogAction log, CancellationToken ct)
		{
			string text = JsonSerializer.Serialize(dto, new JsonSerializerOptions
			{
				WriteIndented = false
			});
			log("[ImportRecordsFromCsv] Incoming JSON: " + text, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			log($"[ImportRecordsFromCsv] accId={dto.AccountId}, dt={dto.DtUtc:O}, cids={dto.Cids.Count}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			DateTime date = dto.DtUtc.Kind switch
			{
				DateTimeKind.Utc => dto.DtUtc,
				DateTimeKind.Local => dto.DtUtc.ToUniversalTime(),
				DateTimeKind.Unspecified => DateTime.SpecifyKind(dto.DtUtc, DateTimeKind.Local).ToUniversalTime(),
				_ => dto.DtUtc,
			};
			Record rec = new Record
			{
				accId = dto.AccountId,
				cidId = -1,
				date = date,
				equityUSD = dto.EquityUsd,
				type = MutationType.Clearing,
				newTotallyInvestedUSD = dto.NewTotallyInvestedUsd,
				listOfCIDs = new List<CID>()
			};
			int num = 1;
			foreach (ImportClearingRecordCidDTO cid2 in dto.Cids)
			{
				CID item = new CID
				{
					InputData = new CIDInputData
					{
						Id = num++,
						Name = cid2.CidName,
						InvestmentUSD = cid2.InvestmentUsd,
						ReinvestedUSD = cid2.ReinvestedUsd,
						ClientSharePercents = cid2.ClientSharePercents,
						DepositWithdrawalValueUSD = cid2.DepositWithdrawalUsd,
						FeeRatePercents = cid2.FeePercents
					},
					OutputData = new CIDOutputData
					{
						ClientEquityUSD = cid2.ClientEquityUsd,
						PnLUSD = cid2.PnLUsd,
						FeeUSD = cid2.FeeUsd,
						ProfitUSD = cid2.ProfitUsd,
						NewInvestmentValueUSD = cid2.NewInvestmentValueUsd,
						NewClientSharePercents = cid2.NewClientSharePercents,
						TotallyInvestedUSD = dto.NewTotallyInvestedUsd
					}
				};
				rec.listOfCIDs.Add(item);
			}
			long num2 = await recordsRepo.SaveRecordAsync(rec, ct);
			log($"[ImportRecordsFromCsv] saved recordId={num2} for accId={dto.AccountId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			Account accountById = orchestrator.GetAccountById(dto.AccountId);
			if (accountById != null)
			{
				accountById.listOfCIDs = rec.listOfCIDs;
				Account account = accountById;
				if (account.listOfRecords == null)
				{
					account.listOfRecords = new List<Record>();
				}
				accountById.listOfRecords.Add(rec);
				log($"[ImportRecordsFromCsv] orchestrator account #{dto.AccountId} listOfCIDs updated, cids={accountById.listOfCIDs.Count}, records={accountById.listOfRecords.Count}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			else
			{
				log($"[ImportRecordsFromCsv][WARN] account #{dto.AccountId} not found in orchestrator", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			return Results.Ok(new
			{
				recordId = num2
			});
		});
		app.MapPost("/api/login", (Func<LoginRequestDTO, AuthorityRepository, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(LoginRequestDTO dto, AuthorityRepository authRepo, CoreDTO.Logger.Enums.LogAction logAction)
		{
			logAction("[POST] /api/login login=" + dto.Login, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			if (dto == null || string.IsNullOrWhiteSpace(dto.Login) || string.IsNullOrWhiteSpace(dto.Password))
			{
				return Results.BadRequest(new
				{
					message = "login and password are required"
				});
			}
			LoginUserDTO loginUserDTO = await authRepo.ValidateUserAsync(dto.Login, dto.Password);
			if (loginUserDTO == null)
			{
				logAction("[AUTH] invalid credentials for '" + dto.Login + "'", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Unauthorized();
			}
			logAction($"[AUTH] login success for '{dto.Login}' (role={loginUserDTO.Role})", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			UserSession session = new UserSession
			{
				UserId = loginUserDTO.UserId,
				Login = loginUserDTO.Login,
				Role = (loginUserDTO.Role ?? "User"),
				AccountId = null
			};
			string token = AuthStore.CreateToken(session);
			LoginResponseDTO data = new LoginResponseDTO
			{
				Token = token,
				User = new LoginUserDTO
				{
					UserId = loginUserDTO.UserId,
					Login = loginUserDTO.Login,
					FullName = loginUserDTO.FullName,
					Role = loginUserDTO.Role
				}
			};
			return Results.Json(data);
		});
	}

	private static async Task<int?> GetMappedDeribitAccountIdAsync(VANWebServiceConfig cfg, UserSession session, CancellationToken ct)
	{
		int? userId = await ResolveExistingUserIdAsync(cfg, session, ct);
		if (!userId.HasValue)
		{
			return null;
		}
		await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
		await conn.OpenAsync(ct);
		await using NpgsqlCommand cmd = new NpgsqlCommand(@"
select ua.acc_id
from public.van_user_account ua
join public.van_account a on a.acc_id = ua.acc_id
where ua.user_id = @uid
order by ua.created_utc desc, ua.acc_id
limit 1;
", conn);
		cmd.Parameters.AddWithValue("uid", userId.Value);
		object value = await cmd.ExecuteScalarAsync(ct);
		return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
	}

	private static async Task<(bool ok, string message, int userId)> SetActiveDeribitAccountAsync(VANWebServiceConfig cfg, UserSession session, int accId, CancellationToken ct)
	{
		await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
		await conn.OpenAsync(ct);
		await using (NpgsqlCommand accountCmd = new NpgsqlCommand("select 1 from public.van_account where acc_id = @acc_id limit 1;", conn))
		{
			accountCmd.Parameters.AddWithValue("acc_id", accId);
			if (await accountCmd.ExecuteScalarAsync(ct) == null)
			{
				return (false, "Deribit account not found", 0);
			}
		}
		int? userId = await ResolveExistingUserIdAsync(cfg, session, ct);
		if (!userId.HasValue)
		{
			return (false, "Authenticated user not found in van_user", 0);
		}
		await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(ct);
		await using (NpgsqlCommand deleteCmd = new NpgsqlCommand("delete from public.van_user_account where user_id = @uid;", conn, tx))
		{
			deleteCmd.Parameters.AddWithValue("uid", userId.Value);
			await deleteCmd.ExecuteNonQueryAsync(ct);
		}
		await using (NpgsqlCommand insertCmd = new NpgsqlCommand("insert into public.van_user_account (user_id, acc_id) values (@uid, @acc_id);", conn, tx))
		{
			insertCmd.Parameters.AddWithValue("uid", userId.Value);
			insertCmd.Parameters.AddWithValue("acc_id", accId);
			await insertCmd.ExecuteNonQueryAsync(ct);
		}
		await tx.CommitAsync(ct);
		return (true, "OK", userId.Value);
	}

	private static async Task<int?> ResolveExistingUserIdAsync(VANWebServiceConfig cfg, UserSession session, CancellationToken ct)
	{
		await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
		await conn.OpenAsync(ct);
		await using NpgsqlCommand cmd = new NpgsqlCommand(@"
select user_id from public.van_user where user_id = @uid
union all
select user_id from public.van_user where lower(login) = lower(@login)
limit 1;
", conn);
		cmd.Parameters.AddWithValue("uid", session.UserId);
		cmd.Parameters.AddWithValue("login", session.Login ?? string.Empty);
		object value = await cmd.ExecuteScalarAsync(ct);
		return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
	}

	private static bool IsDeribitInvalidCredentials(Exception ex)
	{
		string text = ex.Message ?? "";
		return text.Contains("invalid_credentials", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("\"code\":13004", StringComparison.OrdinalIgnoreCase)
			|| text.Contains("13004", StringComparison.OrdinalIgnoreCase);
	}

	private static async Task<DeribitAccountCredentials?> LoadDeribitAccountCredentialsAsync(VANWebServiceConfig cfg, int? accountId, CancellationToken ct)
	{
		await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
		await conn.OpenAsync(ct);
		string sql = accountId.HasValue
			? @"
select acc_id, acc_name, api_public_key, api_secret_cipher
from public.van_account
where acc_id = @acc_id
limit 1;
"
			: @"
select acc_id, acc_name, api_public_key, api_secret_cipher
from public.van_account
where api_public_key is not null
  and api_secret_cipher is not null
order by acc_id
limit 1;
";
		await using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
		if (accountId.HasValue)
		{
			cmd.Parameters.AddWithValue("acc_id", accountId.Value);
		}
		await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
		if (!await reader.ReadAsync(ct))
		{
			return null;
		}
		int accId = reader.GetInt32(0);
		return new DeribitAccountCredentials(
			accId,
			reader.IsDBNull(1) ? ("Account " + accId.ToString()) : reader.GetString(1),
			reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
			reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
	}

	private static object ToDeribitPublicAccount(DeribitAccountCredentials account)
	{
		return new
		{
			id = account.AccId,
			name = account.Name,
			apiKeyMasked = MaskDeribitKey(account.ApiPublicKey),
			isActive = true,
			storeMinuteEquity = true,
			equityCurrency = "USD"
		};
	}

	private static string MaskDeribitKey(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "—";
		}
		string trimmed = value.Trim();
		if (trimmed.Length <= 8)
		{
			return trimmed.Substring(0, Math.Min(2, trimmed.Length)) + "...";
		}
		return trimmed.Substring(0, 4) + "..." + trimmed.Substring(trimmed.Length - 4);
	}

	private static async Task<DeribitAccountSummary> FetchDeribitAccountSummaryAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, CancellationToken ct)
	{
		using HttpClient client = new HttpClient
		{
			BaseAddress = new Uri(string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl)
		};
		string token = await DeribitAuthAsync(client, account.ApiPublicKey, apiSecretPlain, ct);
		Dictionary<string, double> usdRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
		{
			["USDC"] = 1.0,
			["USDT"] = 1.0,
			["USD"] = 1.0
		};
		usdRates["BTC"] = await FetchDeribitMarkPriceAsync(client, "BTC-PERPETUAL", ct);
		usdRates["ETH"] = await FetchDeribitMarkPriceAsync(client, "ETH-PERPETUAL", ct);
		List<DeribitSummaryRow> rows = new List<DeribitSummaryRow>();
		foreach (string currency in new[] { "USDC", "BTC", "ETH" })
		{
			JsonElement result = await DeribitPrivateSummaryAsync(client, token, currency, ct);
			double? equity = GetJsonDouble(result, "equity");
			double? available = GetJsonDouble(result, "available_funds") ?? GetJsonDouble(result, "available_withdrawal_funds");
			double? initialMargin = GetJsonDouble(result, "initial_margin");
			double? maintenanceMargin = GetJsonDouble(result, "maintenance_margin");
			bool? portfolioMargining = GetJsonBool(result, "portfolio_margining_enabled");
			double rate = usdRates.TryGetValue(currency, out double knownRate) ? knownRate : 1.0;
			rows.Add(new DeribitSummaryRow(
				currency,
				equity,
				available,
				initialMargin,
				maintenanceMargin,
				Multiply(equity, rate),
				Multiply(available, rate),
				Multiply(initialMargin, rate),
				Multiply(maintenanceMargin, rate),
				portfolioMargining,
				rate,
				result));
		}
		double? Sum(Func<DeribitSummaryRow, double?> pick)
		{
			double total = 0.0;
			bool any = false;
			foreach (DeribitSummaryRow row in rows)
			{
				double? value = pick(row);
				if (value.HasValue)
				{
					total += value.Value;
					any = true;
				}
			}
			return any ? total : null;
		}
		double? FirstTotalUsd(params string[] propertyNames)
		{
			foreach (string propertyName in propertyNames)
			{
				foreach (DeribitSummaryRow row in rows)
				{
					double? value = GetJsonDouble(row.raw, propertyName);
					if (value.HasValue)
					{
						return value;
					}
				}
			}
			return null;
		}
		double? StableAvailableUsd()
		{
			foreach (string currency in new[] { "USDC", "USDT", "USD" })
			{
				DeribitSummaryRow? row = rows.FirstOrDefault(x => string.Equals(x.currency, currency, StringComparison.OrdinalIgnoreCase));
				if (row != null && row.availableUsd.HasValue)
				{
					return row.availableUsd;
				}
			}
			return rows.Count == 1 ? rows[0].availableUsd : null;
		}
		return new DeribitAccountSummary(
			rows,
			FirstTotalUsd("total_equity_usd", "total_margin_balance_usd") ?? Sum(row => row.equityUsd),
			StableAvailableUsd(),
			FirstTotalUsd("total_initial_margin_usd") ?? Sum(row => row.initialMarginUsd),
			FirstTotalUsd("total_maintenance_margin_usd") ?? Sum(row => row.maintenanceMarginUsd),
			rows.Any(row => row.portfolioMarginingEnabled == true),
			rows.Any(row => row.portfolioMarginingEnabled.HasValue));
	}

	private static async Task<DeribitEquityPayload> LoadDeribitEquityPayloadAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, CancellationToken ct)
	{
		List<DeribitEquityPoint> daily = new List<DeribitEquityPoint>();
		List<DeribitEquityPoint> minute = new List<DeribitEquityPoint>();
		List<DeribitEquityPoint> dailyDrawdownMinute = new List<DeribitEquityPoint>();
		decimal? initialInvestmentUsd = null;
		await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
		await conn.OpenAsync(ct);
		await using (NpgsqlCommand cmd = new NpgsqlCommand(@"
select initial_investment_usd::numeric
from public.van_account
where acc_id = @acc_id;
", conn))
		{
			cmd.Parameters.AddWithValue("acc_id", account.AccId);
			object? value = await cmd.ExecuteScalarAsync(ct);
			if (value != null && value is not DBNull)
			{
				initialInvestmentUsd = Convert.ToDecimal(value);
			}
		}
		await using (NpgsqlCommand cmd = new NpgsqlCommand(@"
select date_utc::timestamp with time zone as ts_utc, total_equity_usd::numeric
from public.van_account_daily_equity
where acc_id = @acc_id
  and date_utc >= (((now() at time zone 'utc')::date) - 3650)
  and total_equity_usd is not null
order by date_utc;
", conn))
		{
			cmd.Parameters.AddWithValue("acc_id", account.AccId);
			await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
			while (await reader.ReadAsync(ct))
			{
				daily.Add(new DeribitEquityPoint(ReadUtc(reader, 0), reader.GetDecimal(1)));
			}
		}
		await using (NpgsqlCommand cmd = new NpgsqlCommand(@"
select minute_utc, equity_total_usd::numeric
from public.van_account_equity
where acc_id = @acc_id
  and minute_utc >= ((now() at time zone 'utc') - interval '24 hours')
  and equity_total_usd is not null
order by minute_utc;
", conn))
		{
			cmd.Parameters.AddWithValue("acc_id", account.AccId);
			await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
			while (await reader.ReadAsync(ct))
			{
				minute.Add(new DeribitEquityPoint(ReadUtc(reader, 0), reader.GetDecimal(1)));
			}
		}
		await using (NpgsqlCommand cmd = new NpgsqlCommand(@"
select minute_utc, equity_total_usd::numeric
from public.van_account_equity
where acc_id = @acc_id
  and minute_utc >= (((now() at time zone 'utc')::date) - 3650)
  and equity_total_usd is not null
order by minute_utc;
", conn))
		{
			cmd.Parameters.AddWithValue("acc_id", account.AccId);
			await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
			while (await reader.ReadAsync(ct))
			{
				dailyDrawdownMinute.Add(new DeribitEquityPoint(ReadUtc(reader, 0), reader.GetDecimal(1)));
			}
		}
		List<DeribitChartPoint> dailyPoints = MergeDailyWithLatestMinuteDay(daily, dailyDrawdownMinute);
		List<DeribitChartPoint> minutePoints = minute.Select(p => new DeribitChartPoint(p.TsUtc, p.Value)).ToList();
		DeribitEquityPoint? latest = minute.LastOrDefault() ?? daily.LastOrDefault();
		DeribitChartPoint? first = dailyPoints.FirstOrDefault();
		DateTime nowUtc = DateTime.UtcNow;
		DeribitEquityPoint? ref24h = minute.LastOrDefault(p => p.TsUtc <= nowUtc.AddHours(-24)) ?? daily.LastOrDefault(p => p.TsUtc <= nowUtc.AddHours(-1)) ?? (latest == null ? null : new DeribitEquityPoint(latest.TsUtc, latest.Value));
		DeribitChartPoint? ref7d = dailyPoints.LastOrDefault(p => p.tsUtc <= nowUtc.AddDays(-7)) ?? first;
		List<DeribitChartPoint> dailyDrawdownPoints = BuildDeribitDailyDrawdownSeriesWithMa60Hwm(dailyPoints, dailyDrawdownMinute.Select(p => new DeribitChartPoint(p.TsUtc, p.Value)).ToList());
		List<DeribitChartPoint> minuteDrawdownPoints = BuildDeribitDrawdownSeriesWith30MinuteConfirmedHwm(minutePoints, collapseToDate: false);
		return new DeribitEquityPayload(
			new
			{
				equityCurrency = "USD",
				storeMinuteEquity = true,
				latestTotalEquity = latest?.Value ?? 0m,
				latestAvailableEquity = 0m,
				latestUnrealizedPnl = 0m,
				latestMarginRatio = (decimal?)null,
				initialInvestmentUsd,
				pnl24h = BuildDeribitEquityDelta(ref24h?.Value, latest?.Value),
				pnl7d = BuildDeribitEquityDelta(ref7d?.value, latest?.Value),
				maxDrawdown24hPct = MaxDrawdown(minuteDrawdownPoints),
				maxDrawdownAllPct = MaxDrawdown(dailyDrawdownPoints),
				dailyPointCount = dailyPoints.Count,
				minutePointCount = minutePoints.Count,
				firstSnapshotUtc = first?.tsUtc,
				lastSnapshotUtc = latest?.TsUtc,
				source = "public.van_account_equity/public.van_account_daily_equity"
			},
			new { points = dailyPoints, drawdownPoints = dailyDrawdownPoints },
			new { points = minutePoints, drawdownPoints = minuteDrawdownPoints });
	}

	private static DateTime ReadUtc(NpgsqlDataReader reader, int ordinal)
	{
		DateTime value = reader.GetDateTime(ordinal);
		return value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
	}

	private static List<DeribitChartPoint> MergeDailyWithLatestMinuteDay(List<DeribitEquityPoint> daily, List<DeribitEquityPoint> minute)
	{
		SortedDictionary<DateOnly, DeribitChartPoint> byDay = new SortedDictionary<DateOnly, DeribitChartPoint>();
		foreach (DeribitEquityPoint point in daily.OrderBy(p => p.TsUtc))
		{
			DateOnly day = DateOnly.FromDateTime(point.TsUtc);
			byDay[day] = new DeribitChartPoint(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), point.Value);
		}
		DeribitEquityPoint? latestMinute = minute.OrderBy(p => p.TsUtc).LastOrDefault();
		if (latestMinute != null)
		{
			DateOnly day = DateOnly.FromDateTime(latestMinute.TsUtc);
			byDay[day] = new DeribitChartPoint(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), latestMinute.Value);
		}
		return byDay.Values.ToList();
	}

	private static object BuildDeribitEquityDelta(decimal? first, decimal? latest)
	{
		if (!first.HasValue || !latest.HasValue || first.Value == 0m)
		{
			return new { absolute = 0m, percent = 0m };
		}
		decimal absolute = latest.Value - first.Value;
		return new { absolute, percent = absolute / first.Value * 100m };
	}

	private static List<DeribitChartPoint> BuildDeribitDrawdownSeries(List<DeribitChartPoint> points)
	{
		List<DeribitChartPoint> result = new List<DeribitChartPoint>();
		if (points.Count == 0)
		{
			return result;
		}
		decimal peak = points[0].value;
		foreach (DeribitChartPoint point in points)
		{
			if (point.value > peak)
			{
				peak = point.value;
			}
			decimal drawdown = peak <= 0m ? 0m : (peak - point.value) / peak * 100m;
			result.Add(new DeribitChartPoint(point.tsUtc, Math.Round(drawdown, 4)));
		}
		return result;
	}

	private static List<DeribitChartPoint> BuildDeribitDailyDrawdownSeriesWithMa60Hwm(List<DeribitChartPoint> dailyPoints, List<DeribitChartPoint> points)
	{
		List<DeribitChartPoint> fallback = BuildDeribitDrawdownSeries(dailyPoints);
		Dictionary<DateOnly, decimal> byDate = fallback.ToDictionary(p => DateOnly.FromDateTime(p.tsUtc), p => p.value);
		if (points.Count == 0)
		{
			return fallback;
		}

		Queue<decimal> window = new Queue<decimal>();
		decimal windowSum = 0m;
		decimal? hwm60m = null;
		DateOnly? currentDay = null;
		decimal currentDayMax = 0m;
		bool currentDayHasMa60 = false;
		foreach (DeribitChartPoint point in points.OrderBy(p => p.tsUtc))
		{
			window.Enqueue(point.value);
			windowSum += point.value;
			while (window.Count > 60)
			{
				windowSum -= window.Dequeue();
			}

			decimal equityMa60m = windowSum / window.Count;
			hwm60m = hwm60m.HasValue && hwm60m.Value > equityMa60m ? hwm60m.Value : equityMa60m;
			decimal drawdown = hwm60m.Value <= 0m ? 0m : (hwm60m.Value - point.value) / hwm60m.Value * 100m;
			if (drawdown < 0m)
			{
				drawdown = 0m;
			}

			DateOnly day = DateOnly.FromDateTime(point.tsUtc);
			if (currentDay != day)
			{
				if (currentDay.HasValue && currentDayHasMa60)
				{
					byDate[currentDay.Value] = Math.Round(currentDayMax, 4);
				}
				currentDay = day;
				currentDayMax = drawdown;
				currentDayHasMa60 = window.Count >= 60;
				continue;
			}

			if (drawdown > currentDayMax)
			{
				currentDayMax = drawdown;
			}
			if (window.Count >= 60)
			{
				currentDayHasMa60 = true;
			}
		}
		if (currentDay.HasValue && currentDayHasMa60)
		{
			byDate[currentDay.Value] = Math.Round(currentDayMax, 4);
		}

		return byDate
			.OrderBy(kv => kv.Key)
			.Select(kv => new DeribitChartPoint(kv.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Math.Round(kv.Value, 4)))
			.ToList();
	}

	private static List<DeribitChartPoint> BuildDeribitDailyDrawdownSeriesWithMinuteOverlay(List<DeribitChartPoint> dailyPoints, List<DeribitChartPoint> minutePoints)
	{
		List<DeribitChartPoint> dailyDrawdown = BuildDeribitDrawdownSeries(dailyPoints);
		if (minutePoints.Count == 0)
		{
			return dailyDrawdown;
		}
		Dictionary<DateOnly, decimal> byDate = dailyDrawdown.ToDictionary(p => DateOnly.FromDateTime(p.tsUtc), p => p.value);
		foreach (DeribitChartPoint point in BuildDeribitDrawdownSeriesWith30MinuteConfirmedHwm(minutePoints, collapseToDate: true))
		{
			byDate[DateOnly.FromDateTime(point.tsUtc)] = point.value;
		}
		return byDate
			.OrderBy(kv => kv.Key)
			.Select(kv => new DeribitChartPoint(kv.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Math.Round(kv.Value, 4)))
			.ToList();
	}

	private static readonly DateOnly DeribitDrawdownMethodCutoffUtc = new DateOnly(2026, 5, 28);

	private static List<DeribitChartPoint> BuildDeribitDailyDrawdownSeriesWithHybridCutoff(List<DeribitChartPoint> dailyPoints, List<DeribitChartPoint> minutePoints, decimal? initialInvestmentUsd)
	{
		List<DeribitChartPoint> dailyDrawdown = BuildDeribitDrawdownSeries(dailyPoints);
		if (minutePoints.Count == 0)
		{
			return dailyDrawdown;
		}
		Dictionary<DateOnly, decimal> byDate = dailyDrawdown.ToDictionary(p => DateOnly.FromDateTime(p.tsUtc), p => p.value);
		if (initialInvestmentUsd.HasValue && initialInvestmentUsd.Value > 0m)
		{
			foreach (DeribitChartPoint point in BuildDeribitPnlPointDrawdownSeriesWith30MinuteConfirmedHwm(minutePoints, initialInvestmentUsd.Value, collapseToDate: true))
			{
				DateOnly day = DateOnly.FromDateTime(point.tsUtc);
				if (day < DeribitDrawdownMethodCutoffUtc)
				{
					byDate[day] = point.value;
				}
			}
		}
		foreach (DeribitChartPoint point in BuildDeribitDrawdownSeriesWith30MinuteConfirmedHwm(minutePoints, collapseToDate: true))
		{
			DateOnly day = DateOnly.FromDateTime(point.tsUtc);
			if (day >= DeribitDrawdownMethodCutoffUtc)
			{
				byDate[day] = point.value;
			}
		}
		return byDate
			.OrderBy(kv => kv.Key)
			.Select(kv => new DeribitChartPoint(kv.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Math.Round(kv.Value, 4)))
			.ToList();
	}

	private static List<DeribitChartPoint> BuildDeribitPnlPointDrawdownSeriesWith30MinuteConfirmedHwm(List<DeribitChartPoint> points, decimal initialInvestmentUsd, bool collapseToDate)
	{
		List<DeribitChartPoint> result = new List<DeribitChartPoint>();
		if (points.Count == 0 || initialInvestmentUsd <= 0m)
		{
			return result;
		}
		List<DeribitChartPoint> ordered = points.OrderBy(p => p.tsUtc).ToList();
		decimal acceptedHwm = (ordered[0].value / initialInvestmentUsd - 1m) * 100m;
		Queue<DeribitChartPoint> pending = new Queue<DeribitChartPoint>();
		foreach (DeribitChartPoint point in ordered)
		{
			while (pending.Count > 0 && pending.Peek().tsUtc <= point.tsUtc.AddMinutes(-30))
			{
				DeribitChartPoint confirmed = pending.Dequeue();
				decimal confirmedPnlPct = (confirmed.value / initialInvestmentUsd - 1m) * 100m;
				if (confirmedPnlPct > acceptedHwm)
				{
					acceptedHwm = confirmedPnlPct;
				}
			}
			decimal pnlPct = (point.value / initialInvestmentUsd - 1m) * 100m;
			decimal drawdown = acceptedHwm - pnlPct;
			if (drawdown < 0m)
			{
				drawdown = 0m;
			}
			pending.Enqueue(point);
			DateTime tsUtc = collapseToDate ? DateTime.SpecifyKind(point.tsUtc.Date, DateTimeKind.Utc) : point.tsUtc;
			if (collapseToDate && result.Count > 0 && result[^1].tsUtc == tsUtc)
			{
				if (drawdown > result[^1].value)
				{
					result[^1] = new DeribitChartPoint(tsUtc, Math.Round(drawdown, 4));
				}
			}
			else
			{
				result.Add(new DeribitChartPoint(tsUtc, Math.Round(drawdown, 4)));
			}
		}
		return result;
	}

	private static List<DeribitChartPoint> BuildDeribitDrawdownSeriesWith30MinuteConfirmedHwm(List<DeribitChartPoint> points, bool collapseToDate)
	{
		List<DeribitChartPoint> result = new List<DeribitChartPoint>();
		if (points.Count == 0)
		{
			return result;
		}
		List<DeribitChartPoint> ordered = points.OrderBy(p => p.tsUtc).ToList();
		decimal acceptedHwm = ordered[0].value;
		Queue<DeribitChartPoint> pending = new Queue<DeribitChartPoint>();
		foreach (DeribitChartPoint point in ordered)
		{
			while (pending.Count > 0 && pending.Peek().tsUtc <= point.tsUtc.AddMinutes(-30))
			{
				DeribitChartPoint confirmed = pending.Dequeue();
				if (confirmed.value > acceptedHwm)
				{
					acceptedHwm = confirmed.value;
				}
			}
			decimal drawdown = acceptedHwm <= 0m ? 0m : (acceptedHwm - point.value) / acceptedHwm * 100m;
			if (drawdown < 0m)
			{
				drawdown = 0m;
			}
			pending.Enqueue(point);
			DateTime tsUtc = collapseToDate ? DateTime.SpecifyKind(point.tsUtc.Date, DateTimeKind.Utc) : point.tsUtc;
			if (collapseToDate && result.Count > 0 && result[^1].tsUtc == tsUtc)
			{
				if (drawdown > result[^1].value)
				{
					result[^1] = new DeribitChartPoint(tsUtc, Math.Round(drawdown, 4));
				}
			}
			else
			{
				result.Add(new DeribitChartPoint(tsUtc, Math.Round(drawdown, 4)));
			}
		}
		return result;
	}

	private static decimal MaxDrawdown(List<DeribitChartPoint> points)
	{
		return points.Count == 0 ? 0m : points.Max(p => p.value);
	}

	private static async Task<string> DeribitAuthAsync(HttpClient client, string clientId, string clientSecret, CancellationToken ct)
	{
		JsonElement result = await DeribitRpcAsync(client, "public/auth", new
		{
			grant_type = "client_credentials",
			client_id = clientId,
			client_secret = clientSecret
		}, null, ct);
		if (result.TryGetProperty("access_token", out JsonElement tokenElement) && tokenElement.ValueKind == JsonValueKind.String)
		{
			return tokenElement.GetString() ?? string.Empty;
		}
		throw new InvalidOperationException("Deribit auth response did not contain access_token");
	}

	private static async Task<JsonElement> DeribitPrivateSummaryAsync(HttpClient client, string token, string currency, CancellationToken ct)
	{
		return await DeribitRpcAsync(client, "private/get_account_summary", new
		{
			currency,
			extended = true
		}, token, ct);
	}

	private static async Task<JsonElement> DeribitPrivateOpenOrdersByCurrencyAsync(HttpClient client, string token, string currency, string kind, CancellationToken ct)
	{
		return await DeribitRpcAsync(client, "private/get_open_orders_by_currency", new
		{
			currency,
			kind
		}, token, ct);
	}

	private static async Task<DeribitPrivateRequestContext> BuildDeribitPrivateRequestContextAsync(HttpContext http, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log, string routeLabel)
	{
		UserSession session = UserSessionTools.GetUserSession(http);
		int? accountId = null;
		if (http.Request.Query.TryGetValue("accountId", out var accountIdValue) && int.TryParse(accountIdValue.ToString(), out int parsedAccountId))
		{
			accountId = parsedAccountId;
		}
		accountId ??= await GetMappedDeribitAccountIdAsync(cfg, session!, http.RequestAborted);
		DeribitAccountCredentials? account = await LoadDeribitAccountCredentialsAsync(cfg, accountId, http.RequestAborted);
		if (account == null)
		{
			return new DeribitPrivateRequestContext(null, null, Results.NotFound(new { message = "No Deribit account selected" }));
		}
		if (string.IsNullOrWhiteSpace(account.ApiPublicKey) || string.IsNullOrWhiteSpace(account.ApiSecretCipher))
		{
			return new DeribitPrivateRequestContext(account, null, Results.BadRequest(new { message = "Selected Deribit account has no API credentials", account = ToDeribitPublicAccount(account) }));
		}
		try
		{
			return new DeribitPrivateRequestContext(account, ApiSecretCrypto.Decrypt(account.ApiSecretCipher), null);
		}
		catch (Exception ex)
		{
			log($"[GET] /api/admin/deribit/futures/{routeLabel}: failed to decrypt account #{account.AccId}: {ex.Message}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
			return new DeribitPrivateRequestContext(account, null, Results.BadRequest(new { message = "Selected Deribit account secret cannot be decrypted", account = ToDeribitPublicAccount(account) }));
		}
	}

	private static int ReadDeribitQueryCount(HttpContext http, int defaultCount, int maxCount)
	{
		string raw = http.Request.Query["pageSize"].ToString();
		if (string.IsNullOrWhiteSpace(raw))
		{
			raw = http.Request.Query["count"].ToString();
		}
		return int.TryParse(raw, out int parsed) ? Math.Max(1, Math.Min(maxCount, parsed)) : defaultCount;
	}

	private static (long startMs, long endMs) ReadDeribitRangeMs(HttpContext http, TimeSpan defaultWindow)
	{
		long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		long end = ReadQueryLong(http, "endTimestamp") ?? ReadQueryLong(http, "end") ?? now;
		long start = ReadQueryLong(http, "startTimestamp") ?? ReadQueryLong(http, "start") ?? end - (long)defaultWindow.TotalMilliseconds;
		if (start >= end)
		{
			start = end - (long)defaultWindow.TotalMilliseconds;
		}
		return (start, end);
	}

	private static long? ReadQueryLong(HttpContext http, string name)
	{
		string raw = http.Request.Query[name].ToString();
		if (long.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long parsed))
		{
			return parsed;
		}
		return null;
	}

	private static async Task<JsonElement> FetchDeribitOrderHistoryAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, string instrument, int count, CancellationToken ct)
	{
		using HttpClient client = new HttpClient { BaseAddress = new Uri(string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl) };
		string token = await DeribitAuthAsync(client, account.ApiPublicKey, apiSecretPlain, ct);
		return await DeribitRpcAsync(client, "private/get_order_history_by_instrument", new { instrument_name = instrument, count, offset = 0, include_old = true, include_unfilled = true }, token, ct);
	}

	private static async Task<JsonElement> FetchDeribitTradeHistoryAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, string instrument, int count, CancellationToken ct)
	{
		using HttpClient client = new HttpClient { BaseAddress = new Uri(string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl) };
		string token = await DeribitAuthAsync(client, account.ApiPublicKey, apiSecretPlain, ct);
		return await DeribitRpcAsync(client, "private/get_user_trades_by_instrument", new { instrument_name = instrument, count, include_old = true, sorting = "desc" }, token, ct);
	}

	private static async Task<List<JsonElement>> FetchDeribitTradeTransactionLogsAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, string instrument, int count, long startMs, long endMs, CancellationToken ct)
	{
		using HttpClient client = new HttpClient { BaseAddress = new Uri(string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl) };
		string token = await DeribitAuthAsync(client, account.ApiPublicKey, apiSecretPlain, ct);
		string currency = instrument.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "BTC";
		List<JsonElement> rows = new List<JsonElement>();
		long? continuation = null;
		for (int page = 0; page < 5 && rows.Count < count; page++)
		{
			Dictionary<string, object> parameters = new Dictionary<string, object>
			{
				["currency"] = currency,
				["start_timestamp"] = startMs,
				["end_timestamp"] = endMs,
				["count"] = 250,
				["query"] = "trade"
			};
			if (continuation.HasValue)
			{
				parameters["continuation"] = continuation.Value;
			}
			JsonElement result = await DeribitRpcAsync(client, "private/get_transaction_log", parameters, token, ct);
			if (result.TryGetProperty("logs", out JsonElement logs) && logs.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement row in logs.EnumerateArray())
				{
					string rowInstrument = GetJsonString(row, "instrument_name") ?? GetJsonString(row, "instrument") ?? string.Empty;
					if (!string.IsNullOrWhiteSpace(rowInstrument) && !string.Equals(rowInstrument, instrument, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					rows.Add(row.Clone());
				}
			}
			continuation = GetJsonLong(result, "continuation");
			if (!continuation.HasValue)
			{
				break;
			}
			if (rows.Count < count)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(1100), ct);
			}
		}
		return rows
			.OrderByDescending(row => GetJsonLong(row, "timestamp") ?? GetJsonLong(row, "trade_timestamp") ?? 0L)
			.Take(Math.Max(1, count))
			.ToList();
	}

	private static async Task<List<object>> FetchDeribitFundingHistoryAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, string instrument, long startMs, long endMs, CancellationToken ct)
	{
		using HttpClient client = new HttpClient { BaseAddress = new Uri(string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl) };
		string token = await DeribitAuthAsync(client, account.ApiPublicKey, apiSecretPlain, ct);
		string currency = instrument.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "BTC";
		JsonElement result = await DeribitRpcAsync(client, "private/get_transaction_log", new { currency, start_timestamp = startMs, end_timestamp = endMs, count = 1000, query = "settlement" }, token, ct);
		List<object> rows = new List<object>();
		if (result.TryGetProperty("logs", out JsonElement logs) && logs.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement row in logs.EnumerateArray())
			{
				string rowInstrument = GetJsonString(row, "instrument_name") ?? GetJsonString(row, "instrument") ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(rowInstrument) && !string.Equals(rowInstrument, instrument, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				rows.Add(ToDeribitFundingHistoryRow(row, instrument));
			}
		}
		return rows;
	}

	private static double? GetDeribitHistoryFee(JsonElement row)
	{
		return GetJsonDouble(row, "fee") ?? GetJsonDouble(row, "commission");
	}

	private static string GetDeribitHistoryFeeCurrency(JsonElement row, string fallback)
	{
		return GetJsonString(row, "fee_currency")
			?? GetJsonString(row, "feeCurrency")
			?? GetJsonString(row, "commission_currency")
			?? GetJsonString(row, "commissionCurrency")
			?? GetJsonString(row, "currency")
			?? GetJsonString(row, "price_currency")
			?? fallback;
	}

	private static double? GetDeribitHistoryRealizedPnl(JsonElement row)
	{
		return GetJsonDouble(row, "profit_loss")
			?? GetJsonDouble(row, "realized_profit_loss")
			?? GetJsonDouble(row, "realized_pnl")
			?? GetJsonDouble(row, "realizedPnl")
			?? GetJsonDouble(row, "realizedPNL")
			?? GetJsonDouble(row, "pnl")
			?? GetJsonDouble(row, "cashflow");
	}

	private static string? GetDeribitHistoryRole(JsonElement row)
	{
		return GetJsonString(row, "liquidity")
			?? GetJsonString(row, "role")
			?? GetJsonString(row, "user_role")
			?? GetJsonString(row, "fee_role");
	}

	private static object ToDeribitTradeHistoryRow(JsonElement trade, string instrument)
	{
		string direction = GetDeribitTradeDirection(trade);
		int? side = direction == "buy" ? 1 : direction == "sell" ? 2 : null;
		double? amount = GetJsonDouble(trade, "amount");
		double? price = GetJsonDouble(trade, "price");
		double? fee = GetDeribitHistoryFee(trade);
		string baseToken = instrument.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		string? tradeId = GetJsonString(trade, "trade_id") ?? GetJsonString(trade, "id");
		long? timestamp = GetJsonLong(trade, "timestamp") ?? GetJsonLong(trade, "trade_timestamp");
		string feeCurrency = GetDeribitHistoryFeeCurrency(trade, baseToken);
		double? realizedPnl = GetDeribitHistoryRealizedPnl(trade);
		string? role = GetDeribitHistoryRole(trade);
		return new { tradeId, id = tradeId, orderId = GetJsonString(trade, "order_id"), symbol = instrument, displaySymbol = instrument, displayName = instrument, side, direction, tradeSide = direction, amount, qty = amount, price, fillPrice = price, filledPrice = price, markPrice = GetJsonDouble(trade, "mark_price"), indexPrice = GetJsonDouble(trade, "index_price"), fee, fees = fee, commission = fee, feeCurrency, fee_currency = feeCurrency, rpnl = realizedPnl, realizedPnl, role, liquidity = role, userRole = GetJsonString(trade, "user_role"), feeRole = GetJsonString(trade, "fee_role"), baseToken, currency = baseToken, time = timestamp, ts = timestamp, raw = trade.Clone() };
	}

	private static DeribitOpenOrderRow ToDeribitTransactionOrderHistoryRow(JsonElement row, string instrument)
	{
		string direction = GetDeribitTradeDirection(row);
		int? side = direction == "buy" ? 1 : direction == "sell" ? 2 : null;
		double? amount = GetJsonDouble(row, "amount");
		double? price = GetJsonDouble(row, "price");
		string baseToken = instrument.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		long? timestamp = GetJsonLong(row, "timestamp") ?? GetJsonLong(row, "trade_timestamp");
		string orderId = GetJsonString(row, "order_id") ?? GetJsonString(row, "id") ?? GetJsonString(row, "trade_id") ?? string.Empty;
		string orderType = GetJsonString(row, "order_type") ?? GetJsonString(row, "type") ?? "trade";
		return new DeribitOpenOrderRow(
			orderId: orderId,
			clientOrderId: GetJsonString(row, "label") ?? string.Empty,
			symbol: instrument,
			displaySymbol: instrument,
			displayName: instrument,
			instrument: instrument,
			market: "futures",
			side: side,
			direction: direction,
			tradeSide: direction,
			amount: amount,
			qty: amount,
			size: amount,
			orderQty: amount,
			filled: amount,
			fillQty: amount,
			filledQty: amount,
			price: price,
			px: price,
			orderPrice: price,
			avgPrice: price,
			averagePrice: price,
			orderType: orderType,
			type: orderType,
			status: GetJsonString(row, "order_state") ?? "filled",
			state: GetJsonString(row, "order_state") ?? "filled",
			reduceOnly: GetJsonBool(row, "reduce_only"),
			postOnly: GetJsonBool(row, "post_only"),
			timeInForce: GetJsonString(row, "time_in_force") ?? string.Empty,
			baseToken: baseToken,
			currency: GetJsonString(row, "currency") ?? baseToken,
			createTime: timestamp,
			createdTime: timestamp,
			updateTime: timestamp,
			ts: timestamp,
			fee: GetDeribitHistoryFee(row),
			fees: GetDeribitHistoryFee(row),
			commission: GetDeribitHistoryFee(row),
			feeCurrency: GetDeribitHistoryFeeCurrency(row, baseToken),
			fee_currency: GetDeribitHistoryFeeCurrency(row, baseToken),
			rpnl: GetDeribitHistoryRealizedPnl(row),
			realizedPnl: GetDeribitHistoryRealizedPnl(row),
			role: GetDeribitHistoryRole(row),
			liquidity: GetDeribitHistoryRole(row),
			raw: row.Clone());
	}

	private static string GetDeribitTradeDirection(JsonElement row)
	{
		string direction = (GetJsonString(row, "direction") ?? GetJsonString(row, "side") ?? string.Empty).Trim().ToLowerInvariant();
		if (direction == "long")
		{
			return "buy";
		}
		if (direction == "short")
		{
			return "sell";
		}
		return direction;
	}

	private static object ToDeribitFundingHistoryRow(JsonElement row, string instrument)
	{
		double? amount = GetJsonDouble(row, "amount") ?? GetJsonDouble(row, "position");
		double? funding = GetJsonDouble(row, "funding") ?? GetJsonDouble(row, "funding_payment") ?? GetJsonDouble(row, "cash_flow");
		double? rate = GetJsonDouble(row, "funding_rate") ?? GetJsonDouble(row, "interest_8h");
		return new { ctime = GetJsonLong(row, "timestamp"), time = GetJsonLong(row, "timestamp"), symbol = instrument, displayName = instrument, qty = amount, amount, fundFee = funding, funding, fundRate = rate, fundingRate = rate, fee = GetJsonDouble(row, "fee"), raw = row.Clone() };
	}

	private static async Task<List<object>> FetchDeribitFuturesPositionsAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, string? instrument, CancellationToken ct)
	{
		using HttpClient client = new HttpClient
		{
			BaseAddress = new Uri(string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl)
		};
		string token = await DeribitAuthAsync(client, account.ApiPublicKey, apiSecretPlain, ct);
		string[] currencies = string.IsNullOrWhiteSpace(instrument)
			? new[] { "BTC", "ETH" }
			: new[] { instrument.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "BTC" };
		List<object> rows = new List<object>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string currency in currencies.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			JsonElement result = await DeribitRpcAsync(client, "private/get_positions", new { currency, kind = "future" }, token, ct);
			if (result.ValueKind != JsonValueKind.Array)
			{
				continue;
			}
			foreach (JsonElement position in result.EnumerateArray())
			{
				string rowInstrument = GetJsonString(position, "instrument_name") ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(instrument) && !string.Equals(rowInstrument, instrument, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				string key = rowInstrument + ":" + (GetJsonString(position, "direction") ?? string.Empty);
				if (!seen.Add(key))
				{
					continue;
				}
				rows.Add(ToDeribitPositionRow(position));
			}
		}
		return rows
			.OrderBy(row => (row.GetType().GetProperty("symbol")?.GetValue(row) ?? string.Empty).ToString())
			.ToList();
	}

	private static object ToDeribitPositionRow(JsonElement position)
	{
		string instrument = GetJsonString(position, "instrument_name") ?? string.Empty;
		string direction = (GetJsonString(position, "direction") ?? string.Empty).Trim().ToLowerInvariant();
		int? side = direction == "buy" ? 1 : direction == "sell" ? 2 : null;
		double? size = GetJsonDouble(position, "size");
		double? sizeCurrency = GetJsonDouble(position, "size_currency");
		double? markPrice = GetJsonDouble(position, "mark_price");
		double? avgPrice = GetJsonDouble(position, "average_price");
		double? initialMargin = GetJsonDouble(position, "initial_margin");
		double? maintenanceMargin = GetJsonDouble(position, "maintenance_margin");
		double? floatingPnl = GetJsonDouble(position, "floating_profit_loss");
		double? realizedPnl = GetJsonDouble(position, "realized_profit_loss");
		double? totalPnl = GetJsonDouble(position, "total_profit_loss");
		double? delta = GetJsonDouble(position, "delta");
		double? leverage = GetJsonDouble(position, "leverage");
		string baseToken = instrument.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		return new
		{
			symbol = instrument,
			displaySymbol = instrument,
			displayName = instrument,
			instrument = instrument,
			instId = instrument,
			market = "futures",
			side,
			direction,
			tradeSide = direction,
			amount = sizeCurrency ?? size,
			qty = sizeCurrency ?? size,
			size,
			sizeCurrency,
			size_currency = sizeCurrency,
			position = sizeCurrency ?? size,
			avgPrice,
			averagePrice = avgPrice,
			entryPrice = avgPrice,
			markPrice,
			lastPrice = markPrice,
			indexPrice = GetJsonDouble(position, "index_price"),
			settlementPrice = GetJsonDouble(position, "settlement_price"),
			estLiquidationPrice = GetJsonDouble(position, "estimated_liquidation_price"),
			estimated_liquidation_price = GetJsonDouble(position, "estimated_liquidation_price"),
			initialMargin,
			initial_margin = initialMargin,
			initMargin = initialMargin,
			maintenanceMargin,
			maintenance_margin = maintenanceMargin,
			maintMargin = maintenanceMargin,
			openOrdersMargin = GetJsonDouble(position, "open_orders_margin"),
			open_orders_margin = GetJsonDouble(position, "open_orders_margin"),
			floatingProfitLoss = floatingPnl,
			floating_profit_loss = floatingPnl,
			unrealizedPnl = floatingPnl,
			upnl = floatingPnl,
			realizedProfitLoss = realizedPnl,
			realized_profit_loss = realizedPnl,
			realizedPnl,
			rpnl = realizedPnl,
			totalProfitLoss = totalPnl,
			total_profit_loss = totalPnl,
			pnl = totalPnl,
			realizedFunding = GetJsonDouble(position, "realized_funding"),
			realized_funding = GetJsonDouble(position, "realized_funding"),
			delta,
			leverage,
			baseToken,
			currency = baseToken,
			updateTime = GetJsonLong(position, "last_update_timestamp"),
			ts = GetJsonLong(position, "last_update_timestamp"),
			raw = position.Clone()
		};
	}

	private static async Task<List<DeribitOpenOrderRow>> FetchDeribitOpenOrdersAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, string market, CancellationToken ct)
	{
		using HttpClient client = new HttpClient
		{
			BaseAddress = new Uri(string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl)
		};
		string token = await DeribitAuthAsync(client, account.ApiPublicKey, apiSecretPlain, ct);
		string[] kinds = string.Equals(market, "spot", StringComparison.OrdinalIgnoreCase)
			? new[] { "spot" }
			: new[] { "future", "future_combo" };
		string[] currencies = string.Equals(market, "spot", StringComparison.OrdinalIgnoreCase)
			? new[] { "BTC", "ETH", "USDC" }
			: new[] { "BTC", "ETH" };
		List<DeribitOpenOrderRow> rows = new List<DeribitOpenOrderRow>();
		foreach (string kind in kinds)
		{
			foreach (string currency in currencies)
			{
				JsonElement result = await DeribitPrivateOpenOrdersByCurrencyAsync(client, token, currency, kind, ct);
				if (result.ValueKind != JsonValueKind.Array)
				{
					continue;
				}
				foreach (JsonElement order in result.EnumerateArray())
				{
					rows.Add(ToDeribitOpenOrderRow(order, market));
				}
			}
		}
		return rows
			.GroupBy(row => string.IsNullOrWhiteSpace(row.orderId) ? row.symbol + "|" + (row.createTime ?? row.ts ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture) : row.orderId)
			.Select(group => group.First())
			.OrderByDescending(row => row.updateTime ?? row.createTime ?? row.ts ?? 0)
			.ToList();
	}

	private static async Task<JsonElement> PlaceDeribitFuturesOrderAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, DeribitPlaceOrderRequest dto, CancellationToken ct)
	{
		string instrumentName = NormalizeDeribitFuturesInstrument(dto.symbol);
		string side = (dto.side ?? dto.tradeSide ?? string.Empty).Trim().ToLowerInvariant();
		if (side == "1")
		{
			side = "buy";
		}
		else if (side == "2")
		{
			side = "sell";
		}
		if (side != "buy" && side != "sell")
		{
			throw new InvalidOperationException("side must be buy or sell");
		}
		double amount = ReadPositiveDouble(dto.amount ?? dto.qty, "amount");
		string orderType = NormalizeDeribitOrderType(dto.type ?? dto.tradeType);
		Dictionary<string, object> parameters = new Dictionary<string, object>
		{
			["instrument_name"] = instrumentName,
			["amount"] = amount,
			["type"] = orderType
		};
		if (orderType == "limit")
		{
			double price = ReadPositiveDouble(dto.price, "price");
			parameters["price"] = price;
		}
		if (IsTruthy(dto.postOnly) || string.Equals(dto.type, "POST_ONLY", StringComparison.OrdinalIgnoreCase) || string.Equals(dto.tradeType, "3", StringComparison.OrdinalIgnoreCase))
		{
			parameters["post_only"] = true;
		}
		if (IsTruthy(dto.reduceOnly))
		{
			parameters["reduce_only"] = true;
		}
		using HttpClient client = new HttpClient
		{
			BaseAddress = new Uri(string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl)
		};
		string token = await DeribitAuthAsync(client, account.ApiPublicKey, apiSecretPlain, ct);
		return await DeribitRpcAsync(client, "private/" + side, parameters, token, ct);
	}

	private static async Task<JsonElement> CancelDeribitOrderAsync(VANWebServiceConfig cfg, DeribitAccountCredentials account, string apiSecretPlain, DeribitCancelOrderRequest dto, CancellationToken ct)
	{
		string orderId = (dto.orderId ?? dto.order_id ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(orderId))
		{
			throw new InvalidOperationException("order_id is required for Deribit private/cancel");
		}
		using HttpClient client = new HttpClient
		{
			BaseAddress = new Uri(string.IsNullOrWhiteSpace(cfg.DeribitBaseUrl) ? "https://www.deribit.com" : cfg.DeribitBaseUrl)
		};
		string token = await DeribitAuthAsync(client, account.ApiPublicKey, apiSecretPlain, ct);
		return await DeribitRpcAsync(client, "private/cancel", new { order_id = orderId }, token, ct);
	}

	private static string NormalizeDeribitFuturesInstrument(string? value)
	{
		string raw = (value ?? string.Empty).Trim().ToUpperInvariant();
		if (string.IsNullOrWhiteSpace(raw))
		{
			throw new InvalidOperationException("symbol is required");
		}
		if (raw == "BTCUSD" || raw == "BTCUSDT" || raw == "BTC-PERP" || raw == "BTC-PERPETUAL")
		{
			return "BTC-PERPETUAL";
		}
		if (raw == "ETHUSD" || raw == "ETHUSDT" || raw == "ETH-PERP" || raw == "ETH-PERPETUAL")
		{
			return "ETH-PERPETUAL";
		}
		System.Text.RegularExpressions.Match dated = System.Text.RegularExpressions.Regex.Match(raw, "^(BTC|ETH)USD-(\\d{1,2}[A-Z]{3}\\d{2})$");
		if (dated.Success)
		{
			return dated.Groups[1].Value + "-" + dated.Groups[2].Value;
		}
		return raw.Replace("USDT-PERP", "-PERPETUAL", StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeDeribitOrderType(string? value)
	{
		string raw = (value ?? "limit").Trim().ToUpperInvariant();
		return raw switch
		{
			"2" or "MARKET" => "market",
			"3" or "POST_ONLY" or "POSTONLY" => "limit",
			_ => "limit"
		};
	}

	private static double ReadPositiveDouble(string? value, string field)
	{
		if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) || parsed <= 0)
		{
			throw new InvalidOperationException(field + " must be a positive number");
		}
		return parsed;
	}

	private static bool IsTruthy(object? value)
	{
		if (value == null)
		{
			return false;
		}
		string text = value.ToString()?.Trim().ToLowerInvariant() ?? string.Empty;
		return text == "1" || text == "true" || text == "yes";
	}

	private static async Task<double> FetchDeribitMarkPriceAsync(HttpClient client, string instrumentName, CancellationToken ct)
	{
		using HttpResponseMessage resp = await client.GetAsync("/api/v2/public/ticker?instrument_name=" + Uri.EscapeDataString(instrumentName), ct);
		string text = await resp.Content.ReadAsStringAsync(ct);
		if (!resp.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Deribit HTTP {resp.StatusCode} on public/ticker: {text}");
		}
		using JsonDocument doc = JsonDocument.Parse(text);
		JsonElement result = doc.RootElement.GetProperty("result");
		double? markPrice = GetJsonDouble(result, "mark_price");
		if (!markPrice.HasValue || markPrice.Value <= 0)
		{
			throw new InvalidOperationException("Deribit ticker did not contain mark_price for " + instrumentName);
		}
		return markPrice.Value;
	}

	private static async Task<JsonElement> DeribitRpcAsync(HttpClient client, string method, object parameters, string? bearerToken, CancellationToken ct)
	{
		using HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, "/api/v2/" + method);
		if (!string.IsNullOrWhiteSpace(bearerToken))
		{
			msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}
		msg.Content = new StringContent(JsonSerializer.Serialize(new
		{
			jsonrpc = "2.0",
			id = 1,
			method,
			@params = parameters
		}), Encoding.UTF8, "application/json");
		using HttpResponseMessage resp = await client.SendAsync(msg, ct);
		string text = await resp.Content.ReadAsStringAsync(ct);
		if (!resp.IsSuccessStatusCode)
		{
			throw new HttpRequestException($"Deribit HTTP {resp.StatusCode} on {method}: {text}");
		}
		using JsonDocument doc = JsonDocument.Parse(text);
		if (doc.RootElement.TryGetProperty("error", out JsonElement error))
		{
			throw new InvalidOperationException("Deribit " + method + " error: " + error.ToString());
		}
		if (!doc.RootElement.TryGetProperty("result", out JsonElement result))
		{
			throw new InvalidOperationException("Deribit " + method + " response did not contain result");
		}
		return result.Clone();
	}

	private static double? GetJsonDouble(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out JsonElement value))
		{
			return null;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
		{
			return number;
		}
		if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
		{
			return number;
		}
		return null;
	}

	private static bool? GetJsonBool(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out JsonElement value))
		{
			return null;
		}
		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
			_ => null
		};
	}

	private static string? GetJsonString(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
		{
			return null;
		}
		return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
	}

	private static long? GetJsonLong(JsonElement element, string propertyName)
	{
		if (!element.TryGetProperty(propertyName, out JsonElement value))
		{
			return null;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
		{
			return number;
		}
		if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number))
		{
			return number;
		}
		return null;
	}

	private static DeribitOpenOrderRow ToDeribitOpenOrderRow(JsonElement order, string market)
	{
		string instrument = GetJsonString(order, "instrument_name") ?? string.Empty;
		string direction = (GetJsonString(order, "direction") ?? string.Empty).Trim().ToLowerInvariant();
		int? side = direction == "buy" ? 1 : direction == "sell" ? 2 : null;
		double? amount = GetJsonDouble(order, "amount");
		double? filled = GetJsonDouble(order, "filled_amount");
		double? price = GetJsonDouble(order, "price");
		double? avgPrice = GetJsonDouble(order, "average_price");
		string orderType = GetJsonString(order, "order_type") ?? string.Empty;
		string timeInForce = GetJsonString(order, "time_in_force") ?? string.Empty;
		bool? postOnly = GetJsonBool(order, "post_only") ?? (string.Equals(orderType, "limit", StringComparison.OrdinalIgnoreCase) && string.Equals(timeInForce, "post_only", StringComparison.OrdinalIgnoreCase));
		string baseToken = instrument.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
		long? createTime = GetJsonLong(order, "creation_timestamp");
		long? updateTime = GetJsonLong(order, "last_update_timestamp");
		return new DeribitOpenOrderRow(
			orderId: GetJsonString(order, "order_id") ?? string.Empty,
			clientOrderId: GetJsonString(order, "label") ?? string.Empty,
			symbol: instrument,
			displaySymbol: instrument,
			displayName: instrument,
			instrument: instrument,
			market: market,
			side: side,
			direction: direction,
			tradeSide: direction,
			amount: amount,
			qty: amount,
			size: amount,
			orderQty: amount,
			filled: filled,
			fillQty: filled,
			filledQty: filled,
			price: price,
			px: price,
			orderPrice: price,
			avgPrice: avgPrice,
			averagePrice: avgPrice,
			orderType: orderType,
			type: orderType,
			status: GetJsonString(order, "order_state") ?? "open",
			state: GetJsonString(order, "order_state") ?? "open",
			reduceOnly: GetJsonBool(order, "reduce_only"),
			postOnly: postOnly,
			timeInForce: timeInForce,
			baseToken: baseToken,
			currency: GetJsonString(order, "currency") ?? baseToken,
			createTime: createTime,
			createdTime: createTime,
			updateTime: updateTime,
			ts: updateTime ?? createTime,
			fee: GetDeribitHistoryFee(order),
			fees: GetDeribitHistoryFee(order),
			commission: GetDeribitHistoryFee(order),
			feeCurrency: GetDeribitHistoryFeeCurrency(order, baseToken),
			fee_currency: GetDeribitHistoryFeeCurrency(order, baseToken),
			rpnl: GetDeribitHistoryRealizedPnl(order),
			realizedPnl: GetDeribitHistoryRealizedPnl(order),
			role: GetDeribitHistoryRole(order),
			liquidity: GetDeribitHistoryRole(order),
			raw: order.Clone());
	}

	private static double? Multiply(double? value, double rate)
	{
		return value.HasValue ? value.Value * rate : null;
	}

	private sealed record DeribitPrivateRequestContext(DeribitAccountCredentials? Account, string? ApiSecretPlain, IResult? Result);

	private sealed record DeribitAccountCredentials(int AccId, string Name, string ApiPublicKey, string ApiSecretCipher);

	private sealed record DeribitAccountSummary(List<DeribitSummaryRow> Rows, double? TotalEquityUsd, double? AvailableUsd, double? InitialMarginUsd, double? MaintenanceMarginUsd, bool PortfolioMarginingEnabled, bool PortfolioMarginingObserved);

	private sealed record DeribitEquityPoint(DateTime TsUtc, decimal Value);

	private sealed record DeribitChartPoint(DateTime tsUtc, decimal value);

	private sealed record DeribitEquityPayload(object metrics, object daily, object minute);

	private sealed record DeribitOpenOrderRow(
		string orderId,
		string clientOrderId,
		string symbol,
		string displaySymbol,
		string displayName,
		string instrument,
		string market,
		int? side,
		string direction,
		string tradeSide,
		double? amount,
		double? qty,
		double? size,
		double? orderQty,
		double? filled,
		double? fillQty,
		double? filledQty,
		double? price,
		double? px,
		double? orderPrice,
		double? avgPrice,
		double? averagePrice,
		string orderType,
		string type,
		string status,
		string state,
		bool? reduceOnly,
		bool? postOnly,
		string timeInForce,
		string baseToken,
		string currency,
		long? createTime,
		long? createdTime,
		long? updateTime,
		long? ts,
		double? fee,
		double? fees,
		double? commission,
		string feeCurrency,
		string fee_currency,
		double? rpnl,
		double? realizedPnl,
		string? role,
		string? liquidity,
		JsonElement raw);

	private sealed class DeribitPlaceOrderRequest
	{
		public string? symbol { get; set; }
		public string? side { get; set; }
		public string? tradeSide { get; set; }
		public string? type { get; set; }
		public string? tradeType { get; set; }
		public string? amount { get; set; }
		public string? qty { get; set; }
		public string? price { get; set; }
		public object? postOnly { get; set; }
		public object? reduceOnly { get; set; }
	}

	private sealed class DeribitCancelOrderRequest
	{
		public string? orderId { get; set; }
		public string? order_id { get; set; }
	}

	private sealed record DeribitSummaryRow(
		string currency,
		double? equity,
		double? available,
		double? initialMargin,
		double? maintenanceMargin,
		double? equityUsd,
		double? availableUsd,
		double? initialMarginUsd,
		double? maintenanceMarginUsd,
		bool? portfolioMarginingEnabled,
		double usdRate,
		JsonElement raw);

	private sealed class DeribitCredentialsRequest
	{
		public string? apiPublicKey { get; set; }
		public string? apiSecretKeyPlain { get; set; }
		public string? apiKey { get; set; }
		public string? apiSecret { get; set; }
		public string? clientId { get; set; }
		public string? clientSecret { get; set; }
		public int? accId { get; set; }

		public string ApiPublicKeyValue => FirstNonEmpty(apiPublicKey, apiKey, clientId);
		public string ApiSecretValue => FirstNonEmpty(apiSecretKeyPlain, apiSecret, clientSecret);

		private static string FirstNonEmpty(params string?[] values)
		{
			foreach (string? value in values)
			{
				if (!string.IsNullOrWhiteSpace(value))
				{
					return value.Trim();
				}
			}
			return string.Empty;
		}
	}
}
