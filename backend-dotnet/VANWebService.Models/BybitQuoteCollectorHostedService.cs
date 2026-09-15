using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;

namespace VANWebService.Models;

public sealed class BybitQuoteCollectorHostedService : BackgroundService
{
    private const string BaseUrl = "https://api.bybit.com";
    private const int RecentMinuteRepairDepth = 3;
    private const int SnapshotLagSeconds = 8;
    private static readonly TimeSpan UniverseCacheTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinRequestSpacing = TimeSpan.FromMilliseconds(750);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static readonly Random Jitter = new();
    private static DateTime _nextAllowedRequestUtc = DateTime.MinValue;
    private static readonly HashSet<string> WantedBaseCoins = new(new[] { "BTC", "ETH" }, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WantedSpotSymbols = new(new[] { "BTCUSDT", "ETHUSDT", "BTCUSDC", "ETHUSDC" }, StringComparer.OrdinalIgnoreCase);

    private readonly Enums.LogAction _log;
    private readonly BybitRepository _repo;
    private readonly object _universeLock = new();
    private List<BybitInstrument>? _cachedUniverse;
    private DateTime _cachedUniverseUntilUtc = DateTime.MinValue;
    private int _consecutiveRateLimitCycles;

    private sealed record BybitInstrument(string Category, string Market, string Symbol, string InstrumentName, string DisplayName, string TickerId, string BaseCurrency, string QuoteCurrency, string ProductType, bool IsPerpetual, int? FundingIntervalMinutes);
    private sealed record BybitTicker(decimal? Last, decimal? Mark, decimal? Bid, decimal? Ask, decimal? Index, decimal? CurrentFunding, int? FundingIntervalHours, string RawJson);
    private sealed record BybitKline(DateTime MinuteUtc, decimal? Open, decimal? High, decimal? Low, decimal? Close, decimal? Volume, decimal? QuoteVolume);
    private sealed class BybitRateLimitException : Exception
    {
        public BybitRateLimitException(string message) : base(message) { }
    }

    public BybitQuoteCollectorHostedService(Enums.LogAction log, BybitRepository repo)
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
            _log("[BYBIT][QUOTE][SCHEMA][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DelayUntilNextSnapshotAsync(stoppingToken);
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
        try
        {
            DateTime currentMinuteUtc = BybitRepository.TruncateToMinuteUtc(DateTime.UtcNow);
            var instruments = await LoadUniverseCachedAsync(ct);
            var tickers = await LoadTickersByCategoryAsync(instruments.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase), ct);
            int saved = 0;
            int errors = 0;
            int rateLimitErrors = 0;

            foreach (var instrument in instruments)
            {
                try
                {
                    tickers.TryGetValue(BuildTickerKey(instrument.Category, instrument.Symbol), out var ticker);
                    ticker ??= new BybitTicker(null, null, null, null, null, null, null, "{}");
                    var klines = await LoadKlinesAsync(instrument, RecentMinuteRepairDepth, ct);
                    var currentKline = klines.FirstOrDefault(x => x.MinuteUtc == currentMinuteUtc) ?? klines.OrderByDescending(x => x.MinuteUtc).FirstOrDefault();

                    await _repo.UpsertQuoteMinuteAsync(BuildRow(instrument, currentMinuteUtc, currentKline, ticker), ct);
                    saved++;

                    foreach (var kline in klines.Where(x => x.MinuteUtc < currentMinuteUtc).OrderBy(x => x.MinuteUtc))
                    {
                        await _repo.UpsertQuoteMinuteAsync(BuildRow(instrument, kline.MinuteUtc, kline, ticker), ct);
                        saved++;
                    }
                }
                catch (BybitRateLimitException ex)
                {
                    errors++;
                    rateLimitErrors++;
                    _log($"[BYBIT][QUOTE][{instrument.Category} {instrument.Symbol}][RATE_LIMIT][SKIP] {ex.Message}", Enums.LogLevel.llBaselogic);
                }
                catch (Exception ex)
                {
                    errors++;
                    _log($"[BYBIT][QUOTE][{instrument.Category} {instrument.Symbol}][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
                }
            }

            if (rateLimitErrors > 0)
            {
                _consecutiveRateLimitCycles++;
                string level = _consecutiveRateLimitCycles >= 3 ? "ERROR" : "WARN";
                _log($"[BYBIT][QUOTE][RATE_LIMIT][{level}] instruments={instruments.Count} rows={saved} rate_limit_skips={rateLimitErrors} errors={errors} consecutive_cycles={_consecutiveRateLimitCycles}", _consecutiveRateLimitCycles >= 3 ? Enums.LogLevel.llExceptions : Enums.LogLevel.llBaselogic);
            }
            else
            {
                _consecutiveRateLimitCycles = 0;
            }

            _log($"[BYBIT][QUOTE][OK] instruments={instruments.Count} rows={saved} errors={errors} rate_limit_skips={rateLimitErrors}", Enums.LogLevel.llBaselogic);
        }
        catch (BybitRateLimitException ex)
        {
            _consecutiveRateLimitCycles++;
            string level = _consecutiveRateLimitCycles >= 3 ? "ERROR" : "WARN";
            _log($"[BYBIT][QUOTE][CYCLE][RATE_LIMIT][{level}] {ex.Message} consecutive_cycles={_consecutiveRateLimitCycles}", _consecutiveRateLimitCycles >= 3 ? Enums.LogLevel.llExceptions : Enums.LogLevel.llBaselogic);
        }
        catch (Exception ex)
        {
            _log("[BYBIT][QUOTE][CYCLE][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }
    }

    private static BybitQuoteMinuteRow BuildRow(BybitInstrument instrument, DateTime minuteUtc, BybitKline? kline, BybitTicker ticker)
    {
        decimal? quoteVolume = kline?.QuoteVolume;
        return new BybitQuoteMinuteRow
        {
            exchange = "Bybit",
            market = instrument.Market,
            category = instrument.Category,
            symbol = instrument.Symbol,
            instrumentName = instrument.InstrumentName,
            displayName = instrument.DisplayName,
            tickerId = instrument.TickerId,
            baseCurrency = instrument.BaseCurrency,
            quoteCurrency = instrument.QuoteCurrency,
            productType = instrument.ProductType,
            minuteUtc = minuteUtc,
            last = ticker.Last ?? kline?.Close,
            mark = ticker.Mark,
            bid = ticker.Bid,
            ask = ticker.Ask,
            index = ticker.Index,
            open = kline?.Open,
            high = kline?.High,
            low = kline?.Low,
            close = kline?.Close ?? ticker.Last,
            volume = kline?.Volume,
            volumeUsd = string.Equals(instrument.QuoteCurrency, "USD", StringComparison.OrdinalIgnoreCase) ? quoteVolume : null,
            quoteVolume = quoteVolume,
            currentFunding = instrument.IsPerpetual ? ticker.CurrentFunding : null,
            interest8h = null,
            rawJson = string.IsNullOrWhiteSpace(ticker.RawJson) ? "{}" : ticker.RawJson
        };
    }

    private static bool IsConfirmedEightHourPerpetual(BybitInstrument instrument, BybitTicker ticker)
    {
        return instrument.IsPerpetual
            && (ticker.FundingIntervalHours == 8 || instrument.FundingIntervalMinutes == 480);
    }

    private async Task<List<BybitInstrument>> LoadUniverseCachedAsync(CancellationToken ct)
    {
        lock (_universeLock)
        {
            if (_cachedUniverse is { Count: > 0 } && DateTime.UtcNow < _cachedUniverseUntilUtc)
                return _cachedUniverse.ToList();
        }

        try
        {
            var loaded = await LoadUniverseAsync(ct);
            lock (_universeLock)
            {
                _cachedUniverse = loaded.ToList();
                _cachedUniverseUntilUtc = DateTime.UtcNow.Add(UniverseCacheTtl);
            }
            return loaded;
        }
        catch (BybitRateLimitException ex)
        {
            lock (_universeLock)
            {
                if (_cachedUniverse is { Count: > 0 })
                {
                    _log("[BYBIT][QUOTE][UNIVERSE][RATE_LIMIT][CACHE] " + ex.Message, Enums.LogLevel.llBaselogic);
                    _cachedUniverseUntilUtc = DateTime.UtcNow.AddMinutes(5);
                    return _cachedUniverse.ToList();
                }
            }
            throw;
        }
    }

    private static async Task<List<BybitInstrument>> LoadUniverseAsync(CancellationToken ct)
    {
        var list = new List<BybitInstrument>();
        list.AddRange(await LoadSpotUniverseAsync(ct));
        list.AddRange(await LoadContractUniverseAsync("linear", ct));
        list.AddRange(await LoadContractUniverseAsync("inverse", ct));
        return list
            .GroupBy(x => x.Category + "|" + x.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Market, StringComparer.Ordinal)
            .ThenBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.Symbol, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<List<BybitInstrument>> LoadSpotUniverseAsync(CancellationToken ct)
    {
        using var doc = await GetBybitAsync("/v5/market/instruments-info?category=spot", ct);
        var result = new List<BybitInstrument>();
        foreach (var item in EnumerateResultList(doc.RootElement))
        {
            string symbol = ReadString(item, "symbol").Trim().ToUpperInvariant();
            string status = ReadString(item, "status");
            string baseCoin = ReadString(item, "baseCoin").Trim().ToUpperInvariant();
            string quoteCoin = ReadString(item, "quoteCoin").Trim().ToUpperInvariant();
            if (!WantedSpotSymbols.Contains(symbol) || !string.Equals(status, "Trading", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new BybitInstrument("spot", "spot", symbol, symbol, symbol, symbol, baseCoin, quoteCoin, "Spot", false, null));
        }
        return result;
    }

    private static async Task<List<BybitInstrument>> LoadContractUniverseAsync(string category, CancellationToken ct)
    {
        var result = new List<BybitInstrument>();
        string? cursor = null;
        do
        {
            string path = "/v5/market/instruments-info?category=" + Uri.EscapeDataString(category) + "&limit=1000";
            if (!string.IsNullOrWhiteSpace(cursor)) path += "&cursor=" + Uri.EscapeDataString(cursor);
            using var doc = await GetBybitAsync(path, ct);
            foreach (var item in EnumerateResultList(doc.RootElement))
            {
                string symbol = ReadString(item, "symbol").Trim().ToUpperInvariant();
                string status = ReadString(item, "status");
                string baseCoin = ReadString(item, "baseCoin").Trim().ToUpperInvariant();
                string quoteCoin = ReadString(item, "quoteCoin").Trim().ToUpperInvariant();
                string contractType = ReadString(item, "contractType").Trim();
                string displayName = ReadString(item, "displayName").Trim();
                int? fundingIntervalMinutes = ReadInt(item, "fundingInterval");
                if (string.IsNullOrWhiteSpace(symbol) || !WantedBaseCoins.Contains(baseCoin) || !string.Equals(status, "Trading", StringComparison.OrdinalIgnoreCase)) continue;
                bool isPerpetual = contractType.IndexOf("Perpetual", StringComparison.OrdinalIgnoreCase) >= 0;
                result.Add(new BybitInstrument(category, "futures", symbol, symbol, string.IsNullOrWhiteSpace(displayName) ? symbol : displayName, symbol, baseCoin, quoteCoin, contractType, isPerpetual, fundingIntervalMinutes));
            }
            cursor = ReadString(doc.RootElement.GetProperty("result"), "nextPageCursor");
        } while (!string.IsNullOrWhiteSpace(cursor));
        return result;
    }

    private static async Task<Dictionary<string, BybitTicker>> LoadTickersByCategoryAsync(IEnumerable<string> categories, CancellationToken ct)
    {
        var result = new Dictionary<string, BybitTicker>(StringComparer.OrdinalIgnoreCase);
        foreach (string category in categories.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            using var doc = await GetBybitAsync("/v5/market/tickers?category=" + Uri.EscapeDataString(category), ct);
            foreach (var item in EnumerateResultList(doc.RootElement))
            {
                string symbol = ReadString(item, "symbol").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol)) continue;
                result[BuildTickerKey(category, symbol)] = ParseTicker(item);
            }
        }
        return result;
    }

    private static string BuildTickerKey(string category, string symbol)
    {
        return category.Trim().ToLowerInvariant() + "|" + symbol.Trim().ToUpperInvariant();
    }

    private static BybitTicker ParseTicker(JsonElement item)
    {
        decimal? funding = ReadDecimal(item, "fundingRate");
        return new BybitTicker(
            ReadDecimal(item, "lastPrice"),
            ReadDecimal(item, "markPrice"),
            ReadDecimal(item, "bid1Price"),
            ReadDecimal(item, "ask1Price"),
            ReadDecimal(item, "indexPrice") ?? ReadDecimal(item, "usdIndexPrice"),
            funding,
            ReadInt(item, "fundingIntervalHour"),
            item.GetRawText());
    }

    private static async Task<BybitTicker> LoadTickerAsync(BybitInstrument instrument, CancellationToken ct)
    {
        using var doc = await GetBybitAsync("/v5/market/tickers?category=" + Uri.EscapeDataString(instrument.Category) + "&symbol=" + Uri.EscapeDataString(instrument.Symbol), ct);
        var item = EnumerateResultList(doc.RootElement).FirstOrDefault();
        if (item.ValueKind == JsonValueKind.Undefined) return new BybitTicker(null, null, null, null, null, null, null, "{}");
        return ParseTicker(item);
    }

    private static async Task<List<BybitKline>> LoadKlinesAsync(BybitInstrument instrument, int limit, CancellationToken ct)
    {
        using var doc = await GetBybitAsync("/v5/market/kline?category=" + Uri.EscapeDataString(instrument.Category) + "&symbol=" + Uri.EscapeDataString(instrument.Symbol) + "&interval=1&limit=" + Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture), ct);
        if (!doc.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
            return new List<BybitKline>();

        return list.EnumerateArray()
            .Where(row => row.ValueKind == JsonValueKind.Array && row.GetArrayLength() >= 7)
            .Select(row => new BybitKline(
                DateTimeOffset.FromUnixTimeMilliseconds(ReadLong(row[0])).UtcDateTime,
                ReadDecimal(row[1]),
                ReadDecimal(row[2]),
                ReadDecimal(row[3]),
                ReadDecimal(row[4]),
                ReadDecimal(row[5]),
                ReadDecimal(row[6])))
            .OrderBy(x => x.MinuteUtc)
            .ToList();
    }

    private static async Task<JsonDocument> GetBybitAsync(string path, CancellationToken ct)
    {
        Exception? lastError = null;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            await WaitForRequestSlotAsync(ct);
            using var res = await Http.GetAsync(BaseUrl + path, ct);
            string body = await res.Content.ReadAsStringAsync(ct);
            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(body);
                int retCode = doc.RootElement.TryGetProperty("retCode", out var codeEl) && codeEl.TryGetInt32(out var c) ? c : -1;
                if (res.IsSuccessStatusCode && retCode == 0) return doc;
                string retMsg = doc.RootElement.TryGetProperty("retMsg", out var msgEl) ? msgEl.GetString() ?? string.Empty : string.Empty;
                if (IsRateLimit(res.StatusCode, retCode, retMsg))
                {
                    doc.Dispose();
                    lastError = new BybitRateLimitException(string.IsNullOrWhiteSpace(retMsg) ? "Bybit rate limit" : retMsg);
                    await DelayAfterRateLimitAsync(attempt, ct);
                    continue;
                }

                doc.Dispose();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(retMsg) ? "Bybit HTTP " + (int)res.StatusCode : retMsg);
            }
            catch (JsonException ex)
            {
                doc?.Dispose();
                if (res.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    lastError = new BybitRateLimitException("Bybit HTTP 429");
                    await DelayAfterRateLimitAsync(attempt, ct);
                    continue;
                }
                throw new InvalidOperationException("Bybit returned invalid JSON: " + ex.Message);
            }
        }

        throw lastError ?? new BybitRateLimitException("Bybit rate limit");
    }

