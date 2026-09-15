using System;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;

namespace VANWebService.Models;

public sealed class BinanceSnapshotHostedService : BackgroundService
{
    private readonly Enums.LogAction _log;
    private readonly BinanceRepository _repo;

    public BinanceSnapshotHostedService(Enums.LogAction log, BinanceRepository repo)
    {
        _log = log;
        _repo = repo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _repo.EnsureSchemaAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _log("[BINANCE][SNAPSHOT][SCHEMA][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }

        await RunCycleAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DelayUntilNextMinutePlus5Async(stoppingToken);
            }
            catch
            {
                break;
            }
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        int saved = 0;
        int skipped = 0;
        int errors = 0;
        try
        {
            var accounts = await _repo.GetAccountsAsync(includeSecrets: true, ct);
            foreach (var account in accounts)
            {
                if (!account.storeMinuteEquity)
                {
                    skipped++;
                    continue;
                }
                try
                {
                    var summary = await VANWebService.BinanceEndpoints.BuildAccountSummaryAsync(account, ct);
                    if (!summary.ok)
                    {
                        errors++;
                        _log($"[BINANCE][SNAPSHOT][ACCOUNT {account.id}][ERROR] {summary.message} code={summary.code}", Enums.LogLevel.llBaselogic);
                        continue;
                    }
                    await VANWebService.BinanceEndpoints.StoreSummarySnapshotAsync(_repo, account, summary, true, ct);
                    saved++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors++;
                    _log($"[BINANCE][SNAPSHOT][ACCOUNT {account.id}][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
                }
            }
            _log($"[BINANCE][SNAPSHOT][OK] saved={saved} skipped={skipped} errors={errors}", Enums.LogLevel.llBaselogic);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log("[BINANCE][SNAPSHOT][CYCLE][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }
    }

    private static async Task DelayUntilNextMinutePlus5Async(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero).AddMinutes(1).AddSeconds(5);
        if (next <= now) next = next.AddMinutes(1);
        await Task.Delay(next - now, ct);
    }
}
