using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;
using VANWebService.Services;

namespace VANWebService.Models;

public sealed class CoincallFuturesMarkPriceHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(10);

    private readonly Enums.LogAction _log;
    private readonly ConcurrentDictionary<string, CoincallFuturesMarkPriceQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastUpdatedUtc = DateTime.MinValue;
    private DateTime _lastErrorUtc = DateTime.MinValue;
    private string _lastError = string.Empty;

    public CoincallFuturesMarkPriceHostedService(Enums.LogAction log)
    {
        _log = log;
    }

    public CoincallFuturesMarkPriceSnapshot GetSnapshot()
    {
        return new CoincallFuturesMarkPriceSnapshot(
            _lastUpdatedUtc,
            _lastErrorUtc,
            _lastError,
            _quotes.Values.OrderBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastErrorUtc = DateTime.UtcNow;
                _lastError = ex.Message;
                _log("[COINCALL][MARK][ERROR] " + ex.Message, Enums.LogLevel.llBaselogic);
                await Task.Delay(ErrorBackoff, stoppingToken);
            }
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        var client = new CoincallApiClient(new CoincallAccountRecord());
        using var doc = await client.GetFuturesQuoteSymbolsAsync(ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("CoinCall quote-symbols response has no data array.");

        var now = DateTime.UtcNow;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in data.EnumerateArray())
        {
            string symbol = ReadString(item, "symbol").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol)) continue;
            if (!IsWantedFutures(symbol)) continue;

            decimal? markPrice = ReadDecimal(item, "markPrice");
            decimal? indexPrice = ReadDecimal(item, "indexPrice");
            if (!markPrice.HasValue && !indexPrice.HasValue) continue;

            var quote = new CoincallFuturesMarkPriceQuote(
                symbol,
                ReadString(item, "displayName").Trim(),
                markPrice,
                indexPrice,
                ReadUnixMs(item, "ts"),
                now);
            _quotes[symbol] = quote;
            seen.Add(symbol);
        }

        foreach (var key in _quotes.Keys)
            if (!seen.Contains(key)) _quotes.TryRemove(key, out _);

        _lastUpdatedUtc = now;
        _lastError = string.Empty;
    }

    private static bool IsWantedFutures(string symbol)
        => symbol == "BTCUSD" || symbol == "ETHUSD" || symbol.StartsWith("BTCUSD-", StringComparison.OrdinalIgnoreCase) || symbol.StartsWith("ETHUSD-", StringComparison.OrdinalIgnoreCase);

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

    private static long? ReadUnixMs(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }
}

public sealed record CoincallFuturesMarkPriceSnapshot(DateTime LastUpdatedUtc, DateTime LastErrorUtc, string LastError, IReadOnlyCollection<CoincallFuturesMarkPriceQuote> Items);

public sealed record CoincallFuturesMarkPriceQuote(string Symbol, string DisplayName, decimal? MarkPrice, decimal? IndexPrice, long? SourceTs, DateTime UpdatedUtc);
