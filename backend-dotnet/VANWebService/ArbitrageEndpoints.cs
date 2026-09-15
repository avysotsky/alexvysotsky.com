using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VANWebService.Auth;
using VANWebService.Models;

namespace VANWebService;

public static class ArbitrageEndpoints
{
    private static readonly HttpClient UpstreamHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
    public static void MapArbitrageEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/arbitrage/bidask/minute", (HttpContext context, ArbitrageBidAskRepository repo, ArbitrageBidAskCollectorHostedService collector) =>
            HandleBidAskMinuteAsync(context, repo, collector, requireAdmin:true));
        app.MapGet("/api/public/arbitrage/bidask/minute", (HttpContext context, ArbitrageBidAskRepository repo, ArbitrageBidAskCollectorHostedService collector) =>
            HandleBidAskMinuteAsync(context, repo, collector, requireAdmin:false));
        app.MapGet("/api/admin/arbitrage/robots", (HttpContext context, ArbitrageRobotManager robots) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            return Results.Json(new { ok = true, robots = robots.List() });
        });
        app.MapPost("/api/admin/arbitrage/robots/ensure", (HttpContext context, ArbitrageRobotManager robots, ArbitrageRobotRequest request) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            return Results.Json(new { ok = true, robot = robots.Ensure(request) });
        });
        app.MapPost("/api/admin/arbitrage/robots/start", (HttpContext context, ArbitrageRobotManager robots, ArbitrageRobotRequest request) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            return Results.Json(new { ok = true, robot = robots.Start(request) });
        });
        app.MapPost("/api/admin/arbitrage/robots/stop", (HttpContext context, ArbitrageRobotManager robots, ArbitrageRobotRequest request) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var robot = robots.Stop(request);
            return robot == null ? Results.NotFound(new { ok = false, message = "Robot instance not found" }) : Results.Json(new { ok = true, robot });
        });
        app.MapGet("/api/public/arbitrage/exchange-proxy", async (HttpRequest request) => await HandleExchangeProxyAsync(request.HttpContext));
    }


    private static async Task<IResult> HandleExchangeProxyAsync(HttpContext context)
    {
        string exchangeId = ReadString(context, "exchangeId", "").Trim();
        string type = ReadString(context, "type", "").Trim();
        string url = BuildExchangeProxyUrl(context, exchangeId, type);
        if (string.IsNullOrWhiteSpace(url)) return Results.BadRequest(new { message = "Unsupported arbitrage exchange proxy request" });
        if (!UpstreamHttp.DefaultRequestHeaders.UserAgent.Any())
        {
            UpstreamHttp.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VAN-Arbitrage", "1.0"));
            UpstreamHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
        using var res = await UpstreamHttp.GetAsync(url, context.RequestAborted);
        string text = await res.Content.ReadAsStringAsync(context.RequestAborted);
        if (!res.IsSuccessStatusCode) return Results.StatusCode((int)res.StatusCode);
        if (string.IsNullOrEmpty(text))
        {
            return Results.Json(new
            {
                message = "Empty upstream response",
                upstreamStatus = (int)res.StatusCode,
                upstreamContentType = res.Content.Headers.ContentType?.ToString(),
                upstreamContentLength = res.Content.Headers.ContentLength,
                upstreamUrl = url
            }, statusCode: 502);
        }
        return Results.Content(text, "application/json");
    }

    private static string BuildExchangeProxyUrl(HttpContext context, string exchangeId, string type)
    {
        string symbol = Uri.EscapeDataString(ReadString(context, "symbol", ReadString(context, "apiSymbol", "")).Trim());
        string interval = Uri.EscapeDataString(ReadString(context, "interval", "1m").Trim());
        string limit = ReadInt(context, "limit", 500, 1, 5000).ToString(CultureInfo.InvariantCulture);
        string startTime = ReadLongString(context, "startTime");
        string endTime = ReadLongString(context, "endTime");
        string start = ReadLongString(context, "start");
        string end = ReadLongString(context, "end");
        string after = ReadLongString(context, "after");
        string before = ReadLongString(context, "before");
        string cursor = Uri.EscapeDataString(ReadString(context, "cursor", "").Trim());
        string currency = Uri.EscapeDataString(ReadString(context, "currency", "").Trim().ToUpperInvariant());
        return (exchangeId, type) switch
        {
            ("binance-futures", "instruments") => "https://fapi.binance.com/fapi/v1/exchangeInfo",
            ("binance-spot", "instruments") => "https://api.binance.com/api/v3/exchangeInfo",
            ("binance-futures", "candles") => "https://fapi.binance.com/fapi/v1/klines?symbol=" + symbol + "&interval=" + interval + "&startTime=" + startTime + "&endTime=" + endTime + "&limit=" + limit,
            ("binance-spot", "candles") => "https://api.binance.com/api/v3/klines?symbol=" + symbol + "&interval=" + interval + "&startTime=" + startTime + "&endTime=" + endTime + "&limit=" + limit,
            ("binance-futures", "orderbook") => "https://fapi.binance.com/fapi/v1/depth?symbol=" + symbol + "&limit=" + NormalizeDepthLimit(limit),
            ("binance-spot", "orderbook") => "https://api.binance.com/api/v3/depth?symbol=" + symbol + "&limit=" + NormalizeDepthLimit(limit),
            ("okx-swap", "instruments") => "https://www.okx.com/api/v5/public/instruments?instType=SWAP",
            ("okx-futures", "instruments") => "https://www.okx.com/api/v5/public/instruments?instType=FUTURES",
            ("okx-spot", "instruments") => "https://www.okx.com/api/v5/public/instruments?instType=SPOT",
            ("okx-swap", "candles") or ("okx-futures", "candles") or ("okx-spot", "candles") => "https://www.okx.com/api/v5/market/candles?instId=" + symbol + "&bar=" + interval + (string.IsNullOrWhiteSpace(after) ? "" : "&after=" + after) + (string.IsNullOrWhiteSpace(before) ? "" : "&before=" + before) + "&limit=" + limit,
            ("okx-swap", "orderbook") or ("okx-futures", "orderbook") or ("okx-spot", "orderbook") => "https://www.okx.com/api/v5/market/books?instId=" + symbol + "&sz=" + NormalizeDepthLimit(limit),
            ("deribit-futures", "instruments") when !string.IsNullOrWhiteSpace(currency) => "https://www.deribit.com/api/v2/public/get_instruments?currency=" + currency + "&kind=future&expired=false",
            ("deribit-futures", "candles") => "https://www.deribit.com/api/v2/public/get_tradingview_chart_data?instrument_name=" + symbol + "&start_timestamp=" + start + "&end_timestamp=" + end + "&resolution=" + interval,
            ("deribit-futures", "orderbook") => "https://www.deribit.com/api/v2/public/get_order_book?instrument_name=" + symbol + "&depth=" + NormalizeDepthLimit(limit),
            ("bybit-linear", "instruments") => "https://api.bybit.com/v5/market/instruments-info?category=linear&limit=1000" + (string.IsNullOrWhiteSpace(cursor) ? "" : "&cursor=" + cursor),
            ("bybit-spot", "instruments") => "https://api.bybit.com/v5/market/instruments-info?category=spot&limit=1000" + (string.IsNullOrWhiteSpace(cursor) ? "" : "&cursor=" + cursor),
            ("bybit-inverse", "instruments") => "https://api.bybit.com/v5/market/instruments-info?category=inverse&limit=1000" + (string.IsNullOrWhiteSpace(cursor) ? "" : "&cursor=" + cursor),
            ("bybit-linear", "candles") => "https://api.bybit.com/v5/market/kline?category=linear&symbol=" + symbol + "&interval=" + interval + "&start=" + start + "&end=" + end + "&limit=" + limit,
            ("bybit-spot", "candles") => "https://api.bybit.com/v5/market/kline?category=spot&symbol=" + symbol + "&interval=" + interval + "&start=" + start + "&end=" + end + "&limit=" + limit,
            ("bybit-inverse", "candles") => "https://api.bybit.com/v5/market/kline?category=inverse&symbol=" + symbol + "&interval=" + interval + "&start=" + start + "&end=" + end + "&limit=" + limit,
            ("bybit-linear", "orderbook") => "https://api.bybit.com/v5/market/orderbook?category=linear&symbol=" + symbol + "&limit=" + NormalizeDepthLimit(limit),
            ("bybit-spot", "orderbook") => "https://api.bybit.com/v5/market/orderbook?category=spot&symbol=" + symbol + "&limit=" + NormalizeDepthLimit(limit),
            ("bybit-inverse", "orderbook") => "https://api.bybit.com/v5/market/orderbook?category=inverse&symbol=" + symbol + "&limit=" + NormalizeDepthLimit(limit),
            ("coincall-spot", "instruments") => "https://api.coincall.com/open/spot/market/instruments",
            ("coincall-spot", "candles") => "https://api.coincall.com/open/spot/market/klines?symbol=" + symbol + "&interval=" + interval + "&limit=" + limit,
            ("coincall-spot", "orderbook") => "https://api.coincall.com/open/spot/market/orderbook?symbol=" + symbol,
            ("coincall-futures", "orderbook") => "https://api.coincall.com/open/futures/order/orderbook/v1/" + symbol,
            _ => ""
        };
    }

    private static string ReadLongString(HttpContext context, string name)
    {
        string value = context.Request.Query[name].ToString();
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 ? parsed.ToString(CultureInfo.InvariantCulture) : "";
    }

    private static string NormalizeDepthLimit(string value)
    {
        if (!int.TryParse(value, out var parsed)) parsed = 10;
        int[] allowed = { 1, 5, 10, 20, 25, 50, 100, 200, 500, 1000, 5000 };
        int best = allowed[0];
        foreach (int item in allowed)
        {
            if (item <= parsed) best = item;
        }
        return best.ToString(CultureInfo.InvariantCulture);
    }

    private static async Task<IResult> HandleBidAskMinuteAsync(HttpContext context, ArbitrageBidAskRepository repo, ArbitrageBidAskCollectorHostedService collector, bool requireAdmin)
    {
        if (requireAdmin && !TryRequireAdmin(context, out var fail)) return fail!;
        string exchangeId = ReadString(context, "exchangeId", "");
        string apiSymbol = ReadString(context, "apiSymbol", "");
        string symbol = ReadString(context, "symbol", apiSymbol);
        int limit = ReadInt(context, "limit", 1440, 1, 20000);
        if (string.IsNullOrWhiteSpace(exchangeId) || string.IsNullOrWhiteSpace(apiSymbol))
        {
            return Results.BadRequest(new { message = "exchangeId and apiSymbol are required" });
        }
        exchangeId = exchangeId.Trim();
        apiSymbol = apiSymbol.Trim();
        symbol = symbol.Trim();
        await repo.RegisterWatchAsync(exchangeId, apiSymbol, symbol, context.RequestAborted);
        bool collectedNow = await collector.TryCollectAsync(exchangeId, apiSymbol, symbol, context.RequestAborted);
        var rows = await repo.GetRowsAsync(exchangeId, apiSymbol, limit, null, context.RequestAborted);
        return Results.Json(new
        {
            ok = true,
            exchangeId,
            apiSymbol,
            symbol,
            collectedNow,
            rows = rows.Select(r => new
            {
                time = new DateTimeOffset(DateTime.SpecifyKind(r.minuteUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                minuteUtc = DateTime.SpecifyKind(r.minuteUtc, DateTimeKind.Utc),
                bestBidClose = r.bestBidClose,
                bestAskClose = r.bestAskClose,
                bidAskMedian = r.bestBidClose.HasValue && r.bestAskClose.HasValue ? (r.bestBidClose.Value + r.bestAskClose.Value) / 2m : (decimal?)null,
                source = r.source
            }).ToList()
        });
    }

    private static string ReadString(HttpContext context, string name, string fallback)
    {
        string value = context.Request.Query[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int ReadInt(HttpContext context, string name, int fallback, int min, int max)
    {
        string value = context.Request.Query[name].ToString();
        if (!int.TryParse(value, out var parsed)) parsed = fallback;
        return Math.Clamp(parsed, min, max);
    }

    private static bool TryRequireAdmin(HttpContext context, out IResult? fail)
    {
        var session = UserSessionTools.GetUserSession(context);
        if (session == null)
        {
            fail = Results.Unauthorized();
            return false;
        }
        if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            fail = Results.Forbid();
            return false;
        }
        fail = null;
        return true;
    }
}