    private static bool IsRateLimit(HttpStatusCode statusCode, int retCode, string retMsg)
    {
        return statusCode == HttpStatusCode.TooManyRequests
            || retCode == 10006
            || retMsg.IndexOf("Too many visits", StringComparison.OrdinalIgnoreCase) >= 0
            || retMsg.IndexOf("Rate Limit", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static async Task WaitForRequestSlotAsync(CancellationToken ct)
    {
        await RequestGate.WaitAsync(ct);
        try
        {
            DateTime now = DateTime.UtcNow;
            if (_nextAllowedRequestUtc > now)
                await Task.Delay(_nextAllowedRequestUtc - now, ct);

            int jitterMs;
            lock (Jitter) jitterMs = Jitter.Next(0, 150);
            _nextAllowedRequestUtc = DateTime.UtcNow.Add(MinRequestSpacing).AddMilliseconds(jitterMs);
        }
        finally
        {
            RequestGate.Release();
        }
    }

    private static async Task DelayAfterRateLimitAsync(int attempt, CancellationToken ct)
    {
        int jitterMs;
        lock (Jitter) jitterMs = Jitter.Next(250, 1500);
        int backoffSeconds = attempt switch
        {
            1 => 10,
            2 => 20,
            _ => 40
        };
        await Task.Delay(TimeSpan.FromSeconds(backoffSeconds).Add(TimeSpan.FromMilliseconds(jitterMs)), ct);
    }

    private static IEnumerable<JsonElement> EnumerateResultList(JsonElement root)
    {
        if (!root.TryGetProperty("result", out var result) || !result.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in list.EnumerateArray()) yield return item;
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.ToString()
        };
    }

    private static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        return ReadDecimal(value);
    }

    private static decimal? ReadDecimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    private static long ReadLong(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return 0;
    }

    private static int? ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    private static async Task DelayUntilNextSnapshotAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        DateTime target = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1)
            .AddSeconds(SnapshotLagSeconds);
        if (target <= now) target = target.AddMinutes(1);
        await Task.Delay(target - now, ct);
    }
}
