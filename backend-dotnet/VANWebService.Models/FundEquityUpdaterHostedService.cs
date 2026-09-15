using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VANWebService.Models;

public sealed class FundEquityUpdaterHostedService : BackgroundService
{
	private readonly IServiceProvider _services;

	private readonly Enums.LogAction logAction;

	private static readonly DateOnly FirstTradeDate = new DateOnly(2021, 10, 19);

	public FundEquityUpdaterHostedService(IServiceProvider services, Enums.LogAction log)
	{
		_services = services;
		logAction = log;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await EnsureBackfillAsync(stoppingToken);
		while (!stoppingToken.IsCancellationRequested)
		{
			DateTime nextRunUtc = GetNextRunUtc();
			TimeSpan timeSpan = nextRunUtc - DateTime.UtcNow;
			if (timeSpan < TimeSpan.Zero)
			{
				timeSpan = TimeSpan.Zero;
			}
			logAction($"[FundEquityUpdater] Next run at {nextRunUtc:O}", Enums.LogLevel.llBaselogic);
			try
			{
				await Task.Delay(timeSpan, stoppingToken);
			}
			catch (TaskCanceledException)
			{
				break;
			}
			await EnsureBackfillAsync(stoppingToken);
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

	private async Task EnsureBackfillAsync(CancellationToken ct)
	{
		using IServiceScope scope = _services.CreateScope();
		FundEquityRepository repo = scope.ServiceProvider.GetRequiredService<FundEquityRepository>();
		AuthorityRepository requiredService = scope.ServiceProvider.GetRequiredService<AuthorityRepository>();
		VANWebServiceConfig cfg = scope.ServiceProvider.GetRequiredService<VANWebServiceConfig>();
		AuthorityRepository.DeribitApiKeyRecord keys = await requiredService.GetReferenceSelfCustodyKeysAsync();
		if (keys == null)
		{
			logAction("[FundEquityUpdater] No Self_Custody API keys in fund_settings, skip", Enums.LogLevel.llBaselogic);
			return;
		}
		DateOnly? value = await repo.GetMaxDateAsync();
		DateOnly dateOnly = (value.HasValue ? value.Value.AddDays(1) : FirstTradeDate);
		DateOnly until = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(0.0));
		logAction($"[FundEquityUpdater] lastDate = {value}, fromDate = {dateOnly}.", Enums.LogLevel.llBaselogic);
		if (dateOnly > until)
		{
			logAction("[FundEquityUpdater] No missing days, nothing to fetch", Enums.LogLevel.llBaselogic);
			return;
		}
		logAction($"[FundEquityUpdater] Need equity from {dateOnly:yyyy-MM-dd} to {until:yyyy-MM-dd}", Enums.LogLevel.llBaselogic);
		List<(DateOnly Date, double TotalEquityUsd, double EthUsd)> allRows = new List<(DateOnly, double, double)>();
		DateOnly chunkStart = dateOnly;
		while (chunkStart <= until)
		{
			DateOnly chunkEnd = chunkStart.AddDays(364);
			if (chunkEnd > until)
			{
				chunkEnd = until;
			}
			logAction($"[FundEquityUpdater] Fetching chunk {chunkStart:yyyy-MM-dd}..{chunkEnd:yyyy-MM-dd}", Enums.LogLevel.llBaselogic);
			DeribitSettlementExporter deribitSettlementExporter = new DeribitSettlementExporter(cfg.DeribitBaseUrl, keys.ClientId, keys.ClientSecret, chunkStart, chunkEnd, "/tmp/van_fund_equity.csv", logAction, keys.SubaccountId);
			List<(DateOnly, double, double)> list = (from p in await deribitSettlementExporter.GetEquitySeriesAsync()
				where p.TotalEquityUsd.HasValue && p.EthUsd.HasValue
				select (Date: p.Date, TotalEquityUsd: p.TotalEquityUsd.Value, EthUsd: p.EthUsd.Value)).ToList();
			logAction($"[FundEquityUpdater] Chunk {chunkStart:yyyy-MM-dd}..{chunkEnd:yyyy-MM-dd} -> {list.Count} days", Enums.LogLevel.llBaselogic);
			if (list.Count == 0)
			{
				logAction($"[FundEquityUpdater] WARNING: chunk {chunkStart:yyyy-MM-dd}..{chunkEnd:yyyy-MM-dd} returned 0 rows", Enums.LogLevel.llBaselogic);
			}
			allRows.AddRange(list);
			await Task.Delay(TimeSpan.FromSeconds(1.0), ct);
			chunkStart = chunkEnd.AddDays(1);
		}
		if (allRows.Count == 0)
		{
			logAction("[FundEquityUpdater] No TotalEquityUsd values returned from Deribit", Enums.LogLevel.llBaselogic);
			return;
		}
		await repo.UpsertRangeAsync(allRows);
		logAction($"[FundEquityUpdater] Saved {allRows.Count} days into self_custody_fund_equity_curve", Enums.LogLevel.llBaselogic);
	}
}
