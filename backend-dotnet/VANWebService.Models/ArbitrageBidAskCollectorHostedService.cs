using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;

namespace VANWebService.Models;

public sealed class ArbitrageBidAskCollectorHostedService : BackgroundService
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    private readonly ArbitrageBidAskRepository _repo;
    private readonly Enums.LogAction _log;

    public ArbitrageBidAskCollectorHostedService(ArbitrageBidAskRepository repo, Enums.LogAction log)
    {
        _repo = repo;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await _repo.EnsureSchemaAsync(stoppingToken); } catch { }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var watches = await _repo.GetActiveWatchesAsync(stoppingToken);
                foreach (var watch in watches)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    await TryCollectAsync(watch.exchangeId, watch.apiSymbol, watch.symbol, stoppingToken);
                    await Task.Delay(120, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                try { _log?.Invoke("ArbitrageBidAskCollector loop failed: " + ex.Message, Enums.LogLevel.llExceptions); } catch { }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch { }
        }
    }

    public async Task<bool> TryCollectAsync(string exchangeId, string apiSymbol, string symbol, CancellationToken ct = default)
    {
        try
        {
            var snapshot = await FetchTopOfBookAsync(exchangeId, apiSymbol, ct);
            if (snapshot == null) return false;
            var minute = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, DateTime.UtcNow.Hour, DateTime.UtcNow.Minute, 0, DateTimeKind.Utc);
            await _repo.UpsertMinuteAsync(exchangeId, apiSymbol, minute, snapshot.Value.bid, snapshot.Value.ask, snapshot.Value.source, ct);
            return true;
        }
        catch (Exception ex)
        {
            await _repo.SetWatchErrorAsync(exchangeId, apiSymbol, ex.Message, ct);
            return false;
        }
    }

    private static async Task<(decimal bid, decimal ask, string source)?> FetchTopOfBookAsync(string exchangeId, string apiSymbol, CancellationToken ct)
    {
        string url = BuildUrl(exchangeId, apiSymbol);
        if (string.IsNullOrWhiteSpace(url)) return null;
        using var res = await Http.GetAsync(url, ct);
        string text = await res.Content.ReadAsStringAsync(ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var book = ParseBook(exchangeId, root);
        if (book == null || book.Value.bid <= 0 || book.Value.ask <= 0) return null;
        return (book.Value.bid, book.Value.ask, url);
    }

    private static string BuildUrl(string exchangeId, string apiSymbol)
    {
        string s = Uri.EscapeDataString(apiSymbol ?? "");
        return exchangeId switch
        {
            "binance-futures" => "https://fapi.binance.com/fapi/v1/depth?symbol=" + s + "&limit=5",
            "binance-spot" => "https://api.binance.com/api/v3/depth?symbol=" + s + "&limit=5",
            "okx-swap" or "okx-futures" or "okx-spot" => "https://www.okx.com/api/v5/market/books?instId=" + s + "&sz=5",
            "bybit-linear" => "https://api.bybit.com/v5/market/orderbook?category=linear&symbol=" + s + "&limit=1",
            "bybit-spot" => "https://api.bybit.com/v5/market/orderbook?category=spot&symbol=" + s + "&limit=1",
            "bybit-inverse" => "https://api.bybit.com/v5/market/orderbook?category=inverse&symbol=" + s + "&limit=1",
            "deribit-futures" => "https://www.deribit.com/api/v2/public/get_order_book?instrument_name=" + s + "&depth=1",
            "coincall-spot" => "https://api.coincall.com/open/spot/market/orderbook?symbol=" + s,
            "mexc-futures" => "https://contract.mexc.com/api/v1/contract/depth/" + s + "?limit=5",
            "mexc-spot" => "https://api.mexc.com/api/v3/depth?symbol=" + s + "&limit=5",
            _ => ""
        };
    }

    private static (decimal bid, decimal ask)? ParseBook(string exchangeId, JsonElement root)
    {
        if (exchangeId.StartsWith("binance-", StringComparison.OrdinalIgnoreCase) || exchangeId == "mexc-spot")
        {
            return ParseBidsAsks(root, "bids", "asks");
        }
        if (exchangeId.StartsWith("okx-", StringComparison.OrdinalIgnoreCase))
        {
            if (TryFirst(root, "data", out var item)) return ParseBidsAsks(item, "bids", "asks");
            return null;
        }
        if (exchangeId.StartsWith("bybit-", StringComparison.OrdinalIgnoreCase))
        {
            if (root.TryGetProperty("result", out var result)) return ParseBidsAsks(result, "b", "a");
            return null;
        }
        if (exchangeId == "deribit-futures")
        {
            if (root.TryGetProperty("result", out var result)
                && TryDecimal(result, "best_bid_price", out var bid)
                && TryDecimal(result, "best_ask_price", out var ask)) return (bid, ask);
            return null;
        }
        if (exchangeId == "mexc-futures")
        {
            if (root.TryGetProperty("data", out var data)) return ParseBidsAsks(data, "bids", "asks");
            return null;
        }
        if (exchangeId == "coincall-spot")
        {
            JsonElement source = root;
            if (root.TryGetProperty("data", out var data)) source = data;
            return ParseBidsAsks(source, "bids", "asks") ?? ParseBidsAsks(source, "bid", "ask") ?? ParseBidsAsks(source, "b", "a");
        }
        return null;
    }

    private static (decimal bid, decimal ask)? ParseBidsAsks(JsonElement root, string bidsName, string asksName)
    {
        if (!TryFirst(root, bidsName, out var bidRow) || !TryFirst(root, asksName, out var askRow)) return null;
        if (!TryFirstPrice(bidRow, out var bid) || !TryFirstPrice(askRow, out var ask)) return null;
        return (bid, ask);
    }

    private static bool TryFirst(JsonElement root, string name, out JsonElement item)
    {
        item = default;
        if (!root.TryGetProperty(name, out var arr)) return false;
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return false;
        item = arr[0];
        return true;
    }

    private static bool TryFirstPrice(JsonElement row, out decimal value)
    {
        value = 0m;
        if (row.ValueKind == JsonValueKind.Array && row.GetArrayLength() > 0) return TryElementDecimal(row[0], out value);
        if (row.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "price", "p", "px", "bidPrice", "askPrice" })
            {
                if (TryDecimal(row, name, out value)) return true;
            }
        }
        return TryElementDecimal(row, out value);
    }

    private static bool TryDecimal(JsonElement root, string name, out decimal value)
    {
        value = 0m;
        return root.TryGetProperty(name, out var el) && TryElementDecimal(el, out value);
    }

    private static bool TryElementDecimal(JsonElement el, out decimal value)
    {
        value = 0m;
        if (el.ValueKind == JsonValueKind.Number) return el.TryGetDecimal(out value);
        if (el.ValueKind == JsonValueKind.String) return decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        return false;
    }
}
