using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using CoreDTO.VANDTO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;
using VANWebService.Auth;
using VANWebService.Models;

namespace VANWebService;

public static class AdminEndpoints
{
	public static void MapAdminEndpoints(this WebApplication app)
	{
		app.MapGet("/api/admin/dashboard", (Func<HttpContext, Orchestrator, CoreDTO.Logger.Enums.LogAction, IResult>)delegate(HttpContext context, Orchestrator orchestrator, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				logAction("[GET] /api/admin/dashboard -> Unauthorized", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				logAction("[GET] /api/admin/dashboard -> Forbid (not Admin)", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Forbid();
			}
			List<Account> accounts = orchestrator.GetAccounts();
			double equityUSD = orchestrator.equityUSD;
			AdminDashboardResponseDTO data = new AdminDashboardResponseDTO
			{
				fundEquity = Boot.BuildSimpleCurve(equityUSD, 90),
				accounts = accounts.Select((Account a) => new AdminAccountDTO
				{
					accountId = a.accId,
					name = a.accName,
					equityUsd = 0.0,
					pnlUsd = 0.0,
					cids = a.listOfCIDs.Select((CID cid) => new AdminCidDTO
					{
						cidId = cid.InputData.Id,
						name = cid.InputData.Name,
						feeRatePercents = cid.InputData.FeeRatePercents
					}).ToList()
				}).ToList()
			};
			return Results.Json(data);
		});
		app.MapGet("/api/admin/account-equity-curve", (Func<HttpContext, IRecordsRepository, Task<IResult>>)async delegate(HttpContext context, IRecordsRepository recordsRepo)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			if (!context.Request.Query.TryGetValue("accountId", out var value))
			{
				return Results.BadRequest(new
				{
					message = "accountId query parameter is required"
				});
			}
			if (!int.TryParse(value.FirstOrDefault(), out var result))
			{
				return Results.BadRequest(new
				{
					message = "accountId must be integer"
				});
			}
			List<EquityPoint> source = await recordsRepo.GetAccountEquityCurveAsync(result);
			EquityCurveDTO data = new EquityCurveDTO
			{
				points = source.Select((EquityPoint p) => new EquityCurvePointDTO
				{
					dt = p.DtUtc,
					equityUsd = p.EquityUsd
				}).ToList()
			};
			return Results.Json(data);
		});
		app.MapGet("/api/admin/account-daily-equity", (Func<HttpContext, VANWebServiceConfig, Task<IResult>>)async delegate(HttpContext ctx, VANWebServiceConfig cfg)
		{
			if (!ctx.Request.Query.TryGetValue("accId", out var value) || !int.TryParse(value, out var accId))
			{
				return Results.BadRequest(new
				{
					message = "accId is required"
				});
			}
			List<object> rows = new List<object>();
			IResult result3;
			await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
			{
				await conn.OpenAsync(ctx.RequestAborted);
				IResult result2;
				await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nwith a as (\r\n  select\r\n    acc_id,\r\n    created_at::date as start_date,\r\n    include_usdc_in_total as a_inc_usdc,\r\n    include_eth_in_total  as a_inc_eth,\r\n    include_btc_in_total  as a_inc_btc\r\n  from public.van_account\r\n  where acc_id = @acc_id\r\n  limit 1\r\n),\r\nt as (\r\n  select (now() at time zone 'utc')::date as today_utc\r\n),\r\nendd as (\r\n  select\r\n    case\r\n      when exists (\r\n        select 1\r\n        from public.van_account_daily_equity e0\r\n        where e0.acc_id = a.acc_id\r\n          and e0.date_utc = t.today_utc\r\n      )\r\n      then t.today_utc\r\n      else (t.today_utc - 1)\r\n    end as end_date\r\n  from a, t\r\n)\r\nselect\r\n  a.acc_id,\r\n  d.date_utc,\r\n\r\n  e.usdc_equity,\r\n  e.eth_equity,\r\n  e.btc_equity,\r\n  e.eth_price,\r\n  e.btc_price,\r\n\r\n  e.eth_equity_usd,\r\n  e.btc_equity_usd,\r\n\r\n  coalesce(e.include_usdc_in_total, a.a_inc_usdc) as include_usdc_in_total,\r\n  coalesce(e.include_eth_in_total,  a.a_inc_eth)  as include_eth_in_total,\r\n  coalesce(e.include_btc_in_total,  a.a_inc_btc)  as include_btc_in_total,\r\n\r\n  (\r\n    coalesce(e.usdc_equity, 0) * (coalesce(e.include_usdc_in_total, a.a_inc_usdc)::int) +\r\n    coalesce(e.eth_equity_usd, 0) * (coalesce(e.include_eth_in_total,  a.a_inc_eth)::int) +\r\n    coalesce(e.btc_equity_usd, 0) * (coalesce(e.include_btc_in_total,  a.a_inc_btc)::int)\r\n  )::numeric(38,10) as total_equity_usd\r\n\r\nfrom a\r\njoin endd on true\r\njoin lateral generate_series(\r\n    a.start_date,\r\n    endd.end_date,\r\n    interval '1 day'\r\n) as d(date_utc) on true\r\nleft join public.van_account_daily_equity e\r\n  on e.acc_id = a.acc_id and e.date_utc = d.date_utc\r\norder by d.date_utc;\r\n", conn))
				{
					cmd.Parameters.AddWithValue("acc_id", accId);
					IResult result;
					await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ctx.RequestAborted))
					{
						while (await r.ReadAsync(ctx.RequestAborted))
						{
							rows.Add(new
							{
								accId = r.GetInt32(0),
								dateUtc = r.GetDateTime(1).Date,
								usdcEquity = (r.IsDBNull(2) ? ((decimal?)null) : new decimal?(r.GetDecimal(2))),
								ethEquity = (r.IsDBNull(3) ? ((decimal?)null) : new decimal?(r.GetDecimal(3))),
								btcEquity = (r.IsDBNull(4) ? ((decimal?)null) : new decimal?(r.GetDecimal(4))),
								ethPrice = (r.IsDBNull(5) ? ((decimal?)null) : new decimal?(r.GetDecimal(5))),
								btcPrice = (r.IsDBNull(6) ? ((decimal?)null) : new decimal?(r.GetDecimal(6))),
								ethEquityUsd = (r.IsDBNull(7) ? ((decimal?)null) : new decimal?(r.GetDecimal(7))),
								btcEquityUsd = (r.IsDBNull(8) ? ((decimal?)null) : new decimal?(r.GetDecimal(8))),
								includeUsdcInTotal = r.GetBoolean(9),
								includeEthInTotal = r.GetBoolean(10),
								includeBtcInTotal = r.GetBoolean(11),
								totalEquityUsd = (r.IsDBNull(12) ? ((decimal?)null) : new decimal?(r.GetDecimal(12)))
							});
						}
						result = Results.Json(new { accId, rows });
					}
					result2 = result;
				}
				result3 = result2;
			}
			return result3;
		});
		app.MapGet("/api/admin/registrations", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				logAction("[GET] /api/admin/registrations -> Unauthorized", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				logAction("[GET] /api/admin/registrations -> Forbid (not Admin)", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Forbid();
			}
			List<PublicRegistrationListItemDTO> items = new List<PublicRegistrationListItemDTO>();
			try
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync();
				await using NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect\r\n    id,\r\n    full_name,\r\n    email,\r\n    telegram,\r\n    country,\r\n    city,\r\n    planned_investment_usd,\r\n    comment,\r\n    timestamp_utc\r\nfrom van_public_registration\r\norder by timestamp_utc desc;\r\n", conn);
				await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
				while (await reader.ReadAsync())
				{
					PublicRegistrationListItemDTO item = new PublicRegistrationListItemDTO
					{
						id = reader.GetInt32(reader.GetOrdinal("id")),
						fullName = ((reader["full_name"] as string) ?? ""),
						email = ((reader["email"] as string) ?? ""),
						telegram = (reader["telegram"] as string),
						country = (reader["country"] as string),
						city = (reader["city"] as string),
						plannedInvestmentUsd = ((reader["planned_investment_usd"] is DBNull) ? ((double?)null) : new double?(Convert.ToDouble(reader["planned_investment_usd"]))),
						comment = (reader["comment"] as string),
						createdAtUtc = reader.GetDateTime(reader.GetOrdinal("timestamp_utc"))
					};
					items.Add(item);
				}
			}
			catch (Exception value)
			{
				logAction($"[REG][LIST][ERROR] {value}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			return Results.Json(new PublicRegistrationListResponseDTO
			{
				items = items
			});
		});
		app.MapPost("/api/admin/registrations/delete", (Func<HttpContext, DeleteRegistrationRequestDTO, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, DeleteRegistrationRequestDTO dto, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession session = UserSessionTools.GetUserSession(context);
			if (session == null)
			{
				logAction("[POST] /api/admin/registrations/delete -> Unauthorized", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				logAction("[POST] /api/admin/registrations/delete -> Forbid (not Admin)", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Forbid();
			}
			try
			{
				IResult result2;
				await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
				{
					await conn.OpenAsync();
					IResult result;
					await using (NpgsqlCommand cmd = new NpgsqlCommand("delete from van_public_registration where id = @id;", conn))
					{
						cmd.Parameters.AddWithValue("id", dto.registrationId);
						int rows = await cmd.ExecuteNonQueryAsync();
						logAction($"[REG][DELETE] registration #{dto.registrationId}, rowsAffected={rows}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
						if (!string.IsNullOrWhiteSpace(cfg.TelegramBotToken) && !string.IsNullOrWhiteSpace(cfg.TelegramChatId))
						{
							try
							{
								using HttpClient http = new HttpClient();
								string value = $"VAN: registration #{dto.registrationId} deleted by {session.Login} ({session.Role}).";
								string requestUri = "https://api.telegram.org/bot" + cfg.TelegramBotToken + "/sendMessage";
								Dictionary<string, string> nameValueCollection = new Dictionary<string, string>
								{
									["chat_id"] = cfg.TelegramChatId,
									["text"] = value
								};
								await http.PostAsync(requestUri, new FormUrlEncodedContent(nameValueCollection));
							}
							catch (Exception ex)
							{
								logAction("[REG][DELETE][TG][ERROR] " + ex.Message, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
							}
						}
						result = Results.Json(new
						{
							ok = true,
							rows = rows
						});
					}
					result2 = result;
				}
				return result2;
			}
			catch (Exception ex2)
			{
				logAction($"[REG][DELETE][ERROR] {ex2}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Json(new
				{
					ok = false,
					error = ex2.Message
				});
			}
		});
		app.MapPost("/api/admin/users/create", (Func<HttpContext, CreateUserRequestDTO, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, CreateUserRequestDTO dto, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			if (dto == null || string.IsNullOrWhiteSpace(dto.login) || string.IsNullOrWhiteSpace(dto.password))
			{
				return Results.BadRequest(new CreateUserResponseDTO
				{
					ok = false,
					error = "login and password are required"
				});
			}
			string role = (string.IsNullOrWhiteSpace(dto.role) ? "User" : dto.role.Trim());
			string accountType = (string.IsNullOrWhiteSpace(dto.accountType) ? "Individual" : dto.accountType.Trim());
			bool isSeg = string.Equals(accountType, "Segregated", StringComparison.OrdinalIgnoreCase);
			if (isSeg)
			{
				if (!dto.segregatedAccountId.HasValue || dto.segregatedAccountId.Value <= 0)
				{
					return Results.BadRequest(new CreateUserResponseDTO
					{
						ok = false,
						error = "segregatedAccountId is required for Segregated user"
					});
				}
				if (!dto.cidId.HasValue || dto.cidId.Value <= 0)
				{
					return Results.BadRequest(new CreateUserResponseDTO
					{
						ok = false,
						error = "cidId is required for Segregated user"
					});
				}
			}
			else
			{
				dto.segregatedAccountId = null;
				dto.cidId = null;
			}
			try
			{
				IResult result2;
				await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
				{
					await conn.OpenAsync(context.RequestAborted);
					IResult result;
					await using (NpgsqlTransaction tx = await conn.BeginTransactionAsync(context.RequestAborted))
					{
						int newUserId;
						await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\ninsert into public.van_user\r\n(login, password, full_name, role, timestamp_utc, is_active,\r\n investment_usd, account_type, segregated_account_id, cid_id)\r\nvalues\r\n(@login, @password, @full_name, @role, now(), @is_active,\r\n @investment_usd, @account_type, @segregated_account_id, @cid_id)\r\nreturning user_id;\r\n", conn, tx))
						{
							cmd.Parameters.AddWithValue("login", dto.login.Trim());
							cmd.Parameters.AddWithValue("password", AuthorityRepository.HashPassword(dto.password));
							cmd.Parameters.AddWithValue("full_name", ((object)(string.IsNullOrWhiteSpace(dto.fullName) ? dto.login.Trim() : dto.fullName.Trim())) ?? ((object)DBNull.Value));
							cmd.Parameters.AddWithValue("role", role);
							cmd.Parameters.AddWithValue("is_active", dto.isActive);
							cmd.Parameters.AddWithValue("investment_usd", ((object)dto.investmentUsd) ?? DBNull.Value);
							cmd.Parameters.AddWithValue("account_type", isSeg ? "Segregated" : "Individual");
							cmd.Parameters.AddWithValue("segregated_account_id", isSeg ? ((object)dto.segregatedAccountId.Value) : DBNull.Value);
							cmd.Parameters.AddWithValue("cid_id", isSeg ? ((object)dto.cidId.Value) : DBNull.Value);
							newUserId = Convert.ToInt32(await cmd.ExecuteScalarAsync(context.RequestAborted));
						}
						if (dto.accIds != null && dto.accIds.Length != 0)
						{
							foreach (int item2 in dto.accIds.Distinct())
							{
								if (item2 > 0)
								{
									await using NpgsqlCommand cmd = new NpgsqlCommand("\r\ninsert into public.van_user_account(user_id, acc_id, created_utc)\r\nvalues (@uid, @aid, now())\r\non conflict (user_id, acc_id) do nothing;\r\n", conn, tx);
									cmd.Parameters.AddWithValue("uid", newUserId);
									cmd.Parameters.AddWithValue("aid", item2);
									await cmd.ExecuteNonQueryAsync(context.RequestAborted);
								}
							}
						}
						await tx.CommitAsync(context.RequestAborted);
						logAction($"[USERS][CREATE] user #{newUserId} login='{dto.login}' role={role} type={accountType} segAcc={dto.segregatedAccountId} cid={dto.cidId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
						result = Results.Json(new CreateUserResponseDTO
						{
							ok = true,
							userId = newUserId
						});
					}
					result2 = result;
				}
				return result2;
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return Results.Json(new CreateUserResponseDTO
				{
					ok = false,
					error = "login already exists"
				}, (JsonSerializerOptions?)null, (string?)null, (int?)409);
			}
			catch (Exception ex2)
			{
				logAction($"[USERS][CREATE][ERROR] {ex2}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.Json(new CreateUserResponseDTO
				{
					ok = false,
					error = ex2.Message
				}, (JsonSerializerOptions?)null, (string?)null, (int?)500);
			}
		});
		app.MapGet("/api/admin/users", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			List<AdminUserListItemDTO> items = new List<AdminUserListItemDTO>();
			try
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync();
				await using NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect\r\n  u.user_id,\r\n  u.login,\r\n  u.full_name,\r\n  u.role,\r\n  u.is_active,\r\n  u.timestamp_utc,\r\n  u.investment_usd,\r\n  u.account_type,\r\n  u.segregated_account_id,\r\n  u.cid_id,\r\n  coalesce(\r\n    array_agg(ua.acc_id order by ua.acc_id) filter (where ua.acc_id is not null),\r\n    '{}'::int[]\r\n  ) as acc_ids\r\nfrom public.van_user u\r\nleft join public.van_user_account ua on ua.user_id = u.user_id\r\ngroup by\r\n  u.user_id, u.login, u.full_name, u.role, u.is_active, u.timestamp_utc,\r\n  u.investment_usd, u.account_type, u.segregated_account_id, u.cid_id\r\norder by u.user_id;\r\n", conn);
				await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
				int ordUserId = reader.GetOrdinal("user_id");
				int ordLogin = reader.GetOrdinal("login");
				int ordFullName = reader.GetOrdinal("full_name");
				int ordRole = reader.GetOrdinal("role");
				int ordIsActive = reader.GetOrdinal("is_active");
				int ordTs = reader.GetOrdinal("timestamp_utc");
				int ordInv = reader.GetOrdinal("investment_usd");
				int ordType = reader.GetOrdinal("account_type");
				int ordSeg = reader.GetOrdinal("segregated_account_id");
				int ordCid = reader.GetOrdinal("cid_id");
				int ordAccIds = reader.GetOrdinal("acc_ids");
				while (await reader.ReadAsync())
				{
					AdminUserListItemDTO item = new AdminUserListItemDTO
					{
						userId = reader.GetInt32(ordUserId),
						login = (reader.IsDBNull(ordLogin) ? "" : reader.GetString(ordLogin)),
						fullName = (reader.IsDBNull(ordFullName) ? "" : reader.GetString(ordFullName)),
						role = (reader.IsDBNull(ordRole) ? "" : reader.GetString(ordRole)),
						isActive = (!reader.IsDBNull(ordIsActive) && reader.GetBoolean(ordIsActive)),
						timestampUtc = (reader.IsDBNull(ordTs) ? DateTime.MinValue : reader.GetDateTime(ordTs)),
						investmentUsd = (reader.IsDBNull(ordInv) ? ((double?)null) : new double?(Convert.ToDouble(reader.GetValue(ordInv)))),
						accountType = (reader.IsDBNull(ordType) ? "" : reader.GetString(ordType)),
						segregatedAccountId = (reader.IsDBNull(ordSeg) ? ((int?)null) : new int?(reader.GetInt32(ordSeg))),
						cidId = (reader.IsDBNull(ordCid) ? ((long?)null) : new long?(reader.GetInt64(ordCid))),
						accIds = (reader.IsDBNull(ordAccIds) ? Array.Empty<int>() : reader.GetFieldValue<int[]>(ordAccIds))
					};
					items.Add(item);
				}
			}
			catch (Exception value)
			{
				logAction($"[USERS][LIST][ERROR] {value}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			return Results.Json(new AdminUserListResponseDTO
			{
				items = items
			});
		});
		app.MapPost("/api/admin/users/delete", (Func<HttpContext, DeleteUserRequestDTO, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, DeleteUserRequestDTO dto, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession session = UserSessionTools.GetUserSession(context);
			if (session == null)
			{
				logAction("[POST] /api/admin/users/delete -> Unauthorized", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				logAction("[POST] /api/admin/users/delete -> Forbid (not Admin)", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Forbid();
			}
			try
			{
				IResult result2;
				await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
				{
					await conn.OpenAsync();
					IResult result;
					await using (NpgsqlCommand cmd = new NpgsqlCommand("delete from van_user where user_id = @id;", conn))
					{
						cmd.Parameters.AddWithValue("id", dto.userId);
						int rows = await cmd.ExecuteNonQueryAsync();
						logAction($"[USERS][DELETE] user #{dto.userId}, rowsAffected={rows}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
						if (!string.IsNullOrWhiteSpace(cfg.TelegramBotToken) && !string.IsNullOrWhiteSpace(cfg.TelegramChatId))
						{
							try
							{
								using HttpClient http = new HttpClient();
								string value = $"VAN: van_user #{dto.userId} deleted by {session.Login} ({session.Role}).";
								string requestUri = "https://api.telegram.org/bot" + cfg.TelegramBotToken + "/sendMessage";
								Dictionary<string, string> nameValueCollection = new Dictionary<string, string>
								{
									["chat_id"] = cfg.TelegramChatId,
									["text"] = value
								};
								await http.PostAsync(requestUri, new FormUrlEncodedContent(nameValueCollection));
							}
							catch (Exception ex)
							{
								logAction("[USERS][DELETE][TG][ERROR] " + ex.Message, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
							}
						}
						result = Results.Json(new
						{
							ok = true,
							rows = rows
						});
					}
					result2 = result;
				}
				return result2;
			}
			catch (Exception ex2)
			{
				logAction($"[USERS][DELETE][ERROR] {ex2}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Json(new
				{
					ok = false,
					error = ex2.Message
				});
			}
		});
		app.MapPost("/api/admin/users/bind-account", (Func<HttpContext, BindUserAccountRequestDTO, VANWebServiceConfig, Task<IResult>>)async delegate(HttpContext context, BindUserAccountRequestDTO dto, VANWebServiceConfig cfg)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			try
			{
				IResult result2;
				await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
				{
					await conn.OpenAsync();
					IResult result;
					await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\ninsert into van_user_account(user_id, acc_id)\r\nvalues (@uid, @aid)\r\non conflict (user_id, acc_id) do nothing;\r\n", conn))
					{
						cmd.Parameters.AddWithValue("uid", dto.userId);
						cmd.Parameters.AddWithValue("aid", dto.accId);
						result = Results.Json(new
						{
							ok = true,
							rows = await cmd.ExecuteNonQueryAsync()
						});
					}
					result2 = result;
				}
				return result2;
			}
			catch (Exception ex)
			{
				return Results.Json(new
				{
					ok = false,
					error = ex.Message
				});
			}
		});
		app.MapPost("/api/admin/users/unbind-account", (Func<HttpContext, BindUserAccountRequestDTO, VANWebServiceConfig, Task<IResult>>)async delegate(HttpContext context, BindUserAccountRequestDTO dto, VANWebServiceConfig cfg)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			try
			{
				IResult result2;
				await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
				{
					await conn.OpenAsync();
					IResult result;
					await using (NpgsqlCommand cmd = new NpgsqlCommand("delete from van_user_account where user_id=@uid and acc_id=@aid;", conn))
					{
						cmd.Parameters.AddWithValue("uid", dto.userId);
						cmd.Parameters.AddWithValue("aid", dto.accId);
						result = Results.Json(new
						{
							ok = true,
							rows = await cmd.ExecuteNonQueryAsync()
						});
					}
					result2 = result;
				}
				return result2;
			}
			catch (Exception ex)
			{
				return Results.Json(new
				{
					ok = false,
					error = ex.Message
				});
			}
		});
		app.MapPost("/api/updateAccountIncludeFlags", (Func<HttpContext, UpdateAccountIncludeFlagsRequestDTO, VANWebServiceConfig, Task<IResult>>)async delegate(HttpContext context, UpdateAccountIncludeFlagsRequestDTO dto, VANWebServiceConfig cfg)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			IResult result2;
			await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
			{
				await conn.OpenAsync(context.RequestAborted);
				IResult result;
				await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nupdate public.van_account\r\nset\r\n  include_usdc_in_total = @usdc,\r\n  include_btc_in_total  = @btc,\r\n  include_eth_in_total  = @eth\r\nwhere acc_id = @accId;\r\n", conn))
				{
					cmd.Parameters.AddWithValue("accId", dto.accId);
					cmd.Parameters.AddWithValue("usdc", dto.includeUsdcInTotal);
					cmd.Parameters.AddWithValue("btc", dto.includeBtcInTotal);
					cmd.Parameters.AddWithValue("eth", dto.includeEthInTotal);
					result = Results.Json(new
					{
						ok = (await cmd.ExecuteNonQueryAsync(context.RequestAborted) > 0)
					});
				}
				result2 = result;
			}
			return result2;
		});
		app.MapPost("/api/admin/clearing/setAccountSchedule", (Func<HttpContext, SetAccountScheduleRequestDTO, IAccountsRepository, Orchestrator, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, SetAccountScheduleRequestDTO dto, IAccountsRepository accountsRepo, Orchestrator orchestrator, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				logAction("[POST] /api/admin/clearing/setAccountSchedule -> Unauthorized", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				logAction("[POST] /api/admin/clearing/setAccountSchedule -> Forbid (not Admin)", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Forbid();
			}
			var (flag, error, nearestClearingTimestampUtc, timeSpan) = ClearingScheduleTools.TryComputeNearestClearingTimestampUtc(dto.clearingEnabled, dto.clearingPeriod, dto.clearingTimeUtc, dto.clearingDayOfWeek, dto.clearingDayOfMonth);
			if (!flag)
			{
				return Results.Json(new
				{
					ok = false,
					error = error
				}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
			}
			var (flag2, error2, nearestClearingTimestampUtc2) = await accountsRepo.SetAccountClearingScheduleAsync(dto.accountId, dto.clearingEnabled, dto.clearingPeriod ?? "None", timeSpan.Value, dto.clearingDayOfWeek, dto.clearingDayOfMonth, nearestClearingTimestampUtc);
			if (!flag2)
			{
				return Results.Json(new
				{
					ok = false,
					error = error2
				}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
			}
			orchestrator.UpdateAccountClearingSchedule(dto.accountId, dto.clearingEnabled, dto.clearingPeriod ?? "None", dto.clearingTimeUtc ?? "00:00:00", dto.clearingDayOfWeek, dto.clearingDayOfMonth, nearestClearingTimestampUtc2);
			return Results.Json(new
			{
				ok = true,
				nearestClearingTimestampUtc = nearestClearingTimestampUtc2
			});
		});
		app.MapPost("/api/admin/clearing/doClearingNow", (Func<HttpContext, DoClearingNowRequestDTO, IAccountsRepository, Orchestrator, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, DoClearingNowRequestDTO dto, IAccountsRepository accountsRepo, Orchestrator orchestrator, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				logAction("[POST] /api/admin/clearing/doClearingNow -> Unauthorized", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				logAction("[POST] /api/admin/clearing/doClearingNow -> Forbid (not Admin)", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Forbid();
			}
			Account acc = orchestrator.GetAccountById(dto.accountId);
			if (acc == null)
			{
				return Results.Json(new
				{
					ok = false,
					error = $"Account {dto.accountId} not found"
				}, (JsonSerializerOptions?)null, (string?)null, (int?)400);
			}
			orchestrator.OnNext(new AccMutationDTO
			{
				mutationType = MutationType.Clearing,
				acc = acc,
				cidId = -1,
				dt = DateTime.UtcNow,
				usdValue = 0.0,
				equityUsd = null
			});
			var (flag, _, nearestClearingTimestampUtc, timeSpan) = ClearingScheduleTools.TryComputeNearestClearingTimestampUtc(acc.clearingEnabled, acc.clearingPeriod, acc.clearingTimeUtc, acc.clearingDayOfWeek, acc.clearingDayOfMonth);
			if (flag && timeSpan.HasValue)
			{
				var (flag2, _, nearestClearingTimestampUtc2) = await accountsRepo.SetAccountClearingScheduleAsync(acc.accId, acc.clearingEnabled, acc.clearingPeriod ?? "None", timeSpan.Value, acc.clearingDayOfWeek, acc.clearingDayOfMonth, nearestClearingTimestampUtc);
				if (flag2)
				{
					orchestrator.UpdateAccountClearingSchedule(acc.accId, acc.clearingEnabled, acc.clearingPeriod ?? "None", acc.clearingTimeUtc ?? "00:00:00", acc.clearingDayOfWeek, acc.clearingDayOfMonth, nearestClearingTimestampUtc2);
				}
			}
			return Results.Json(new
			{
				ok = true
			});
		});
		app.MapGet("/api/admin/contact-messages", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			List<object> items = new List<object>();
			try
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync(context.RequestAborted);
				await using NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect\r\n  id,\r\n  \"name\",\r\n  email,\r\n  message,\r\n  ip,\r\n  user_agent,\r\n  timestamp_utc\r\nfrom public.van_public_contact_message\r\norder by timestamp_utc desc;\r\n", conn);
				await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(context.RequestAborted);
				while (await r.ReadAsync(context.RequestAborted))
				{
					int @int = r.GetInt32(0);
					string name = (r.IsDBNull(1) ? "" : r.GetString(1));
					string email = (r.IsDBNull(2) ? "" : r.GetString(2));
					string text = (r.IsDBNull(3) ? "" : r.GetString(3));
					string ip = (r.IsDBNull(4) ? null : r.GetString(4));
					string userAgent = (r.IsDBNull(5) ? null : r.GetString(5));
					DateTime createdAtUtc = (r.IsDBNull(6) ? DateTime.UtcNow : r.GetDateTime(6));
					string text2 = text ?? "";
					if (text2.Length > 200)
					{
						text2 = text2.Substring(0, 200) + "…";
					}
					items.Add(new
					{
						id = @int,
						name = name,
						email = email,
						ip = ip,
						userAgent = userAgent,
						createdAtUtc = createdAtUtc,
						messagePreview = text2,
						messageLen = (text ?? "").Length
					});
				}
			}
			catch (Exception ex)
			{
				logAction($"[CONTACT][ADMIN][LIST][ERROR] {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.Json(new
				{
					ok = false,
					error = ex.Message
				}, (JsonSerializerOptions?)null, (string?)null, (int?)500);
			}
			return Results.Json(new
			{
				ok = true,
				items = items
			});
		});
		app.MapGet("/api/admin/contact-messages/get", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession userSession = UserSessionTools.GetUserSession(context);
			if (userSession == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(userSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			if (!context.Request.Query.TryGetValue("contactMessageId", out var value))
			{
				return Results.BadRequest(new
				{
					message = "contactMessageId query parameter is required"
				});
			}
			if (!int.TryParse(value.FirstOrDefault(), out var id) || id <= 0)
			{
				return Results.BadRequest(new
				{
					message = "contactMessageId must be integer"
				});
			}
			try
			{
				IResult result3;
				await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
				{
					await conn.OpenAsync(context.RequestAborted);
					IResult result2;
					await using (NpgsqlCommand cmd = new NpgsqlCommand("\r\nselect\r\n  id,\r\n  \"name\",\r\n  email,\r\n  message,\r\n  ip,\r\n  user_agent,\r\n  timestamp_utc\r\nfrom public.van_public_contact_message\r\nwhere id = @id\r\nlimit 1;\r\n", conn))
					{
						cmd.Parameters.AddWithValue("id", id);
						IResult result;
						await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(context.RequestAborted))
						{
							if (!(await r.ReadAsync(context.RequestAborted)))
							{
								result = Results.NotFound(new
								{
									message = $"contactMessageId={id} not found"
								});
							}
							else
							{
								var item = new
								{
									id = r.GetInt32(0),
									name = (r.IsDBNull(1) ? "" : r.GetString(1)),
									email = (r.IsDBNull(2) ? "" : r.GetString(2)),
									message = (r.IsDBNull(3) ? "" : r.GetString(3)),
									ip = (r.IsDBNull(4) ? null : r.GetString(4)),
									userAgent = (r.IsDBNull(5) ? null : r.GetString(5)),
									createdAtUtc = (r.IsDBNull(6) ? DateTime.UtcNow : r.GetDateTime(6))
								};
								result = Results.Json(new
								{
									ok = true,
									item = item
								});
							}
						}
						result2 = result;
					}
					result3 = result2;
				}
				return result3;
			}
			catch (Exception ex)
			{
				logAction($"[CONTACT][ADMIN][GET][ERROR] {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.Json(new
				{
					ok = false,
					error = ex.Message
				}, (JsonSerializerOptions?)null, (string?)null, (int?)500);
			}
		});
		app.MapPost("/api/admin/contact-messages/delete", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession session = UserSessionTools.GetUserSession(context);
			if (session == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			int id;
			try
			{
				using StreamReader sr = new StreamReader(context.Request.Body);
				JsonDocument jsonDocument = JsonDocument.Parse(await sr.ReadToEndAsync());
				if (!jsonDocument.RootElement.TryGetProperty("contactMessageId", out var value))
				{
					return Results.BadRequest(new
					{
						message = "contactMessageId is required"
					});
				}
				id = value.GetInt32();
				if (id <= 0)
				{
					return Results.BadRequest(new
					{
						message = "contactMessageId must be > 0"
					});
				}
			}
			catch
			{
				return Results.BadRequest(new
				{
					message = "Invalid JSON body"
				});
			}
			try
			{
				IResult result2;
				await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
				{
					await conn.OpenAsync(context.RequestAborted);
					IResult result;
					await using (NpgsqlCommand cmd = new NpgsqlCommand("delete from public.van_public_contact_message where id = @id;", conn))
					{
						cmd.Parameters.AddWithValue("id", id);
						int num = await cmd.ExecuteNonQueryAsync(context.RequestAborted);
						logAction($"[CONTACT][ADMIN][DELETE] id={id}, rowsAffected={num}, by={session.Login}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
						result = Results.Json(new
						{
							ok = true,
							rows = num
						});
					}
					result2 = result;
				}
				return result2;
			}
			catch (Exception ex)
			{
				logAction($"[CONTACT][ADMIN][DELETE][ERROR] {ex}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.Json(new
				{
					ok = false,
					error = ex.Message
				}, (JsonSerializerOptions?)null, (string?)null, (int?)500);
			}
		});
	}
}
