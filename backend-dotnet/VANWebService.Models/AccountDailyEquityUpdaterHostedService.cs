using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VANWebService.Services;

namespace VANWebService.Models;

public sealed class AccountDailyEquityUpdaterHostedService : BackgroundService
{
	private readonly IServiceProvider _services;

	private readonly Enums.LogAction _log;

	private readonly IAccountDailyEquityBackfillQueue _queue;

	private const int HeadRowsToLog = 10;

	private const int TailRowsToLog = 10;

	private const string DebugDumpDir = "/tmp/van_daily_debug";

	// Anomaly guard: if today's total_equity_usd deviates from prior day by more than this ratio,
	// the row is treated as anomalous and deferred.
	private const double AnomalyDropThreshold = 0.30;   // 30% drop triggers guard
	private const double AnomalyJumpThreshold = 1.00;   // 100% jump also suspicious
	private const int RetryDelaySeconds = 60;
	private const int MaxRetryAttempts = 60;

	public AccountDailyEquityUpdaterHostedService(IServiceProvider services, Enums.LogAction log, IAccountDailyEquityBackfillQueue queue)
	{
		_services = services;
		_log = log;
		_queue = queue;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			await EnsureBackfillAsync(stoppingToken);
		}
		catch (Exception value)
		{
			_log($"[AccountDailyEquityUpdater][ERROR] Startup backfill failed: {value}", Enums.LogLevel.llExceptions);
		}
		Task task = RunQueueLoopAsync(stoppingToken);
		Task task2 = RunScheduleLoopAsync(stoppingToken);
		await Task.WhenAll(task, task2);
	}

	private async Task RunScheduleLoopAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			DateTime nextRunUtc = GetNextRunUtc();
			TimeSpan timeSpan = nextRunUtc - DateTime.UtcNow;
			if (timeSpan < TimeSpan.Zero)
			{
				timeSpan = TimeSpan.Zero;
			}
			_log($"[AccountDailyEquityUpdater] Next run at {nextRunUtc:O}", Enums.LogLevel.llBaselogic);
			try
			{
				await Task.Delay(timeSpan, stoppingToken);
			}
			catch (TaskCanceledException)
			{
				break;
			}
			try
			{
				await EnsureBackfillAsync(stoppingToken);
			}
			catch (Exception value)
			{
				_log($"[AccountDailyEquityUpdater][ERROR] Daily backfill failed: {value}", Enums.LogLevel.llExceptions);
			}
		}
	}

	private async Task RunQueueLoopAsync(CancellationToken ct)
	{
		await foreach (int accId in _queue.DequeueAllAsync(ct))
		{
			try
			{
				using IServiceScope scope = _services.CreateScope();
				VANWebServiceConfig cfg = scope.ServiceProvider.GetRequiredService<VANWebServiceConfig>();
				AccountDailyEquityRepository repo = scope.ServiceProvider.GetRequiredService<AccountDailyEquityRepository>();
				AccountDailyEquityRepository.AccountForDailyEquity accountForDailyEquity = await repo.GetAccountWithKeysAsync(accId, ct);
				if (accountForDailyEquity == null)
				{
					_log($"[AccountDailyEquityUpdater][QUEUE] accId={accId} has no keys -> skip", Enums.LogLevel.llBaselogic);
				}
				else
				{
					DateOnly untilDeribitReadyUtc = GetUntilDeribitReadyUtc(DateTime.UtcNow);
					await BackfillAccountAsync(accountForDailyEquity, untilDeribitReadyUtc, cfg, repo, ct);
				}
			}
			catch (Exception value)
			{
				_log($"[AccountDailyEquityUpdater][QUEUE][ERROR] accId={accId}: {value}", Enums.LogLevel.llExceptions);
			}
			finally
			{
				_queue.MarkDone(accId);
			}
		}
	}

	private static DateTime GetNextRunUtc()
	{
		DateTime utcNow = DateTime.UtcNow;
		DateTime dateTime = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 8, 1, 0, DateTimeKind.Utc);
		if (!(utcNow <= dateTime))
		{
			return dateTime.AddDays(1.0);
		}
		return dateTime;
	}

	private static DateOnly GetUntilDeribitReadyUtc(DateTime utcNow)
	{
		DateTime dateTime = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 8, 1, 0, DateTimeKind.Utc);
		if (!(utcNow < dateTime))
		{
			return DateOnly.FromDateTime(utcNow.Date);
		}
		return DateOnly.FromDateTime(utcNow.Date.AddDays(-1.0));
	}

	private static decimal? D(double? x)
	{
		if (!x.HasValue)
		{
			return null;
		}
		return (decimal)x.Value;
	}

	private async Task<List<AccountDailyEquityRepository.DailyRow>> ApplyMinuteUsdcFallbacksAsync(int accId, IReadOnlyCollection<AccountDailyEquityRepository.DailyRow> rows, AccountDailyEquityRepository repo, CancellationToken ct)
	{
		List<AccountDailyEquityRepository.DailyRow> result = new List<AccountDailyEquityRepository.DailyRow>(rows.Count);
		foreach (AccountDailyEquityRepository.DailyRow row in rows)
		{
			result.Add(await ApplyMinuteUsdcFallbackAsync(accId, row, repo, ct));
		}
		return result;
	}

	private async Task<AccountDailyEquityRepository.DailyRow> ApplyMinuteUsdcFallbackAsync(int accId, AccountDailyEquityRepository.DailyRow row, AccountDailyEquityRepository repo, CancellationToken ct)
	{
		if (row.UsdcEquity.HasValue)
		{
			return row;
		}
		DateTime startUtc = DateTime.SpecifyKind(row.DateUtc.ToDateTime(new TimeOnly(8, 1)), DateTimeKind.Utc);
		DateTime endUtc = startUtc.AddMinutes(10.0);
		AccountDailyEquityRepository.MinuteUsdSnapshot minuteUsdSnapshot = await repo.GetMinuteSnapshotAtOrAfterAsync(accId, startUtc, endUtc, ct);
		if (minuteUsdSnapshot == null || !minuteUsdSnapshot.UsdcEquityUsd.HasValue)
		{
			_log($"[AccountDailyEquityUpdater][FALLBACK][USDC][MISS] accId={accId} date={row.DateUtc:yyyy-MM-dd} window=[{startUtc:O}..{endUtc:O}]", Enums.LogLevel.llBaselogic);
			return row;
		}
		decimal usdc = minuteUsdSnapshot.UsdcEquityUsd.Value;
		decimal estTotal = row.EthEquityUsd.GetValueOrDefault() + row.BtcEquityUsd.GetValueOrDefault() + usdc;
		_log($"[AccountDailyEquityUpdater][FALLBACK][USDC] accId={accId} date={row.DateUtc:yyyy-MM-dd} source=van_account_equity ts={minuteUsdSnapshot.TimestampUtc:O} usdc={usdc:F2} est_total={estTotal:F2}", Enums.LogLevel.llBaselogic);
		return row with { UsdcEquity = usdc };
	}

	private static bool IsInvalidCredentials(Exception ex)
	{
		string text = ex.Message ?? "";
		if (!text.Contains("invalid_credentials", StringComparison.OrdinalIgnoreCase) && !text.Contains("\"code\":13004", StringComparison.OrdinalIgnoreCase))
		{
			return text.Contains("13004", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private async Task EnsureBackfillAsync(CancellationToken ct)
	{
		using IServiceScope scope = _services.CreateScope();
		VANWebServiceConfig cfg = scope.ServiceProvider.GetRequiredService<VANWebServiceConfig>();
		AccountDailyEquityRepository repo = scope.ServiceProvider.GetRequiredService<AccountDailyEquityRepository>();
		List<AccountDailyEquityRepository.AccountForDailyEquity> list = (await repo.GetAccountsWithKeysAsync(ct)).ToList();
		if (list.Count == 0)
		{
			_log("[AccountDailyEquityUpdater] No accounts with Deribit keys, skip", Enums.LogLevel.llBaselogic);
			return;
		}
		DateTime utcNow = DateTime.UtcNow;
		DateOnly until = GetUntilDeribitReadyUtc(utcNow);
		_log($"[AccountDailyEquityUpdater] utcNow={utcNow:O} until(deribit_ready)={until:yyyy-MM-dd}", Enums.LogLevel.llBaselogic);
		foreach (AccountDailyEquityRepository.AccountForDailyEquity acc in list)
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				await BackfillAccountAsync(acc, until, cfg, repo, ct);
			}
			catch (Exception value)
			{
				_log($"[AccountDailyEquityUpdater][ERROR] accId={acc.AccId} failed: {value}", Enums.LogLevel.llExceptions);
			}
		}
	}

	// ──────────────────────────────────────────────────────────────────────────
	// Anomaly guard helpers
	// ──────────────────────────────────────────────────────────────────────────

	/// <summary>
	/// Returns true if the candidate row for <paramref name="today"/> looks
	/// anomalous (missing required USDC component or implausible total drop).
	/// Only applied to the newest/today row to avoid touching historical data.
	/// </summary>
	private bool IsTodayRowAnomalous(
		DateOnly today,
		DateOnly until,
		AccountDailyEquityRepository.AccountForDailyEquity acc,
		AccountDailyEquityRepository.DailyRow candidateRow,
		AccountDailyEquityRepository.DailyRow? priorRow,
		out string anomalyReason)
	{
		anomalyReason = string.Empty;
		// Only guard the freshest day (== until).
		if (candidateRow.DateUtc != until)
			return false;

		// Rule 1: if account includes USDC in total and usdc_equity is null → anomalous.
		if (acc.IncUsdc && candidateRow.UsdcEquity == null)
		{
			_log($"[AccountDailyEquityUpdater][ANOMALY] accId={acc.AccId} date={today:yyyy-MM-dd} USDC missing (null) while include_usdc_in_total=true -> defer", Enums.LogLevel.llBaselogic);
			anomalyReason = $"USDC null while include_usdc_in_total=true (accId={acc.AccId} date={today:yyyy-MM-dd})";
			return true;
		}

		// Rule 2: compare total vs prior day total for implausible drop/jump.
		if (priorRow != null && priorRow.UsdcEquity != null &&
		    candidateRow.UsdcEquity == null && acc.IncUsdc)
		{
			// Caught above, but belt-and-suspenders.
			_log($"[AccountDailyEquityUpdater][ANOMALY] accId={acc.AccId} date={today:yyyy-MM-dd} prior had USDC but today USDC null -> defer", Enums.LogLevel.llBaselogic);
			anomalyReason = $"Prior had USDC but today USDC null (accId={acc.AccId} date={today:yyyy-MM-dd})";
			return true;
		}

		// Rule 3: TotalEquityUsd drop > threshold vs prior accepted total.
		if (priorRow != null)
		{
			// Compute candidate total (same logic as DB trigger: sum of enabled components).
			double candTotal = 0.0;
			if (acc.IncUsdc)  candTotal += (double)(candidateRow.UsdcEquity ?? 0m);
			if (acc.IncEth)   candTotal += (double)(candidateRow.EthEquityUsd ?? 0m);
			if (acc.IncBtc)   candTotal += (double)(candidateRow.BtcEquityUsd ?? 0m);

			double priorTotal = 0.0;
			if (acc.IncUsdc)  priorTotal += (double)(priorRow.UsdcEquity ?? 0m);
			if (acc.IncEth)   priorTotal += (double)(priorRow.EthEquityUsd ?? 0m);
			if (acc.IncBtc)   priorTotal += (double)(priorRow.BtcEquityUsd ?? 0m);

			if (priorTotal > 0.0 && candTotal > 0.0)
			{
				double changeRatio = (priorTotal - candTotal) / priorTotal;
				if (changeRatio > AnomalyDropThreshold)
				{
					_log($"[AccountDailyEquityUpdater][ANOMALY] accId={acc.AccId} date={today:yyyy-MM-dd} total dropped {changeRatio:P1} ({priorTotal:F2} -> {candTotal:F2}) > threshold {AnomalyDropThreshold:P0} -> defer", Enums.LogLevel.llBaselogic);
					anomalyReason = $"Total dropped {changeRatio:P1} ({priorTotal:F2} -> {candTotal:F2}) > threshold {AnomalyDropThreshold:P0} (accId={acc.AccId} date={today:yyyy-MM-dd})";
					return true;
				}
				if (candTotal > priorTotal * (1.0 + AnomalyJumpThreshold))
				{
					_log($"[AccountDailyEquityUpdater][ANOMALY] accId={acc.AccId} date={today:yyyy-MM-dd} total jumped {candTotal/priorTotal:P1} -> defer", Enums.LogLevel.llBaselogic);
					anomalyReason = $"Total jumped {candTotal/priorTotal:P1} > 100% threshold (accId={acc.AccId} date={today:yyyy-MM-dd})";
					return true;
				}
			}
		}

		return false;
	}

	// ──────────────────────────────────────────────────────────────────────────

	private async Task BackfillAccountAsync(AccountDailyEquityRepository.AccountForDailyEquity acc, DateOnly until, VANWebServiceConfig cfg, AccountDailyEquityRepository repo, CancellationToken ct)
	{
		DateOnly? value = await repo.GetMaxDateAsync(acc.AccId, ct);
		DateOnly dateOnly = (value.HasValue ? value.Value.AddDays(1) : acc.StartDateUtc);
		_log($"[AccountDailyEquityUpdater] accId={acc.AccId} last={value} from={dateOnly} until={until}", Enums.LogLevel.llBaselogic);
		if (dateOnly > until)
		{
			return;
		}
		DateOnly chunkStart = dateOnly;
		while (chunkStart <= until)
		{
			ct.ThrowIfCancellationRequested();
			DateOnly chunkEnd = chunkStart.AddDays(364);
			if (chunkEnd > until)
			{
				chunkEnd = until;
			}
			_log($"[AccountDailyEquityUpdater] accId={acc.AccId} fetch {chunkStart:yyyy-MM-dd}..{chunkEnd:yyyy-MM-dd}", Enums.LogLevel.llBaselogic);
			DeribitSettlementExporter deribitSettlementExporter = new DeribitSettlementExporter(cfg.DeribitBaseUrl, acc.ApiPublicKey, acc.ApiSecretPlain, chunkStart, chunkEnd, "/tmp/van_account_daily_equity.csv", _log);
			List<DeribitSettlementExporter.DailyAccountEquityPoint> series;
			try
			{
				series = await deribitSettlementExporter.GetDailyAccountEquitySeriesAsync();
			}
			catch (HttpRequestException ex) when (IsInvalidCredentials(ex))
			{
				_log($"[AccountDailyEquityUpdater][WARN] accId={acc.AccId} Deribit invalid_credentials -> skip account", Enums.LogLevel.llBaselogic);
				break;
			}
			catch (HttpRequestException value2)
			{
				_log($"[AccountDailyEquityUpdater][WARN] accId={acc.AccId} Deribit HTTP error -> skip account. {value2}", Enums.LogLevel.llBaselogic);
				break;
			}
			LogDeribitDailySeriesSummary(acc.AccId, chunkStart, chunkEnd, series);
			LogDeribitDailySeriesSample(acc.AccId, series, 10, 10);
			try
			{
				await DumpDeribitDailySeriesToCsvAsync(acc.AccId, chunkStart, chunkEnd, series, ct);
			}
			catch (Exception ex2)
			{
				_log($"[AccountDailyEquityUpdater][DERIBIT][DUMP][ERROR] accId={acc.AccId}: {ex2.Message}", Enums.LogLevel.llBaselogic);
			}
			List<AccountDailyEquityRepository.DailyRow> rows = series.Select((DeribitSettlementExporter.DailyAccountEquityPoint p) => new AccountDailyEquityRepository.DailyRow(p.DateUtc, D(p.UsdcEquity), D(p.EthEquity), D(p.BtcEquity), D(p.EthPrice), D(p.BtcPrice), D(p.EthEquityUsd), D(p.BtcEquityUsd))).ToList();
			rows = await ApplyMinuteUsdcFallbacksAsync(acc.AccId, rows, repo, ct);
			Dictionary<DateOnly, decimal> intradayDrawdownByDate = await repo.GetIntradayDrawdownRangeAsync(acc.AccId, chunkStart, chunkEnd, ct);
			rows = rows.Select(r => r with { DayMaxDrawdownPct = (intradayDrawdownByDate.TryGetValue(r.DateUtc, out decimal dd) ? dd : null) }).ToList();
			_log($"[AccountDailyEquityUpdater] accId={acc.AccId} chunk -> {rows.Count} days", Enums.LogLevel.llBaselogic);
			if (rows.Count > 0)
			{
				// ── ANOMALY GUARD ──────────────────────────────────────────────────────
				// For the newest row (date == until), check if it looks wrong.
				// If anomalous, split: write all historical rows immediately, then
				// retry the newest row up to MaxRetryAttempts with RetryDelaySeconds delay.
				var newestRow = rows.FirstOrDefault(r => r.DateUtc == until);
				var historicalRows = rows.Where(r => r.DateUtc != until).ToList();

				// Write non-today rows unconditionally (safe: historical data).
				if (historicalRows.Count > 0)
				{
					await repo.UpsertRangeAsync(acc.AccId, historicalRows, acc.IncUsdc, acc.IncEth, acc.IncBtc, ct);
				}

				if (newestRow != null)
				{
					// Fetch second-most-recent accepted row from DB to use as baseline.
					AccountDailyEquityRepository.DailyRow? priorRow = await repo.GetPriorDayRowAsync(acc.AccId, until, ct);

					bool accepted = false;
					for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
					{
						ct.ThrowIfCancellationRequested();

						bool anomalous = IsTodayRowAnomalous(until, until, acc, newestRow, priorRow, out string anomalyReason);
					if (!anomalous)
						{
							_log($"[AccountDailyEquityUpdater][GUARD] accId={acc.AccId} date={until:yyyy-MM-dd} row accepted on attempt {attempt}", Enums.LogLevel.llBaselogic);
							await repo.UpsertRangeAsync(acc.AccId, new[] { newestRow }, acc.IncUsdc, acc.IncEth, acc.IncBtc, ct);
							accepted = true;
							break;
						}

						if (attempt < MaxRetryAttempts)
						{
							_log($"[AccountDailyEquityUpdater][GUARD] accId={acc.AccId} date={until:yyyy-MM-dd} anomalous on attempt {attempt}/{MaxRetryAttempts}, retry in {RetryDelaySeconds}s", Enums.LogLevel.llBaselogic);
							// Send Telegram/email alert on first detection only.
							if (attempt == 1)
							{
								using var alertScope = _services.CreateScope();
								var notifSvc = alertScope.ServiceProvider.GetService<NotificationService>();
								if (notifSvc != null)
									await notifSvc.NotifyAnomalyGuardRetryAsync(acc.AccId, until, attempt, MaxRetryAttempts, anomalyReason, ct);
							}
							try
							{
								await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), ct);
							}
							catch (TaskCanceledException)
							{
								_log($"[AccountDailyEquityUpdater][GUARD] accId={acc.AccId} retry cancelled", Enums.LogLevel.llBaselogic);
								return;
							}

							// Re-fetch from Deribit for today only.
							DeribitSettlementExporter retryExporter = new DeribitSettlementExporter(
								cfg.DeribitBaseUrl, acc.ApiPublicKey, acc.ApiSecretPlain,
								until, until,
								"/tmp/van_account_daily_equity.csv", _log);

							List<DeribitSettlementExporter.DailyAccountEquityPoint> retrySeries;
							try
							{
								retrySeries = await retryExporter.GetDailyAccountEquitySeriesAsync();
							}
							catch (HttpRequestException ex3) when (IsInvalidCredentials(ex3))
							{
								_log($"[AccountDailyEquityUpdater][GUARD][WARN] accId={acc.AccId} Deribit invalid_credentials on retry -> stop", Enums.LogLevel.llBaselogic);
								return;
							}
							catch (HttpRequestException ex4)
							{
								_log($"[AccountDailyEquityUpdater][GUARD][WARN] accId={acc.AccId} Deribit HTTP error on retry -> stop. {ex4}", Enums.LogLevel.llBaselogic);
								return;
							}

							var retryNewest = retrySeries.FirstOrDefault(p => p.DateUtc == until);
							if (retryNewest != null)
							{
								newestRow = new AccountDailyEquityRepository.DailyRow(
									retryNewest.DateUtc,
									D(retryNewest.UsdcEquity), D(retryNewest.EthEquity), D(retryNewest.BtcEquity),
									D(retryNewest.EthPrice), D(retryNewest.BtcPrice),
									D(retryNewest.EthEquityUsd), D(retryNewest.BtcEquityUsd));
								newestRow = await ApplyMinuteUsdcFallbackAsync(acc.AccId, newestRow, repo, ct);
								Dictionary<DateOnly, decimal> retryDrawdownByDate = await repo.GetIntradayDrawdownRangeAsync(acc.AccId, until, until, ct);
								newestRow = newestRow with { DayMaxDrawdownPct = (retryDrawdownByDate.TryGetValue(until, out decimal dd2) ? dd2 : null) };
							}
							else
							{
								_log($"[AccountDailyEquityUpdater][GUARD] accId={acc.AccId} retry returned no row for {until:yyyy-MM-dd}", Enums.LogLevel.llBaselogic);
							}
						}
						else
						{
							// ── Repair-cron recovery check ──────────────────────────────────────
							// Before sending CRITICAL alert, check if repair cron (08:03 UTC) already
							// wrote today's row via a different path (e.g., minute-snapshot repair).
							// If valid row now exists in DB → suppress CRITICAL, log RECOVERED.
							AccountDailyEquityRepository.DailyRow repairedRow = await repo.GetRowForDateAsync(acc.AccId, until, ct);
							bool repairedByOther = repairedRow != null &&
								(!acc.IncUsdc || repairedRow.UsdcEquity != null);
							if (repairedByOther)
							{
								_log($"[AccountDailyEquityUpdater][GUARD][RECOVERED] accId={acc.AccId} date={until:yyyy-MM-dd} row already written by repair cron (usdc={repairedRow.UsdcEquity} eth_usd={repairedRow.EthEquityUsd} btc_usd={repairedRow.BtcEquityUsd}) — CRITICAL alert suppressed.", Enums.LogLevel.llBaselogic);
								accepted = true;
							}
							else
							{
								_log($"[AccountDailyEquityUpdater][GUARD][WARN] accId={acc.AccId} date={until:yyyy-MM-dd} still anomalous after {MaxRetryAttempts} attempts -> NOT writing today's row. Will retry on next scheduled run.", Enums.LogLevel.llBaselogic);
							{
								using var alertScope = _services.CreateScope();
								var notifSvc = alertScope.ServiceProvider.GetService<NotificationService>();
								if (notifSvc != null)
									await notifSvc.NotifyAnomalyGuardExhaustedAsync(acc.AccId, until, MaxRetryAttempts, anomalyReason, ct);
							}
							}
							// ── End repair-cron recovery check ──────────────────────────────
						}
					}

					if (!accepted)
					{
						_log($"[AccountDailyEquityUpdater][GUARD][SKIP] accId={acc.AccId} date={until:yyyy-MM-dd} deferred (not written)", Enums.LogLevel.llBaselogic);
					}
				}
				// ── END ANOMALY GUARD ──────────────────────────────────────────────────
			}
			await Task.Delay(TimeSpan.FromSeconds(1.0), ct);
			chunkStart = chunkEnd.AddDays(1);
		}
	}

	private void LogDeribitDailySeriesSummary(int accId, DateOnly from, DateOnly to, List<DeribitSettlementExporter.DailyAccountEquityPoint> series)
	{
		_log($"[AccountDailyEquityUpdater][DERIBIT] accId={accId} range={from:yyyy-MM-dd}..{to:yyyy-MM-dd} points={series.Count}", Enums.LogLevel.llBaselogic);
		int value = series.Count((DeribitSettlementExporter.DailyAccountEquityPoint p) => !p.EthEquity.HasValue);
		int value2 = series.Count((DeribitSettlementExporter.DailyAccountEquityPoint p) => !p.BtcEquity.HasValue);
		int value3 = series.Count((DeribitSettlementExporter.DailyAccountEquityPoint p) => !p.UsdcEquity.HasValue);
		int value4 = series.Count((DeribitSettlementExporter.DailyAccountEquityPoint p) => !p.EthPrice.HasValue);
		int value5 = series.Count((DeribitSettlementExporter.DailyAccountEquityPoint p) => !p.BtcPrice.HasValue);
		int value6 = series.Count((DeribitSettlementExporter.DailyAccountEquityPoint p) => !p.EthEquityUsd.HasValue);
		int value7 = series.Count((DeribitSettlementExporter.DailyAccountEquityPoint p) => !p.BtcEquityUsd.HasValue);
		_log($"[AccountDailyEquityUpdater][DERIBIT][NULLS] accId={accId} usdc_eq={value3}, eth_eq={value}, btc_eq={value2}, eth_px={value4}, btc_px={value5}, eth_usd={value6}, btc_usd={value7}", Enums.LogLevel.llBaselogic);
	}

	private void LogDeribitDailySeriesSample(int accId, List<DeribitSettlementExporter.DailyAccountEquityPoint> series, int head, int tail)
	{
		if (series.Count == 0)
		{
			return;
		}
		List<DeribitSettlementExporter.DailyAccountEquityPoint> list = series.OrderBy((DeribitSettlementExporter.DailyAccountEquityPoint p) => p.DateUtc).ToList();
		foreach (DeribitSettlementExporter.DailyAccountEquityPoint item in list.Take(Math.Max(0, head)))
		{
			LogRow("HEAD", item);
		}
		if (list.Count <= head)
		{
			return;
		}
		foreach (DeribitSettlementExporter.DailyAccountEquityPoint item2 in list.Skip(Math.Max(head, list.Count - Math.Max(0, tail))))
		{
			LogRow("TAIL", item2);
		}
		static string F(double? x)
		{
			if (!x.HasValue)
			{
				return "NULL";
			}
			return x.Value.ToString("0.##########", CultureInfo.InvariantCulture);
		}
		void LogRow(string tag, DeribitSettlementExporter.DailyAccountEquityPoint p)
		{
			_log($"[AccountDailyEquityUpdater][DERIBIT][{tag}] accId={accId} date={p.DateUtc:yyyy-MM-dd} usdc={F(p.UsdcEquity)} eth={F(p.EthEquity)} btc={F(p.BtcEquity)} eth_px={F(p.EthPrice)} btc_px={F(p.BtcPrice)} eth_usd={F(p.EthEquityUsd)} btc_usd={F(p.BtcEquityUsd)}", Enums.LogLevel.llBaselogic);
		}
	}

	private async Task DumpDeribitDailySeriesToCsvAsync(int accId, DateOnly from, DateOnly to, List<DeribitSettlementExporter.DailyAccountEquityPoint> series, CancellationToken ct)
	{
		Directory.CreateDirectory("/tmp/van_daily_debug");
		string value = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
		string file = $"{"/tmp/van_daily_debug"}/daily_acc{accId}_{from:yyyyMMdd}_{to:yyyyMMdd}_{value}.csv";
		await using (FileStream fs = File.Create(file))
		{
			await using StreamWriter sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			await sw.WriteLineAsync("date_utc;usdc_equity;eth_equity;btc_equity;eth_price;btc_price;eth_equity_usd;btc_equity_usd");
			foreach (DeribitSettlementExporter.DailyAccountEquityPoint item in series.OrderBy((DeribitSettlementExporter.DailyAccountEquityPoint p) => p.DateUtc))
			{
				ct.ThrowIfCancellationRequested();
				await sw.WriteLineAsync(string.Join(";", item.DateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), F(item.UsdcEquity), F(item.EthEquity), F(item.BtcEquity), F(item.EthPrice), F(item.BtcPrice), F(item.EthEquityUsd), F(item.BtcEquityUsd)));
			}
			_log($"[AccountDailyEquityUpdater][DERIBIT][DUMP] accId={accId} -> {file}", Enums.LogLevel.llBaselogic);
		}
		static string F(double? x)
		{
			if (!x.HasValue)
			{
				return "";
			}
			return x.Value.ToString("0.##########", CultureInfo.InvariantCulture);
		}
	}
}
