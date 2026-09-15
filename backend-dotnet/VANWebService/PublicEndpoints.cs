using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using CoreDTO.VANDTO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using MimeKit;
using Npgsql;
using VANWebService.Models;
using VANWebService.Services;

namespace VANWebService;

public static class PublicEndpoints
{
	public static void MapPublicEndpoints(this WebApplication app)
	{
		app.MapGet("/api/public/fund-equity-curve", (Func<FundEquityRepository, CancellationToken, Task<IResult>>)async delegate(FundEquityRepository repo, CancellationToken ct)
		{
			List<FundEquityPointDTO> points = await repo.GetAllAsync(ct);
			return Results.Json(new FundEquityCurveResponseDTO
			{
				points = points
			});
		});
		app.MapGet("/public/fund-equity-curve", (Func<FundEquityRepository, CancellationToken, Task<IResult>>)async delegate(FundEquityRepository repo, CancellationToken ct)
		{
			List<FundEquityPointDTO> points = await repo.GetAllAsync(ct);
			return Results.Json(new FundEquityCurveResponseDTO
			{
				points = points
			});
		});
		app.MapGet("/api/public/equity-chart", (Func<VANWebServiceConfig, CancellationToken, Task<IResult>>)async delegate(VANWebServiceConfig cfg, CancellationToken ct)
		{
			List<object> points = new List<object>();
			List<MinuteEquityPoint> minuteSeries = new List<MinuteEquityPoint>();
			try
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync(ct);
				await using NpgsqlCommand cmd = new NpgsqlCommand("\nselect\n    date_trunc('minute', ae.timestamp_utc) as ts_min,\n    sum(\n        coalesce(ae.equity_usdc_usd, 0) +\n        coalesce(ae.equity_btc_usd,  0) +\n        coalesce(ae.equity_eth_usd,  0)\n    ) as equity_total_usd\nfrom public.van_account_equity ae\nleft join public.van_outage_intervals oi\n    on ae.timestamp_utc >= oi.started_at and ae.timestamp_utc < oi.ended_at\nwhere ae.acc_id = 8\n  and ae.timestamp_utc >= (now() at time zone 'utc' - interval '24 hours')\n  and oi.id is null\ngroup by date_trunc('minute', ae.timestamp_utc)\norder by ts_min;\n", conn);
				await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct);
				while (await r.ReadAsync(ct))
				{
					DateTime dt = r.GetDateTime(0);
					double value = (r.IsDBNull(1) ? 0.0 : Convert.ToDouble(r.GetValue(1)));
					minuteSeries.Add(new MinuteEquityPoint(dt, value));
					points.Add(new
					{
						dt,
						v = value
					});
				}
			}
			catch
			{
			}
			List<object> drawdownPoints = BuildMinuteDrawdownSeriesWith30MinuteFilter(minuteSeries).Select(p => (object)new
			{
				dt = p.MinuteUtc,
				maxDrawdownPct = p.DrawdownPct
			}).ToList();
			return Results.Json(new { points, drawdownPoints });
		});
		app.MapGet("/api/public/daily-equity-chart", (Func<VANWebServiceConfig, CancellationToken, Task<IResult>>)async delegate(VANWebServiceConfig cfg, CancellationToken ct)
		{
			List<DailyEquityPoint> dailySeries = new List<DailyEquityPoint>();
			List<MinuteEquityPoint> minuteEquity = new List<MinuteEquityPoint>();
			double? initialInvestmentUsd = null;
			try
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync(ct);
				await using (NpgsqlCommand cmdInit = new NpgsqlCommand("select initial_investment_usd from public.van_account where acc_id = 8;", conn))
				{
					object obj = await cmdInit.ExecuteScalarAsync(ct);
					if (obj != null && !(obj is DBNull))
					{
						initialInvestmentUsd = Convert.ToDouble(obj);
					}
				}
				await using (NpgsqlCommand cmd = new NpgsqlCommand(@"
select
    date_utc,
    total_equity_usd
from public.van_account_daily_equity
where acc_id = 8
order by date_utc;
", conn))
				await using (NpgsqlDataReader r = await cmd.ExecuteReaderAsync(ct))
				{
					while (await r.ReadAsync(ct))
					{
						DateOnly dateUtc = r.GetFieldValue<DateOnly>(0);
						double value = (r.IsDBNull(1) ? 0.0 : Convert.ToDouble(r.GetValue(1)));
						dailySeries.Add(new DailyEquityPoint(dateUtc, value));
					}
				}
				await using NpgsqlCommand cmdMin = new NpgsqlCommand(@"
select
    ae.minute_utc,
    ae.equity_total_usd
from public.van_account_equity ae
where ae.acc_id = 8
  and ae.equity_total_usd is not null
order by ae.minute_utc;
", conn);
				await using NpgsqlDataReader rMin = await cmdMin.ExecuteReaderAsync(ct);
				while (await rMin.ReadAsync(ct))
				{
					if (!rMin.IsDBNull(0) && !rMin.IsDBNull(1))
					{
						DateTime minuteUtc = rMin.GetFieldValue<DateTime>(0);
						double equityUsd = Convert.ToDouble(rMin.GetValue(1));
						minuteEquity.Add(new MinuteEquityPoint(minuteUtc, equityUsd));
					}
				}
			}
			catch
			{
			}
			dailySeries = MergeDailyWithLatestMinuteDay(dailySeries, minuteEquity);
			List<object> points = dailySeries.Select(p => (object)new
			{
				dt = p.DateUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
				v = p.Value
			}).ToList();
			Dictionary<DateOnly, double> drawdownMap = BuildDailyDrawdownSeriesWithMa60Hwm(dailySeries, minuteEquity);
			List<object> drawdownPoints = dailySeries.Select(p => (object)new
			{
				dt = p.DateUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
				maxDrawdownPct = drawdownMap.TryGetValue(p.DateUtc, out double value) ? value : 0.0
			}).ToList();
			return Results.Json(new { points, drawdownPoints, initialInvestmentUsd });
		});
		app.MapGet("/api/public/max-drawdown", (Func<VANWebServiceConfig, CancellationToken, Task<IResult>>)async delegate(VANWebServiceConfig cfg, CancellationToken ct)
		{
			double? maxDrawdownPct = null;
			try
			{
				await using NpgsqlConnection conn = new NpgsqlConnection(cfg.AuthorityConnectionString);
				await conn.OpenAsync(ct);
				double? initialInvestmentUsd = null;
				List<DailyEquityPoint> dailySeries = new List<DailyEquityPoint>();
				List<MinuteEquityPoint> minuteEquity = new List<MinuteEquityPoint>();
				await using (NpgsqlCommand cmdInit = new NpgsqlCommand("select initial_investment_usd from public.van_account where acc_id = 8;", conn))
				{
					object obj = await cmdInit.ExecuteScalarAsync(ct);
					if (obj != null && !(obj is DBNull))
					{
						initialInvestmentUsd = Convert.ToDouble(obj);
					}
				}
				await using (NpgsqlCommand cmdDaily = new NpgsqlCommand(@"
select
    date_utc,
    total_equity_usd
from public.van_account_daily_equity
where acc_id = 8
order by date_utc;
", conn))
				await using (NpgsqlDataReader rDaily = await cmdDaily.ExecuteReaderAsync(ct))
				{
					while (await rDaily.ReadAsync(ct))
					{
						dailySeries.Add(new DailyEquityPoint(rDaily.GetFieldValue<DateOnly>(0), rDaily.IsDBNull(1) ? 0.0 : Convert.ToDouble(rDaily.GetValue(1))));
					}
				}
				await using NpgsqlCommand cmdMin = new NpgsqlCommand(@"
select
    ae.minute_utc,
    ae.equity_total_usd
from public.van_account_equity ae
where ae.acc_id = 8
  and ae.equity_total_usd is not null
order by ae.minute_utc;
", conn);
				await using NpgsqlDataReader rMin = await cmdMin.ExecuteReaderAsync(ct);
				while (await rMin.ReadAsync(ct))
				{
					if (!rMin.IsDBNull(0) && !rMin.IsDBNull(1))
					{
						DateTime minuteUtc = rMin.GetFieldValue<DateTime>(0);
						double equityUsd = Convert.ToDouble(rMin.GetValue(1));
						minuteEquity.Add(new MinuteEquityPoint(minuteUtc, equityUsd));
					}
				}
				Dictionary<DateOnly, double> drawdownMap = BuildDailyDrawdownSeriesWithMa60Hwm(dailySeries, minuteEquity);
				if (drawdownMap.Count > 0)
				{
					maxDrawdownPct = drawdownMap.Values.Max();
				}
			}
			catch
			{
			}
			return Results.Json(new { maxDrawdownPct });
		});
		app.MapPost("/api/public/register", (Func<PublicRegistrationRequestDTO, AuthorityRepository, NotificationService, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(PublicRegistrationRequestDTO dto, AuthorityRepository authRepo, NotificationService notifier, CoreDTO.Logger.Enums.LogAction logAction)
		{
			logAction($"[REG] New registration (via /api/public/register): {dto.fullName}, email={dto.email}, tg={dto.telegram}, country={dto.country}, city={dto.city}, planned={dto.plannedInvestmentUsd}, comment={dto.comment}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			try
			{
				await authRepo.SavePublicRegistrationAsync(dto);
			}
			catch (Exception value)
			{
				logAction($"[REG][ERROR] Failed to save registration: {value}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			await notifier.NotifyRegistrationAsync(dto);
			return Results.Json(new PublicRegistrationResponseDTO
			{
				ok = true
			});
		});
		app.MapPost("/api/public/contact-us", (Func<HttpContext, PublicContactUsRequestDTO, AuthorityRepository, NotificationService, CoreDTO.Logger.Enums.LogAction, Task<IResult>>)async delegate(HttpContext ctx, PublicContactUsRequestDTO dto, AuthorityRepository authRepo, NotificationService notifier, CoreDTO.Logger.Enums.LogAction logAction)
		{
			if (dto == null)
			{
				return Results.BadRequest(new
				{
					message = "Request body is required"
				});
			}
			dto.name = (dto.name ?? string.Empty).Trim();
			dto.email = (dto.email ?? string.Empty).Trim();
			dto.message = (dto.message ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(dto.name) || string.IsNullOrWhiteSpace(dto.email) || string.IsNullOrWhiteSpace(dto.message))
			{
				return Results.BadRequest(new
				{
					message = "name, email, message are required"
				});
			}
			if (!IsValidEmail(dto.email))
			{
				return Results.BadRequest(new
				{
					message = "Please enter a valid email address"
				});
			}
			string ip = TryGetClientIp(ctx);
			string ua = TryGetUserAgent(ctx);
			logAction($"[CONTACT] New message (via /api/public/contact-us): name={dto.name}, email={dto.email}, ip={ip}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			try
			{
				await authRepo.SavePublicContactUsAsync(dto, ip, ua, ctx.RequestAborted);
			}
			catch (Exception value)
			{
				logAction($"[CONTACT][DB][ERROR] Failed to save message: {value}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			try
			{
				await notifier.NotifyContactUsAsync(dto, ip, ua, ctx.RequestAborted);
			}
			catch (Exception value2)
			{
				logAction($"[CONTACT][NOTIFY][ERROR] {value2}", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			}
			return Results.Json(new PublicContactUsResponseDTO
			{
				ok = true
			});
		});
	}


	private sealed record DailyEquityPoint(DateOnly DateUtc, double Value);

	private sealed record MinuteEquityPoint(DateTime MinuteUtc, double Value);

	private sealed record MinuteDrawdownPoint(DateTime MinuteUtc, double DrawdownPct);

	private static List<DailyEquityPoint> MergeDailyWithLatestMinuteDay(List<DailyEquityPoint> dailySeries, List<MinuteEquityPoint> minuteSeries)
	{
		SortedDictionary<DateOnly, DailyEquityPoint> byDay = new SortedDictionary<DateOnly, DailyEquityPoint>();
		foreach (DailyEquityPoint point in dailySeries.OrderBy(p => p.DateUtc))
		{
			byDay[point.DateUtc] = point;
		}
		MinuteEquityPoint? latestMinute = minuteSeries.OrderBy(p => p.MinuteUtc).LastOrDefault();
		if (latestMinute != null)
		{
			DateOnly day = DateOnly.FromDateTime(latestMinute.MinuteUtc);
			byDay[day] = new DailyEquityPoint(day, latestMinute.Value);
		}
		return byDay.Values.ToList();
	}

	private static List<MinuteDrawdownPoint> BuildMinuteDrawdownSeriesWith30MinuteFilter(List<MinuteEquityPoint> minuteSeries)
	{
		List<MinuteDrawdownPoint> result = new List<MinuteDrawdownPoint>();
		if (minuteSeries.Count == 0)
		{
			return result;
		}
		double acceptedHwm = minuteSeries[0].Value;
		double? candidatePeak = null;
		int candidateCount = 0;
		foreach (MinuteEquityPoint point in minuteSeries)
		{
			if (point.Value > acceptedHwm)
			{
				candidatePeak = candidatePeak.HasValue ? Math.Max(candidatePeak.Value, point.Value) : point.Value;
				candidateCount++;
				if (candidateCount > 30 && candidatePeak.HasValue)
				{
					acceptedHwm = candidatePeak.Value;
					candidatePeak = null;
					candidateCount = 0;
				}
			}
			else
			{
				candidatePeak = null;
				candidateCount = 0;
			}
			double dd = acceptedHwm > 0.0 ? ((acceptedHwm - point.Value) / acceptedHwm * 100.0) : 0.0;
			if (dd < 0.0)
			{
				dd = 0.0;
			}
			result.Add(new MinuteDrawdownPoint(point.MinuteUtc, Math.Round(dd, 4)));
		}
		return result;
	}

	private static Dictionary<DateOnly, double> BuildDailyDrawdownSeriesWithMa60Hwm(List<DailyEquityPoint> dailySeries, List<MinuteEquityPoint> minuteSeries)
	{
		Dictionary<DateOnly, double> result = BuildDailyDrawdownFromDailyEquity(dailySeries);
		if (minuteSeries.Count == 0)
		{
			return result;
		}

		Queue<double> window = new Queue<double>();
		double windowSum = 0.0;
		double? hwm60m = null;
		DateOnly? currentDay = null;
		double currentDayMax = 0.0;
		bool currentDayHasMa60 = false;
		foreach (MinuteEquityPoint point in minuteSeries.OrderBy(p => p.MinuteUtc))
		{
			window.Enqueue(point.Value);
			windowSum += point.Value;
			while (window.Count > 60)
			{
				windowSum -= window.Dequeue();
			}

			double equityMa60m = windowSum / window.Count;
			hwm60m = hwm60m.HasValue && hwm60m.Value > equityMa60m ? hwm60m.Value : equityMa60m;
			double drawdown = hwm60m.Value <= 0.0 ? 0.0 : (hwm60m.Value - point.Value) / hwm60m.Value * 100.0;
			if (drawdown < 0.0)
			{
				drawdown = 0.0;
			}

			DateOnly day = DateOnly.FromDateTime(point.MinuteUtc);
			if (currentDay != day)
			{
				if (currentDay.HasValue && currentDayHasMa60)
				{
					result[currentDay.Value] = Math.Round(currentDayMax, 4);
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
			result[currentDay.Value] = Math.Round(currentDayMax, 4);
		}

		return result;
	}

	private static Dictionary<DateOnly, double> BuildDailyDrawdownFromDailyEquity(List<DailyEquityPoint> dailySeries)
	{
		Dictionary<DateOnly, double> result = new Dictionary<DateOnly, double>();
		double peak = 0.0;
		foreach (DailyEquityPoint point in dailySeries)
		{
			if (point.Value > peak)
			{
				peak = point.Value;
			}
			double dd = (peak > 0.0) ? ((peak - point.Value) / peak * 100.0) : 0.0;
			result[point.DateUtc] = dd;
		}
		return result;
	}

	private static bool IsValidEmail(string? email)
	{
		if (string.IsNullOrWhiteSpace(email))
		{
			return false;
		}
		return MailboxAddress.TryParse(email.Trim(), out var mailbox) && !string.IsNullOrWhiteSpace(mailbox.Address) && mailbox.Address.Contains("@");
	}

	private static string? TryGetClientIp(HttpContext ctx)
	{
		if (ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var value))
		{
			string text = (from s in value.ToString().Split(',')
				select s.Trim()).FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		return ctx.Connection.RemoteIpAddress?.ToString();
	}

	private static string? TryGetUserAgent(HttpContext ctx)
	{
		if (ctx.Request.Headers.TryGetValue("User-Agent", out var value))
		{
			return value.ToString();
		}
		return null;
	}
}
