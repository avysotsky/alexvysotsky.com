using System;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;
using VANWebService.Services;

namespace VANWebService.Models;

public sealed class OkxSnapshotHostedService : BackgroundService
{
    private readonly VANWebServiceConfig _cfg;
    private readonly Enums.LogAction _log;
    private readonly OkxRepository _repo;

    public OkxSnapshotHostedService(VANWebServiceConfig cfg, Enums.LogAction log, OkxRepository repo)
    {
        _cfg = cfg;
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
            _log("[OKX][SNAPSHOT][SCHEMA][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycle(stoppingToken);
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch
            {
                break;
            }
        }
    }

    private async Task RunCycle(CancellationToken ct)
    {
        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            DateTime minuteUtc = OkxRepository.TruncateToMinuteUtc(nowUtc);
            DateOnly dayUtc = DateOnly.FromDateTime(nowUtc);
            bool writeLegacySnapshot = minuteUtc.Minute % 5 == 0;
            var accounts = await _repo.GetAccountsAsync(includeSecrets: true, ct);
            foreach (var account in accounts)
            {
                try
                {
                    var client = new OkxApiClient(account);
                    var snap = await client.GetLiveSnapshotAsync(ct);
                    if (writeLegacySnapshot)
                    {
                        await _repo.AddSnapshotAsync(account.id, snap.totalEquityUsdt, snap.availableEquityUsdt, snap.unrealizedPnlUsdt, snap.marginRatio, ct);
                    }

                    string equityCurrency = OkxRepository.NormalizeEquityCurrency(account.equityCurrency);
                    decimal totalEquity = snap.totalEquityUsdt;
                    decimal availableEquity = snap.availableEquityUsdt;
                    decimal unrealizedPnl = snap.unrealizedPnlUsdt;
                    if (equityCurrency == "BTC")
                    {
                        decimal btcUsdt = await client.GetBtcUsdtLastPriceAsync(ct);
                        if (btcUsdt <= 0m)
                        {
                            throw new InvalidOperationException("BTC-USDT price is unavailable for BTC PnL conversion");
                        }
                        totalEquity /= btcUsdt;
                        availableEquity /= btcUsdt;
                        unrealizedPnl /= btcUsdt;
                    }

                    await _repo.UpsertDailyEquityAsync(account.id, dayUtc, minuteUtc, equityCurrency, totalEquity, availableEquity, unrealizedPnl, snap.marginRatio, ct);
                    if (account.storeMinuteEquity)
                    {
                        await _repo.UpsertMinuteEquityAsync(account.id, minuteUtc, equityCurrency, totalEquity, availableEquity, unrealizedPnl, snap.marginRatio, ct);
                    }
                }
                catch (Exception ex)
                {
                    _log($"[OKX][SNAPSHOT][ACCOUNT {account.id}][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
                }
            }
        }
        catch (Exception ex)
        {
            _log("[OKX][SNAPSHOT][CYCLE][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }
    }
}
