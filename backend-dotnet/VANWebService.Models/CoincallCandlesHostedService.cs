using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;
using VANWebService.Services;

namespace VANWebService.Models;

public sealed class CoincallCandlesHostedService : BackgroundService
{
    private static readonly string[] WantedSpotSymbols = { "BTCUSDT", "ETHUSDT" };
    private static readonly HashSet<string> WantedSpotSet = new(WantedSpotSymbols, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WantedFuturesBaseSet = new(new[] { "BTC", "ETH" }, StringComparer.OrdinalIgnoreCase);
    private readonly Enums.LogAction _log;
    private readonly CoincallRepository _repo;
    private const int SpotParallelism = 12;
    private const int FuturesParallelism = 24;
    private const int RecentMinuteRepairDepth = 5;
    private const int CloseSnapshotLagSeconds = 5;
    private const int FuturesWsHeartbeatSeconds = 15;
    private const int FuturesWsReconnectDelaySeconds = 5;
    private const int FuturesWsRefreshMinutes = 8;
    private const int FuturesWsStateRetentionMinutes = 15;
    private const int BidAskFilterWindow = 20;
    private const decimal BidAskFilterMaxRelativeDeviation = 0.01m;
    private const string FuturesBookStep = "step0";
    private const string SpotWsUrl = "wss://ws.coincall.com/spot/ws";
    private readonly ConcurrentDictionary<string, FuturesMinuteWsState> _futuresMinuteWs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SpotMinuteWsState> _spotMinuteWs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, BidAskFilterState> _bidAskFilter = new(StringComparer.OrdinalIgnoreCase);

    private sealed class BidAskFilterState
    {
        public readonly object Gate = new();
        public readonly Queue<decimal> Values = new();
        public decimal? LastAccepted;
    }

    private sealed record SpotInstrument(string Symbol, string BaseCurrency, string QuoteCurrency);
    private sealed record SpotCloseSnapshot(decimal? BestBid, decimal? BestAsk);
    private sealed record FuturesInstrument(string Symbol, string TickerId, string BaseCurrency, string QuoteCurrency, string ProductType, string DisplayName, decimal? MarkPrice, decimal? IndexPrice);
    private sealed record FuturesCloseSnapshot(decimal? MarkPrice, decimal? IndexPrice, decimal? BestBid, decimal? BestAsk);
    private sealed record FuturesMinuteWsState(string Symbol, DateTime MinuteUtc, decimal? LastTradeClose, DateTime LastTradeTsUtc, decimal? BestBidClose, decimal? BestAskClose, DateTime BookTsUtc);
    private sealed record SpotMinuteWsState(string Symbol, DateTime MinuteUtc, decimal IndexPriceClose, DateTime IndexTsUtc);

    public CoincallCandlesHostedService(Enums.LogAction log, CoincallRepository repo)
    {
        _log = log;
        _repo = repo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task futuresWsTask = RunFuturesWsLoopAsync(stoppingToken);
        Task spotWsTask = RunSpotWsLoopAsync(stoppingToken);
        try
        {
            await _repo.EnsureSchemaAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _log("[COINCALL][CANDLES][SCHEMA][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DelayUntilNextCloseSnapshotAsync(stoppingToken);
            }
            catch
            {
                break;
            }
            await RunCycle(stoppingToken);
        }
        try
        {
            await Task.WhenAll(futuresWsTask, spotWsTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log("[COINCALL][WS][STOP][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }
    }

    private (decimal? Bid, decimal? Ask) FilterFuturesBidAskClose(string symbol, decimal? bid, decimal? ask)
    {
        return (
            FilterBidAskValue("futures:" + symbol, "bid", bid),
            FilterBidAskValue("futures:" + symbol, "ask", ask)
        );
    }

    private static (decimal? Bid, decimal? Ask) SanitizeFuturesBidAsk(decimal? bid, decimal? ask, decimal? referencePrice)
    {
        return (
            SanitizeFuturesBookSide(bid, referencePrice),
            SanitizeFuturesBookSide(ask, referencePrice)
        );
    }

    private static decimal? SanitizeFuturesBookSide(decimal? raw, decimal? referencePrice)
    {
        if (!raw.HasValue || raw.Value <= 0m) return null;
        if (!referencePrice.HasValue || referencePrice.Value <= 0m) return raw;
        decimal maxDeviation = Math.Max(Math.Abs(referencePrice.Value) * BidAskFilterMaxRelativeDeviation, 0.00000001m);
        return Math.Abs(raw.Value - referencePrice.Value) <= maxDeviation ? raw.Value : null;
    }

    private decimal? FilterBidAskValue(string key, string side, decimal? raw)
    {
        if (!raw.HasValue || raw.Value <= 0m) return null;
        string mapKey = key + "::" + side;
        var entry = _bidAskFilter.GetOrAdd(mapKey, _ => new BidAskFilterState());
        lock (entry.Gate)
        {
            if (entry.Values.Count < BidAskFilterWindow)
            {
                entry.Values.Enqueue(raw.Value);
                entry.LastAccepted = raw.Value;
                return entry.Values.Count == BidAskFilterWindow ? raw.Value : null;
            }

            decimal average = entry.Values.Count > 0 ? entry.Values.Average() : 0m;
            if (average <= 0m) return null;

            decimal maxDeviation = Math.Max(Math.Abs(average) * BidAskFilterMaxRelativeDeviation, 0.00000001m);
            bool outlier = string.Equals(side, "bid", StringComparison.OrdinalIgnoreCase)
                ? raw.Value < average - maxDeviation
                : raw.Value > average + maxDeviation;
            if (outlier)
                return entry.LastAccepted.HasValue && entry.LastAccepted.Value > 0m ? entry.LastAccepted.Value : null;

            entry.Values.Enqueue(raw.Value);
            while (entry.Values.Count > BidAskFilterWindow) entry.Values.Dequeue();
            entry.LastAccepted = raw.Value;
            return raw.Value;
        }
    }

    private async Task RunCycle(CancellationToken ct)
    {
        var client = new CoincallApiClient(new CoincallAccountRecord());
        try
        {
            DateTime currentMinuteUtc = CoincallRepository.TruncateToMinuteUtc(DateTime.UtcNow);
            DateTime closeMinuteUtc = currentMinuteUtc.AddMinutes(-1);
            PruneFuturesWsState(closeMinuteUtc.AddMinutes(-FuturesWsStateRetentionMinutes));
            PruneSpotWsState(closeMinuteUtc.AddMinutes(-FuturesWsStateRetentionMinutes));
            var spot = await LoadSpotUniverseAsync(client, ct);
            var futures = await LoadFuturesUniverseAsync(client, ct);
            int spotSaved = await ProcessInParallelAsync(spot, SpotParallelism, async instrument =>
            {
                try
                {
                    var closeSnapshot = await LoadSpotCloseSnapshotAsync(client, instrument, ct);
                    var candles = await LoadSpotMinuteCandlesAsync(client, instrument, RecentMinuteRepairDepth, ct);
                    int saved = 0;
                    foreach (var candle in candles)
                    {
                        if (TryGetSpotMinuteWsState(instrument.Symbol, candle.minuteUtc, out var spotWsState))
                        {
                            candle.indexPriceClose = spotWsState.IndexPriceClose;
                        }
                        if (candle.minuteUtc == closeMinuteUtc || candle.minuteUtc == currentMinuteUtc)
                        {
                            candle.bestBidClose = closeSnapshot.BestBid;
                            candle.bestAskClose = closeSnapshot.BestAsk;
                        }
                        await _repo.UpsertSpotMinuteCandleAsync(candle, ct);
                        saved++;
                    }
                    return saved > 0;
                }
                catch (Exception ex)
                {
                    _log($"[COINCALL][CANDLES][SPOT {instrument.Symbol}][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
                    return false;
                }
            }, ct);

            int futuresSaved = await ProcessInParallelAsync(futures, FuturesParallelism, async instrument =>
            {
                try
                {
                    var closeSnapshot = await LoadFuturesCloseSnapshotAsync(client, instrument, ct);
                    var candles = await LoadFuturesMinuteCandlesAsync(client, instrument, RecentMinuteRepairDepth, ct);
                    TryGetFuturesMinuteWsState(instrument.Symbol, closeMinuteUtc, out var closeMinuteWs);
                    TryGetFuturesMinuteWsState(instrument.Symbol, currentMinuteUtc, out var currentMinuteWs);
                    bool hasCloseMinuteCandle = false;
                    bool hasCurrentMinuteCandle = false;
                    decimal? closeBid = closeMinuteWs is not null ? closeMinuteWs.BestBidClose : null;
                    decimal? closeAsk = closeMinuteWs is not null ? closeMinuteWs.BestAskClose : null;
                    decimal? currentBid = currentMinuteWs is not null ? currentMinuteWs.BestBidClose : null;
                    decimal? currentAsk = currentMinuteWs is not null ? currentMinuteWs.BestAskClose : null;
                    int saved = 0;
                    foreach (var candle in candles)
                    {
                        candle.bestBidClose = null;
                        candle.bestAskClose = null;
                        if (TryGetFuturesMinuteWsState(instrument.Symbol, candle.minuteUtc, out var wsState))
                        {
                            // REST keeps the minute shell truthful for O/H/L/V; WS supplies trade and filtered top-of-book close.
                            if (wsState.LastTradeClose.HasValue) candle.close = wsState.LastTradeClose.Value;
                            candle.bestBidClose = wsState.BestBidClose;
                            candle.bestAskClose = wsState.BestAskClose;
                        }
                        if (candle.minuteUtc == closeMinuteUtc)
                        {
                            hasCloseMinuteCandle = true;
                            candle.markPriceClose = closeSnapshot.MarkPrice ?? instrument.MarkPrice;
                            candle.indexPriceClose = closeSnapshot.IndexPrice ?? instrument.IndexPrice;
                            candle.bestBidClose = closeBid;
                            candle.bestAskClose = closeAsk;
                        }
                        if (candle.minuteUtc == currentMinuteUtc)
                        {
                            hasCurrentMinuteCandle = true;
                            candle.markPriceClose = closeSnapshot.MarkPrice ?? instrument.MarkPrice;
                            candle.indexPriceClose = closeSnapshot.IndexPrice ?? instrument.IndexPrice;
                            candle.bestBidClose = currentBid;
                            candle.bestAskClose = currentAsk;
                        }
                        await _repo.UpsertFuturesMinuteCandleAsync(candle, ct);
                        saved++;
                    }
                    if (!hasCloseMinuteCandle)
                    {
                        await _repo.UpsertFuturesMinuteCandleAsync(BuildFuturesSnapshotOnlyMinute(
                            instrument,
                            closeMinuteUtc,
                            closeMinuteWs?.LastTradeClose,
                            closeSnapshot.MarkPrice ?? instrument.MarkPrice,
                            closeSnapshot.IndexPrice ?? instrument.IndexPrice,
                            closeBid,
                            closeAsk), ct);
                        saved++;
                    }
                    else if (await _repo.TryPatchFuturesMinuteCloseSnapshotAsync(
                        instrument.Symbol,
                        closeMinuteUtc,
                        closeSnapshot.MarkPrice ?? instrument.MarkPrice,
                        closeSnapshot.IndexPrice ?? instrument.IndexPrice,
                        closeBid,
                        closeAsk,
                        ct))
                    {
                        saved++;
                    }
                    if (!hasCurrentMinuteCandle)
                    {
                        await _repo.UpsertFuturesMinuteCandleAsync(BuildFuturesSnapshotOnlyMinute(
                            instrument,
                            currentMinuteUtc,
                            currentMinuteWs?.LastTradeClose,
                            closeSnapshot.MarkPrice ?? instrument.MarkPrice,
                            closeSnapshot.IndexPrice ?? instrument.IndexPrice,
                            currentBid,
                            currentAsk), ct);
                        saved++;
                    }
                    else if (await _repo.TryPatchFuturesMinuteCloseSnapshotAsync(
                        instrument.Symbol,
                        currentMinuteUtc,
                        closeSnapshot.MarkPrice ?? instrument.MarkPrice,
                        closeSnapshot.IndexPrice ?? instrument.IndexPrice,
                        currentBid,
                        currentAsk,
                        ct))
                    {
                        saved++;
                    }
                    return saved > 0;
                }
                catch (Exception ex)
                {
                    _log($"[COINCALL][CANDLES][FUTURES {instrument.Symbol}][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
                    return false;
                }
            }, ct);

            var spreadMinutes = Enumerable.Range(0, RecentMinuteRepairDepth + 1)
                .Select(offset => currentMinuteUtc.AddMinutes(-offset))
                .ToArray();
            int spreadsSaved = await _repo.UpsertFuturesSpreadMinutesFromCandlesAsync(spreadMinutes, ct);

            _log($"[COINCALL][CANDLES][OK] spot={spotSaved}/{spot.Count} futures={futuresSaved}/{futures.Count} spreads={spreadsSaved}", Enums.LogLevel.llBaselogic);
        }
        catch (Exception ex)
        {
            _log("[COINCALL][CANDLES][CYCLE][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }
    }

    public async Task<CoincallChartMinuteRow?> TryGetLiveFuturesOpenMinuteAsync(string symbol, CancellationToken ct)
    {
        string normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        var client = new CoincallApiClient(new CoincallAccountRecord());
        var instrument = (await LoadFuturesUniverseAsync(client, ct))
            .FirstOrDefault(x => string.Equals(x.Symbol, normalized, StringComparison.OrdinalIgnoreCase));
        if (instrument == null) return null;

        DateTime currentMinuteUtc = CoincallRepository.TruncateToMinuteUtc(DateTime.UtcNow);
        var liveCandle = (await LoadFuturesMinuteCandlesAsync(client, instrument, 3, ct))
            .LastOrDefault(x => x.minuteUtc == currentMinuteUtc);
        var closeSnapshot = await LoadFuturesCloseSnapshotAsync(client, instrument, ct);

        if (TryGetFuturesMinuteWsState(instrument.Symbol, currentMinuteUtc, out var wsState))
        {
            liveCandle ??= BuildFuturesSnapshotOnlyMinute(instrument, currentMinuteUtc, null, instrument.MarkPrice, instrument.IndexPrice, null, null);
            if (wsState.LastTradeClose.HasValue) liveCandle.close = wsState.LastTradeClose.Value;
            if (wsState.BestBidClose.HasValue) liveCandle.bestBidClose = wsState.BestBidClose;
            if (wsState.BestAskClose.HasValue) liveCandle.bestAskClose = wsState.BestAskClose;
        }
        if (liveCandle == null && (instrument.MarkPrice.HasValue || instrument.IndexPrice.HasValue)) liveCandle = BuildFuturesSnapshotOnlyMinute(instrument, currentMinuteUtc, null, instrument.MarkPrice, instrument.IndexPrice, null, null);
        if (liveCandle == null) return null;

        // There is no confirmed mark WS stream here; use the current quote-symbols mark snapshot as a separate best-available source.
        liveCandle.markPriceClose = closeSnapshot.MarkPrice ?? instrument.MarkPrice;
        liveCandle.indexPriceClose = closeSnapshot.IndexPrice ?? instrument.IndexPrice;
        return new CoincallChartMinuteRow
        {
            minuteUtc = liveCandle.minuteUtc,
            open = liveCandle.open,
            high = liveCandle.high,
            low = liveCandle.low,
            close = liveCandle.close,
            volume = liveCandle.volume,
            markPriceClose = liveCandle.markPriceClose,
            indexPriceClose = liveCandle.indexPriceClose,
            bestBidClose = liveCandle.bestBidClose,
            bestAskClose = liveCandle.bestAskClose
        };
    }

    public (decimal Bid, decimal Ask, DateTime TsUtc)? TryGetLatestLiveFuturesBidAsk(string symbol, TimeSpan maxAge)
    {
        string normalized = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        DateTime minTsUtc = DateTime.UtcNow - maxAge;
        FuturesMinuteWsState? best = null;
        foreach (var item in _futuresMinuteWs.Values)
        {
            if (!string.Equals(item.Symbol, normalized, StringComparison.OrdinalIgnoreCase)) continue;
            if (!item.BestBidClose.HasValue || !item.BestAskClose.HasValue) continue;
            if (item.BestBidClose.Value <= 0m || item.BestAskClose.Value <= item.BestBidClose.Value) continue;
            if (item.BookTsUtc < minTsUtc) continue;
            if (best == null || item.BookTsUtc > best.BookTsUtc) best = item;
        }
        if (best == null) return null;
        return (best.BestBidClose.Value, best.BestAskClose.Value, best.BookTsUtc);
    }

    private async Task RunFuturesWsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = new CoincallApiClient(new CoincallAccountRecord());
                var instruments = await LoadFuturesUniverseAsync(client, ct);
                if (instruments.Count == 0)
                {
                    _log("[COINCALL][WS][EMPTY] futures universe is empty; retrying", Enums.LogLevel.llBaselogic);
                    await Task.Delay(TimeSpan.FromSeconds(FuturesWsReconnectDelaySeconds), ct);
                    continue;
                }

                var auth = await client.BuildFuturesWebSocketAuthAsync(ct);
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(auth.Url), ct);
                _log($"[COINCALL][WS][OPEN] futures symbols={instruments.Count}", Enums.LogLevel.llBaselogic);

                foreach (var instrument in instruments)
                {
                    await SendJsonAsync(socket, new { c = 20, dt = 58, d = new { s = instrument.Symbol, step = FuturesBookStep } }, ct);
                    await SendJsonAsync(socket, new { c = 20, dt = 43, d = new { s = instrument.Symbol } }, ct);
                }

                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task heartbeatTask = SendHeartbeatLoopAsync(socket, heartbeatCts.Token);
                DateTime refreshAtUtc = DateTime.UtcNow.AddMinutes(FuturesWsRefreshMinutes);
                try
                {
                    while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open && DateTime.UtcNow < refreshAtUtc)
                    {
                        string? payload = await ReceiveTextAsync(socket, ct);
                        if (string.IsNullOrWhiteSpace(payload)) break;
                        await HandleFuturesWsMessageAsync(socket, payload, ct);
                    }
                }
                finally
                {
                    heartbeatCts.Cancel();
                    try
                    {
                        await heartbeatTask;
                    }
                    catch
                    {
                    }
                    try
                    {
                        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "refresh", CancellationToken.None);
                    }
                    catch
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log("[COINCALL][WS][ERROR] " + ex.Message, Enums.LogLevel.llBaselogic);
                await Task.Delay(TimeSpan.FromSeconds(FuturesWsReconnectDelaySeconds), ct);
            }
        }
    }

    private async Task RunSpotWsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri(SpotWsUrl), ct);
                _log($"[COINCALL][WS][OPEN] spot overview symbols={WantedSpotSymbols.Length}", Enums.LogLevel.llBaselogic);

                foreach (string symbol in WantedSpotSymbols)
                {
                    await SendJsonAsync(socket, new { sub = SpotOverviewChannel(symbol), id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, ct);
                }

                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Task heartbeatTask = SendHeartbeatLoopAsync(socket, heartbeatCts.Token);
                DateTime refreshAtUtc = DateTime.UtcNow.AddMinutes(FuturesWsRefreshMinutes);
                try
                {
                    while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open && DateTime.UtcNow < refreshAtUtc)
                    {
                        string? payload = await ReceiveTextAsync(socket, ct);
                        if (string.IsNullOrWhiteSpace(payload)) break;
                        await HandleSpotWsMessageAsync(socket, payload, ct);
                    }
                }
                finally
                {
                    heartbeatCts.Cancel();
                    try
                    {
                        await heartbeatTask;
                    }
                    catch
                    {
                    }
                    try
                    {
                        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "refresh", CancellationToken.None);
                    }
                    catch
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log("[COINCALL][WS][SPOT][ERROR] " + ex.Message, Enums.LogLevel.llBaselogic);
                await Task.Delay(TimeSpan.FromSeconds(FuturesWsReconnectDelaySeconds), ct);
            }
        }
    }

    private static async Task<List<SpotInstrument>> LoadSpotUniverseAsync(CoincallApiClient client, CancellationToken ct)
    {
        using var doc = await client.GetSpotInstrumentsAsync(null, ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return new List<SpotInstrument>();
        var list = new List<SpotInstrument>();
        foreach (var item in data.EnumerateArray())
        {
            string symbol = ReadString(item, "symbol");
            string baseCurrency = ReadString(item, "baseCoin");
            string quoteCurrency = ReadString(item, "quoteCoin");
            int enabled = ReadInt(item, "enableTrading");
            if (string.IsNullOrWhiteSpace(symbol) || enabled != 1) continue;
            if (!WantedSpotSet.Contains(symbol.Trim().ToUpperInvariant())) continue;
            list.Add(new SpotInstrument(symbol.Trim().ToUpperInvariant(), baseCurrency.Trim().ToUpperInvariant(), quoteCurrency.Trim().ToUpperInvariant()));
        }
        return list.OrderBy(x => x.Symbol, StringComparer.Ordinal).ToList();
    }

    private static async Task<List<FuturesInstrument>> LoadFuturesUniverseAsync(CoincallApiClient client, CancellationToken ct)
    {
        using var doc = await client.GetFuturesQuoteSymbolsAsync(ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return new List<FuturesInstrument>();
        var list = new List<FuturesInstrument>();
        foreach (var item in data.EnumerateArray())
        {
            string symbol = ReadString(item, "symbol").Trim().ToUpperInvariant();
            string displayName = ReadString(item, "displayName").Trim();
            string baseCurrency = ReadString(item, "baseToken");
            string quoteCurrency = ReadString(item, "quoteToken");
            string contractType = ReadString(item, "contractType");
            string deliveryKind = ReadString(item, "deliveryKind");
            string productType = string.IsNullOrWhiteSpace(deliveryKind) ? contractType : contractType + ":" + deliveryKind;
            decimal? markPrice = ReadDecimal(item, "markPrice");
            decimal? indexPrice = ReadDecimal(item, "indexPrice");
            if (string.IsNullOrWhiteSpace(symbol)) continue;
            string baseKey = baseCurrency.Trim().ToUpperInvariant();
            if (!WantedFuturesBaseSet.Contains(baseKey)) continue;
            list.Add(new FuturesInstrument(symbol, symbol, baseKey, quoteCurrency.Trim().ToUpperInvariant(), productType.Trim(), displayName, markPrice, indexPrice));
        }
        return list
            .GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<List<CoincallMinuteCandle>> LoadSpotMinuteCandlesAsync(CoincallApiClient client, SpotInstrument instrument, int limit, CancellationToken ct)
    {
        using var doc = await client.GetSpotKlinesAsync(instrument.Symbol, "1min", Math.Clamp(limit,1,1000), ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return new List<CoincallMinuteCandle>();
        return data.EnumerateArray()
            .Select(item => new
            {
                Ts = ReadUnixMs(item, "endTime"),
                Open = ReadDecimal(item, "open"),
                High = ReadDecimal(item, "high"),
                Low = ReadDecimal(item, "low"),
                Close = ReadDecimal(item, "close"),
                Volume = ReadDecimal(item, "volume"),
                QuoteVolume = ReadNullableDecimal(item, "volumeUsd")
            })
            .Where(x => x.Ts.HasValue && x.Open.HasValue && x.High.HasValue && x.Low.HasValue && x.Close.HasValue && x.Volume.HasValue)
            .OrderBy(x => x.Ts!.Value)
            .Select(row => new CoincallMinuteCandle
            {
                symbol = instrument.Symbol,
                baseCurrency = instrument.BaseCurrency,
                quoteCurrency = instrument.QuoteCurrency,
                minuteUtc = CoincallRepository.TruncateToMinuteUtc(row.Ts!.Value),
                open = row.Open!.Value,
                high = row.High!.Value,
                low = row.Low!.Value,
                close = row.Close!.Value,
                volume = row.Volume!.Value,
                quoteVolume = row.QuoteVolume
            })
            .ToList();
    }

    private static async Task<List<CoincallMinuteCandle>> LoadFuturesMinuteCandlesAsync(CoincallApiClient client, FuturesInstrument instrument, int limit, CancellationToken ct)
    {
        int boundedLimit = Math.Clamp(limit,1,1000);
        long bucketMs = 60_000L;
        long end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long start = end - (boundedLimit * bucketMs);
        using var doc = await client.GetFuturesUiChartKlinesAsync(instrument.Symbol, "m1", start, end, boundedLimit, ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0) return new List<CoincallMinuteCandle>();
        return data.EnumerateArray()
            .Select(item => new
            {
                Ts = ReadUnixMs(item, "time"),
                Open = ReadDecimal(item, "open"),
                High = ReadDecimal(item, "high"),
                Low = ReadDecimal(item, "low"),
                Close = ReadDecimal(item, "close"),
                Volume = ReadDecimal(item, "volume")
            })
            .Where(x => x.Ts.HasValue && x.Open.HasValue && x.High.HasValue && x.Low.HasValue && x.Close.HasValue && x.Volume.HasValue)
            .OrderBy(x => x.Ts!.Value)
            .Select(row => new CoincallMinuteCandle
            {
                symbol = instrument.Symbol,
                displayName = instrument.DisplayName,
                tickerId = instrument.TickerId,
                baseCurrency = instrument.BaseCurrency,
                quoteCurrency = instrument.QuoteCurrency,
                productType = instrument.ProductType,
                minuteUtc = CoincallRepository.TruncateToMinuteUtc(row.Ts!.Value),
                open = row.Open!.Value,
                high = row.High!.Value,
                low = row.Low!.Value,
                close = row.Close!.Value,
                volume = row.Volume!.Value
            })
            .ToList();
    }

    private static async Task<SpotCloseSnapshot> LoadSpotCloseSnapshotAsync(CoincallApiClient client, SpotInstrument instrument, CancellationToken ct)
    {
        using var doc = await client.GetSpotOrderBookAsync(instrument.Symbol, ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return new SpotCloseSnapshot(null, null);

        decimal? bestBid = ReadOrderBookPrice(data, "b");
        decimal? bestAsk = ReadOrderBookPrice(data, "a");
        return new SpotCloseSnapshot(bestBid, bestAsk);
    }

    private static async Task<FuturesCloseSnapshot> LoadFuturesCloseSnapshotAsync(CoincallApiClient client, FuturesInstrument instrument, CancellationToken ct)
    {
        using var doc = await client.GetFuturesOrderBookAsync(instrument.Symbol, ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return new FuturesCloseSnapshot(instrument.MarkPrice, instrument.IndexPrice, null, null);

        decimal? bestBid = ReadOrderBookPrice(data, "bids");
        decimal? bestAsk = ReadOrderBookPrice(data, "asks");
        return new FuturesCloseSnapshot(instrument.MarkPrice, instrument.IndexPrice, bestBid, bestAsk);
    }

    private static CoincallMinuteCandle BuildFuturesSnapshotOnlyMinute(
        FuturesInstrument instrument,
        DateTime minuteUtc,
        decimal? close,
        decimal? markPriceClose,
        decimal? indexPriceClose,
        decimal? bestBidClose,
        decimal? bestAskClose)
        => new CoincallMinuteCandle
        {
            symbol = instrument.Symbol,
            displayName = instrument.DisplayName,
            tickerId = instrument.TickerId,
            baseCurrency = instrument.BaseCurrency,
            quoteCurrency = instrument.QuoteCurrency,
            productType = instrument.ProductType,
            minuteUtc = minuteUtc,
            close = close,
            markPriceClose = markPriceClose,
            indexPriceClose = indexPriceClose,
            bestBidClose = bestBidClose,
            bestAskClose = bestAskClose
        };

    private static async Task SendHeartbeatLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            await Task.Delay(TimeSpan.FromSeconds(FuturesWsHeartbeatSeconds), ct);
            if (socket.State != WebSocketState.Open) break;
            await SendJsonAsync(socket, new { c = 11 }, ct);
        }
    }

    private async Task HandleFuturesWsMessageAsync(ClientWebSocket socket, string payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (root.TryGetProperty("ping", out var ping))
        {
            await SendRawAsync(socket, "{\"pong\":" + ping.GetRawText() + "}", ct);
            return;
        }

        int dt = ReadInt(root, "dt");
        if (dt == 43)
        {
            foreach (var trade in EnumerateWsPayload(root))
            {
                string symbol = ReadFirstString(trade, "s", "symbol").Trim().ToUpperInvariant();
                decimal? price = ReadFirstDecimal(trade, "price", "tradePrice", "dealPrice", "lastPrice", "px", "pr", "matchPrice", "mpr");
                DateTime tradeTsUtc = ReadFirstUnixMs(trade, "ts", "time", "tradeTime", "createdTime", "createTime") ?? DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(symbol) || !price.HasValue) continue;
                UpsertFuturesTradeWsState(symbol, tradeTsUtc, price.Value);
            }
            return;
        }

        if (dt == 32 || dt == 37 || dt == 58)
        {
            foreach (var book in EnumerateWsPayload(root))
            {
                string symbol = ReadFirstString(book, "s", "symbol").Trim().ToUpperInvariant();
                DateTime bookTsUtc = ReadFirstUnixMs(book, "ts", "time") ?? DateTime.UtcNow;
                decimal? bestBid = ReadOrderBookPrice(book, "bids");
                decimal? bestAsk = ReadOrderBookPrice(book, "asks");
                if (string.IsNullOrWhiteSpace(symbol)) continue;
                if (!bestBid.HasValue && !bestAsk.HasValue) continue;
                UpsertFuturesBookWsState(symbol, bookTsUtc, bestBid, bestAsk);
            }
        }
    }

    private async Task HandleSpotWsMessageAsync(ClientWebSocket socket, string payload, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        if (root.TryGetProperty("ping", out var ping))
        {
            await SendRawAsync(socket, "{\"pong\":" + ping.GetRawText() + "}", ct);
            return;
        }

        string channel = ReadString(root, "ch");
        if (string.IsNullOrWhiteSpace(channel)) return;
        string symbol = SymbolFromSpotOverviewChannel(channel);
        if (string.IsNullOrWhiteSpace(symbol) || !WantedSpotSet.Contains(symbol)) return;
        if (!root.TryGetProperty("tick", out var tick) || tick.ValueKind != JsonValueKind.Object) return;

        decimal? indexPrice = ReadFirstDecimal(tick, "i", "indexPrice", "index_price", "idx", "index");
        if (!indexPrice.HasValue || indexPrice.Value <= 0m) return;

        DateTime tsUtc = ReadFirstUnixMs(tick, "ts", "time", "t") ?? DateTime.UtcNow;
        UpsertSpotIndexWsState(symbol, tsUtc, indexPrice.Value);
    }

    private void UpsertSpotIndexWsState(string symbol, DateTime indexTsUtc, decimal indexPrice)
    {
        DateTime minuteUtc = CoincallRepository.TruncateToMinuteUtc(indexTsUtc);
        string key = SpotMinuteWsKey(symbol, minuteUtc);
        _spotMinuteWs.AddOrUpdate(
            key,
            _ => new SpotMinuteWsState(symbol, minuteUtc, indexPrice, indexTsUtc),
            (_, current) => indexTsUtc >= current.IndexTsUtc
                ? current with { IndexPriceClose = indexPrice, IndexTsUtc = indexTsUtc }
                : current);
    }

    private bool TryGetSpotMinuteWsState(string symbol, DateTime minuteUtc, out SpotMinuteWsState state)
        => _spotMinuteWs.TryGetValue(SpotMinuteWsKey(symbol, minuteUtc), out state!);

    private void PruneSpotWsState(DateTime oldestMinuteUtc)
    {
        foreach (var item in _spotMinuteWs)
        {
            if (item.Value.MinuteUtc < oldestMinuteUtc) _spotMinuteWs.TryRemove(item.Key, out _);
        }
    }

    private void UpsertFuturesTradeWsState(string symbol, DateTime tradeTsUtc, decimal price)
    {
        DateTime minuteUtc = CoincallRepository.TruncateToMinuteUtc(tradeTsUtc);
        string key = FuturesMinuteWsKey(symbol, minuteUtc);
        _futuresMinuteWs.AddOrUpdate(
            key,
            _ => new FuturesMinuteWsState(symbol, minuteUtc, price, tradeTsUtc, null, null, DateTime.MinValue),
            (_, current) => tradeTsUtc >= current.LastTradeTsUtc
                ? current with { LastTradeClose = price, LastTradeTsUtc = tradeTsUtc }
                : current);
    }

    private void UpsertFuturesBookWsState(string symbol, DateTime bookTsUtc, decimal? bestBid, decimal? bestAsk)
    {
        var filtered = FilterFuturesBidAskClose(symbol, bestBid, bestAsk);
        DateTime minuteUtc = CoincallRepository.TruncateToMinuteUtc(bookTsUtc);
        string key = FuturesMinuteWsKey(symbol, minuteUtc);
        _futuresMinuteWs.AddOrUpdate(
            key,
            _ => new FuturesMinuteWsState(symbol, minuteUtc, null, DateTime.MinValue, filtered.Bid, filtered.Ask, bookTsUtc),
            (_, current) => bookTsUtc >= current.BookTsUtc
                ? current with { BestBidClose = filtered.Bid ?? current.BestBidClose, BestAskClose = filtered.Ask ?? current.BestAskClose, BookTsUtc = bookTsUtc }
                : current);
    }

    private bool TryGetFuturesMinuteWsState(string symbol, DateTime minuteUtc, out FuturesMinuteWsState state)
        => _futuresMinuteWs.TryGetValue(FuturesMinuteWsKey(symbol, minuteUtc), out state!);

    private void PruneFuturesWsState(DateTime oldestMinuteUtc)
    {
        foreach (var item in _futuresMinuteWs)
        {
            if (item.Value.MinuteUtc < oldestMinuteUtc) _futuresMinuteWs.TryRemove(item.Key, out _);
        }
    }

    private static string FuturesMinuteWsKey(string symbol, DateTime minuteUtc)
        => symbol.Trim().ToUpperInvariant() + "|" + CoincallRepository.TruncateToMinuteUtc(minuteUtc).Ticks.ToString(CultureInfo.InvariantCulture);

    private static string SpotMinuteWsKey(string symbol, DateTime minuteUtc)
        => symbol.Trim().ToUpperInvariant() + "|" + CoincallRepository.TruncateToMinuteUtc(minuteUtc).Ticks.ToString(CultureInfo.InvariantCulture);

    private static string SpotOverviewChannel(string symbol)
        => "market." + symbol.Trim().ToUpperInvariant() + ".overviewv2";

    private static string SymbolFromSpotOverviewChannel(string channel)
    {
        const string prefix = "market.";
        const string suffix = ".overviewv2";
        string normalized = (channel ?? string.Empty).Trim();
        if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return normalized.Substring(prefix.Length, normalized.Length - prefix.Length - suffix.Length).Trim().ToUpperInvariant();
    }

    private static IEnumerable<JsonElement> EnumerateWsPayload(JsonElement root)
    {
        if (!root.TryGetProperty("d", out var data)) yield break;
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray()) yield return item;
            yield break;
        }
        if (data.ValueKind == JsonValueKind.Object) yield return data;
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.Count > 0) stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken ct)
        => SendRawAsync(socket, JsonSerializer.Serialize(payload), ct);

    private static Task SendRawAsync(ClientWebSocket socket, string payload, CancellationToken ct)
        => socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, ct);

    private static async Task<int> ProcessInParallelAsync<T>(IReadOnlyCollection<T> items, int parallelism, Func<T, Task<bool>> worker, CancellationToken ct)
    {
        var gate = new SemaphoreSlim(Math.Max(1, parallelism));
        var results = new ConcurrentBag<bool>();
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(ct);
            try
            {
                results.Add(await worker(item));
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();
        await Task.WhenAll(tasks);
        return results.Count(x => x);
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

    private static int ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
        return int.TryParse(ReadString(root, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
        return decimal.TryParse(ReadString(root, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static decimal? ReadNullableDecimal(JsonElement root, string name) => ReadDecimal(root, name);

    private static decimal? ReadFirstDecimal(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadDecimal(root, name);
            if (value.HasValue) return value;
        }
        return null;
    }

    private static DateTime? ReadUnixMs(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        long ms;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        return long.TryParse(ReadString(root, name), NumberStyles.Any, CultureInfo.InvariantCulture, out ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime
            : null;
    }

    private static DateTime? ReadFirstUnixMs(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadUnixMs(root, name);
            if (value.HasValue) return value.Value;
        }
        return null;
    }

    private static string ReadFirstString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            string value = ReadString(root, name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return string.Empty;
    }

    private static decimal? ReadOrderBookPrice(JsonElement root, string sideName)
    {
        if (!root.TryGetProperty(sideName, out var side) || side.ValueKind != JsonValueKind.Array || side.GetArrayLength() == 0) return null;
        var top = side[0];
        if (top.ValueKind == JsonValueKind.Object) return ReadFirstDecimal(top, "price", "p", "px", "pr");
        if (top.ValueKind == JsonValueKind.Array && top.GetArrayLength() > 0)
        {
            var priceEl = top[0];
            if (priceEl.ValueKind == JsonValueKind.Number && priceEl.TryGetDecimal(out var n)) return n;
            if (priceEl.ValueKind == JsonValueKind.String && decimal.TryParse(priceEl.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return null;
    }

    private static async Task DelayUntilNextCloseSnapshotAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;
        DateTime target = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc)
            .AddMinutes(1)
            .AddSeconds(CloseSnapshotLagSeconds);
        if (target <= now) target = target.AddMinutes(1);
        await Task.Delay(target - now, ct);
    }
}
