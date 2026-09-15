using System;
using System.Collections.Generic;
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

public static class UserEndpoints
{
	public static void MapUserEndpoints(this WebApplication app)
	{
		app.MapGet("/api/user/dashboard", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession session = UserSessionTools.GetUserSession(context);
			if (session == null)
			{
				logAction("[GET] /api/user/dashboard -> Unauthorized", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "User", StringComparison.OrdinalIgnoreCase) && !string.Equals(session.Role, "Investor", StringComparison.OrdinalIgnoreCase))
			{
				logAction("[GET] /api/user/dashboard -> Forbid (not User/Investor)", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Forbid();
			}
			double equityTotalUsd = 0.0;
			double investmentUsd = 0.0;
			double pnlPct = 0.0;
			int? linkedAccId = null;
			string linkedAccName = null;
			try
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync();
				await using (NpgsqlCommand cmdAcc = new NpgsqlCommand("\nselect ua.acc_id, a.acc_name\nfrom van_user_account ua\njoin van_account a on a.acc_id = ua.acc_id\nwhere ua.user_id = @uid\norder by ua.acc_id\nlimit 1;\n", conn))
				{
					cmdAcc.Parameters.AddWithValue("uid", session.UserId);
					await using NpgsqlDataReader rdrAcc = await cmdAcc.ExecuteReaderAsync();
					if (await rdrAcc.ReadAsync())
					{
						linkedAccId = (rdrAcc.IsDBNull(0) ? ((int?)null) : new int?(rdrAcc.GetInt32(0)));
						linkedAccName = (rdrAcc.IsDBNull(1) ? null : rdrAcc.GetString(1));
					}
				}
				await using (NpgsqlCommand cmdAcc2 = new NpgsqlCommand("\nselect investment_usd\nfrom van_user\nwhere user_id = @uid;\n", conn))
				{
					cmdAcc2.Parameters.AddWithValue("uid", session.UserId);
					object obj = await cmdAcc2.ExecuteScalarAsync();
					if (obj != null && obj != DBNull.Value)
					{
						investmentUsd = Convert.ToDouble(obj);
					}
				}
				await using NpgsqlCommand cmdAcc3 = new NpgsqlCommand("\nselect equity_total_usd, pnl_pct\nfrom van_user_equity\nwhere user_id = @uid\norder by timestamp_utc desc\nlimit 1;\n", conn);
				cmdAcc3.Parameters.AddWithValue("uid", session.UserId);
				await using NpgsqlDataReader rdrAcc2 = await cmdAcc3.ExecuteReaderAsync();
				if (await rdrAcc2.ReadAsync())
				{
					if (!rdrAcc2.IsDBNull(0))
					{
						equityTotalUsd = Convert.ToDouble(rdrAcc2.GetValue(0));
					}
					if (!rdrAcc2.IsDBNull(1))
					{
						pnlPct = Convert.ToDouble(rdrAcc2.GetValue(1));
					}
				}
			}
			catch (Exception value)
			{
				logAction($"[USER][DASHBOARD][ERROR] {value}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
			}
			UserDashboardResponseDTO data = new UserDashboardResponseDTO
			{
				user = new LoginUserDTO
				{
					UserId = session.UserId,
					Login = session.Login,
					FullName = session.Login,
					Role = session.Role
				},
				profile = new UserProfileDTO
				{
					fullName = session.Login,
					email = "",
					telegram = null,
					country = null,
					city = null
				},
				account = new UserAccountDTO
				{
					accountId = (linkedAccId ?? session.UserId),
					name = (linkedAccName ?? session.Login),
					equityUsd = equityTotalUsd,
					pnlUsd = pnlPct,
					depositsUsd = 0.0,
					withdrawalsUsd = 0.0,
					investmentUsd = investmentUsd
				},
				equityCurve = null
			};
			return Results.Json(data);
		});
		app.MapGet("/api/user/equity-curve", (Func<HttpContext, VANWebServiceConfig, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession session = UserSessionTools.GetUserSession(context);
			if (session == null)
			{
				logAction("[GET] /api/user/equity-curve -> Unauthorized", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "User", StringComparison.OrdinalIgnoreCase) && !string.Equals(session.Role, "Investor", StringComparison.OrdinalIgnoreCase))
			{
				logAction("[GET] /api/user/equity-curve -> Forbid (not User/Investor)", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				return Results.Forbid();
			}
			List<EquityCurvePointDTO> points = new List<EquityCurvePointDTO>();
			try
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync(context.RequestAborted);
				await using NpgsqlCommand cmd = new NpgsqlCommand("\nwith accs as (\n  select\n    a.acc_id,\n    a.include_usdc_in_total as inc_usdc,\n    a.include_btc_in_total  as inc_btc,\n    a.include_eth_in_total  as inc_eth\n  from public.van_user_account ua\n  join public.van_account a on a.acc_id = ua.acc_id\n  where ua.user_id = @uid\n),\nq as (\n  select\n    date_trunc('minute', ae.timestamp_utc) as ts_min,\n    sum(\n      (case when accs.inc_usdc then coalesce(ae.equity_usdc_usd, 0) else 0 end) +\n      (case when accs.inc_btc  then coalesce(ae.equity_btc_usd,  0) else 0 end) +\n      (case when accs.inc_eth  then coalesce(ae.equity_eth_usd,  0) else 0 end)\n    ) as equity_total_usd\n  from public.van_account_equity ae\n  join accs on accs.acc_id = ae.acc_id\n  left join public.van_outage_intervals oi\n      on ae.timestamp_utc >= oi.started_at and ae.timestamp_utc < oi.ended_at\n  where ae.timestamp_utc >= (now() at time zone 'utc' - interval '24 hours')\n    and oi.id is null\n  group by date_trunc('minute', ae.timestamp_utc)\n  order by ts_min\n)\nselect ts_min, equity_total_usd\nfrom q;\n", conn);
				cmd.Parameters.AddWithValue("uid", session.UserId);
				await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(context.RequestAborted);
				while (await reader.ReadAsync(context.RequestAborted))
				{
					DateTime dateTime = reader.GetDateTime(0);
					double equityUsd = (reader.IsDBNull(1) ? 0.0 : Convert.ToDouble(reader.GetValue(1)));
					points.Add(new EquityCurvePointDTO
					{
						dt = dateTime,
						equityUsd = equityUsd
					});
				}
				logAction($"[GET] /api/user/equity-curve -> {points.Count} points for user {session.UserId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			catch (Exception value)
			{
				logAction($"[GET] /api/user/equity-curve[ERROR] {value}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
			}
			return Results.Json(new EquityCurveDTO
			{
				points = points
			});
		});
		app.MapGet("/api/user/accounts", (Func<HttpContext, VANWebServiceConfig, Orchestrator, Task<IResult>>)async delegate(HttpContext context, VANWebServiceConfig cfg, Orchestrator orchestrator)
		{
			UserSession session = UserSessionTools.GetUserSession(context);
			if (session == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "User", StringComparison.OrdinalIgnoreCase) && !string.Equals(session.Role, "Investor", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			List<UserAccountListItemDTO> items = new List<UserAccountListItemDTO>();
			try
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync(context.RequestAborted);
				List<int> accIds = new List<int>();
				await using (NpgsqlCommand cmd = new NpgsqlCommand("\nselect acc_id\nfrom public.van_user_account\nwhere user_id = @uid\norder by acc_id;\n", conn))
				{
					cmd.Parameters.AddWithValue("uid", session.UserId);
					await using NpgsqlDataReader rdr = await cmd.ExecuteReaderAsync(context.RequestAborted);
					while (await rdr.ReadAsync(context.RequestAborted))
					{
						if (!rdr.IsDBNull(0))
						{
							accIds.Add(rdr.GetInt32(0));
						}
					}
				}
				foreach (int item in accIds)
				{
					Account accountById = orchestrator.GetAccountById(item);
					items.Add(new UserAccountListItemDTO
					{
						accId = item,
						accName = (accountById?.accName ?? $"Account {item}")
					});
				}
			}
			catch
			{
			}
			return Results.Json(new UserAccountsResponseDTO
			{
				accounts = items
			});
		});
		app.MapGet("/api/user/account-records", (Func<HttpContext, VANWebServiceConfig, IRecordsRepository, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext context, VANWebServiceConfig cfg, IRecordsRepository recordsRepo, CoreDTO.Logger.Enums.LogAction logAction)
		{
			UserSession session = UserSessionTools.GetUserSession(context);
			if (session == null)
			{
				return Results.Unauthorized();
			}
			if (!string.Equals(session.Role, "User", StringComparison.OrdinalIgnoreCase) && !string.Equals(session.Role, "Investor", StringComparison.OrdinalIgnoreCase))
			{
				return Results.Forbid();
			}
			if (!context.Request.Query.TryGetValue("accId", out var value) || !int.TryParse(value, out var accId))
			{
				return Results.BadRequest(new
				{
					message = "Query parameter 'accId' is required and must be an integer."
				});
			}
			try
			{
				IResult result2;
				await using (NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString))
				{
					await conn.OpenAsync(context.RequestAborted);
					IResult result;
					await using (NpgsqlCommand cmd = new NpgsqlCommand("\nselect 1\nfrom public.van_user_account\nwhere user_id = @uid and acc_id = @aid\nlimit 1;\n", conn))
					{
						cmd.Parameters.AddWithValue("uid", session.UserId);
						cmd.Parameters.AddWithValue("aid", accId);
						if (await cmd.ExecuteScalarAsync(context.RequestAborted) == null)
						{
							logAction($"[GET] /api/user/account-records -> Forbid user={session.UserId} accId={accId}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
							result = Results.Forbid();
							goto IL_0373;
						}
					}
					List<Record> list = await recordsRepo.GetAccountRecordsAsync(accId, context.RequestAborted);
					logAction($"[GET] /api/user/account-records accId={accId} -> {list.Count} records (user {session.UserId})", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
					result2 = Results.Json(new GetAccountRecordsResponseDTO
					{
						accId = accId,
						records = list
					});
					goto end_IL_0163;
					IL_0373:
					result2 = result;
					end_IL_0163:;
				}
				return result2;
			}
			catch (Exception value2)
			{
				logAction($"[GET] /api/user/account-records[ERROR] accId={accId}: {value2}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
				return Results.Problem("Internal server error");
			}
		});
	}
}
