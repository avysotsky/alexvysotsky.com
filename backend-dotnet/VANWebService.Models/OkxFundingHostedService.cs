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

public sealed class OkxFundingHostedService : BackgroundService
{
    private static readonly string[] Instruments = { "BTC-USDT-SWAP", "ETH-USDT-SWAP" };
    private readonly Enums.LogAction _log;
    private readonly OkxRepository _repo;

    public OkxFundingHostedService(Enums.LogAction log, OkxRepository repo)
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
            _log("[OKX][FUNDING][SCHEMA][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }

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
        DateTime observedMinuteUtc = OkxRepository.TruncateToMinuteUtc(DateTime.UtcNow);
        foreach (var instId in Instruments)
        {
            try
            {
                using var doc = await OkxApiClient.GetPublicAsync("/api/v5/public/funding-rate?instId=" + Uri.EscapeDataString(instId), ct);
                foreach (var row in ParseCurrentFundingRows(doc.RootElement, instId, observedMinuteUtc))
                {
                    await _repo.UpsertFundingMinuteAsync(row, ct);
                    saved++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors++;
                _log($"[OKX][FUNDING][{instId}][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
            }
        }
        _log($"[OKX][FUNDING][OK] rows={saved} errors={errors}", Enums.LogLevel.llBaselogic);
    }

    private static IEnumerable<OkxFundingRow> ParseCurrentFundingRows(JsonElement root, string requestedInstId, DateTime observedMinuteUtc)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in data.EnumerateArray())
        {
            string instId = OkxRepository.NormalizeFundingInstId(ReadString(item, "instId"));
            if (!string.Equals(instId, requestedInstId, StringComparison.OrdinalIgnoreCase)) continue;
            yield return new OkxFundingRow
            {
                instId = instId,
                displayName = DisplayName(instId),
                minuteUtc = observedMinuteUtc,
                fundingTimeUtc = ReadTime(item, "fundingTime"),
                nextFundingTimeUtc = ReadTime(item, "nextFundingTime"),
                currentFunding = ReadDecimal(item, "fundingRate"),
                interest8h = ReadDecimal(item, "interestRate"),
                settledFunding = ReadDecimal(item, "settFundingRate"),
                settState = ReadString(item, "settState"),
                source = "okx:/api/v5/public/funding-rate",
                rawJson = item.GetRawText()
            };
        }
    }

    private static async Task DelayUntilNextMinutePlus2Async(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var nextMinute = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero).AddMinutes(1).AddSeconds(2);
        await Task.Delay(nextMinute - now, ct);
    }

    private static string DisplayName(string instId) => instId switch
    {
        "BTC-USDT-SWAP" => "BTCUSDT Perp",
        "ETH-USDT-SWAP" => "ETHUSDT Perp",
        _ => instId
    };

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
