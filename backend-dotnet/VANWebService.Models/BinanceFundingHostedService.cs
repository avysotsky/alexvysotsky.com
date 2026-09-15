using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;
using VANWebService.Services;

namespace VANWebService.Models;

public sealed class BinanceFundingHostedService : BackgroundService
{
    private static readonly string[] Symbols = { "BTCUSDT", "ETHUSDT" };
    private readonly Enums.LogAction _log;
    private readonly BinanceRepository _repo;

    public BinanceFundingHostedService(Enums.LogAction log, BinanceRepository repo)
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
            _log("[BINANCE][FUNDING][SCHEMA][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }

        await RunCycleAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DelayUntilNextMinutePlus2Async(stoppingToken);
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
        int errors = 0;
        DateTime observedMinuteUtc = BinanceRepository.TruncateToMinuteUtc(DateTime.UtcNow);
        foreach (var symbol in Symbols)
        {
            try
            {
                using var doc = await BinanceApiClient.GetPublicAsync("/fapi/v1/premiumIndex?symbol=" + Uri.EscapeDataString(symbol), ct);
                var row = ParsePremiumIndex(doc.RootElement, symbol, observedMinuteUtc);
                if (row != null)
                {
                    await _repo.UpsertFundingMinuteAsync(row, ct);
                    saved++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors++;
                _log($"[BINANCE][FUNDING][{symbol}][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
            }
        }
        _log($"[BINANCE][FUNDING][OK] rows={saved} errors={errors}", Enums.LogLevel.llBaselogic);
    }

    private static BinanceFundingRow? ParsePremiumIndex(JsonElement item, string requestedSymbol, DateTime observedMinuteUtc)
    {
        string symbol = BinanceRepository.NormalizeFundingSymbol(ReadString(item, "symbol"));
        if (!string.Equals(symbol, requestedSymbol, StringComparison.OrdinalIgnoreCase)) return null;
        return new BinanceFundingRow
        {
            symbol = symbol,
            displayName = BinanceRepository.DisplayName(symbol),
            minuteUtc = observedMinuteUtc,
            fundingTimeUtc = ReadTime(item, "time"),
            nextFundingTimeUtc = ReadTime(item, "nextFundingTime"),
            currentFunding = ReadDecimal(item, "lastFundingRate"),
            interest8h = ReadDecimal(item, "interestRate"),
            markPrice = ReadDecimal(item, "markPrice"),
            indexPrice = ReadDecimal(item, "indexPrice"),
            source = "binance:/fapi/v1/premiumIndex",
            rawJson = item.GetRawText()
        };
    }

    private static async Task DelayUntilNextMinutePlus2Async(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var nextMinute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero).AddMinutes(1).AddSeconds(2);
        await Task.Delay(nextMinute - now, ct);
    }

    private static string ReadString(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static decimal? ReadDecimal(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static DateTime? ReadTime(JsonElement item, string name)
    {
        string raw = ReadString(item, name);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        return null;
    }
}
