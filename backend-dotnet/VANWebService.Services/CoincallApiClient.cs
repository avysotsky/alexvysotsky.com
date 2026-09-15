using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VANWebService.Models;

namespace VANWebService.Services;

public sealed class CoincallApiClient
{
    private const string BaseUrl = "https://api.coincall.com";
    private const string FuturesWebSocketVerifyPath = "/users/self/verify";
    private const string FuturesWebSocketBaseUrl = "wss://ws.coincall.com/futures";
    private const string SpotWebSocketPrivateBaseUrl = "wss://ws.coincall.com/spot/ws/private";
    private const string DefaultTsDiff = "3000";
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly HttpClient UiGuestHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly SemaphoreSlim UiGuestAuthLock = new SemaphoreSlim(1, 1);
    private static string? _uiGuestUuid;
    private static string? _uiGuestKey;
    private static string? _uiGuestToken;
    private static string? _uiGuestTsDiff;
    private static DateTime _uiGuestAuthExpiresUtc;
    private readonly CoincallAccountRecord _account;

    static CoincallApiClient()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VANWebService/1.0");
        UiGuestHttp.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
        UiGuestHttp.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        UiGuestHttp.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.coincall.com");
        UiGuestHttp.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.coincall.com/");
    }

    public CoincallApiClient(CoincallAccountRecord account) { _account = account; }

    public Task<JsonDocument> GetAccountSummaryAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/account/summary/v1", null, null, ct);
    public Task<JsonDocument> GetAccountTransferRecordsAsync(CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/account/sysTransferRecords/v1", null, null, ct);
    public Task<JsonDocument> GetSpotInstrumentsAsync(string? symbol = null, CancellationToken ct = default) => SendPublicAsync("/open/spot/market/instruments" + Query(new Dictionary<string, string?> { ["symbol"] = symbol }), ct);
    public Task<JsonDocument> GetSpotKlinesAsync(string symbol, string interval, int limit, CancellationToken ct = default) => SendPublicAsync("/open/spot/market/klines" + Query(new Dictionary<string, string?> { ["symbol"] = symbol, ["interval"] = interval, ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture) }), ct);
    public Task<JsonDocument> GetSpotOrderBookAsync(string symbol, CancellationToken ct = default) => SendPublicAsync("/open/spot/market/orderbook" + Query(new Dictionary<string, string?> { ["symbol"] = symbol }), ct);
    public Task<JsonDocument> GetFuturesInstrumentsAsync(CancellationToken ct = default) => SendPublicAsync("/open/futures/market/instruments/v1", ct);
    public Task<JsonDocument> GetFuturesPublicFundingRateAsync(string? symbol = null, CancellationToken ct = default) => SendPublicAsync("/open/public/fundingRate/v1" + Query(new Dictionary<string, string?> { ["symbol"] = symbol }), ct);
    public Task<JsonDocument> GetFuturesQuoteSymbolsAsync(CancellationToken ct = default) => SendUiGuestAsync("/futures/market/quote/symbols/v1", null, ct);
    public Task<JsonDocument> GetFuturesKlinesAsync(string symbol, string period, long start, long end, int limit = 1, CancellationToken ct = default) => SendPublicAsync("/open/futures/market/kline/history/v2/" + Uri.EscapeDataString(symbol) + Query(new Dictionary<string, string?> { ["period"] = period, ["start"] = start > 0 ? start.ToString(CultureInfo.InvariantCulture) : null, ["end"] = end > 0 ? end.ToString(CultureInfo.InvariantCulture) : null, ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture) }), ct);
    public Task<JsonDocument> GetFuturesUiChartKlinesAsync(string symbol, string period, long start, long end, int limit = 300, CancellationToken ct = default) => SendPublicAsync("/open/futures/market/kline/history/v2/" + Uri.EscapeDataString(symbol) + Query(new Dictionary<string, string?> { ["period"] = period, ["start"] = start > 0 ? start.ToString(CultureInfo.InvariantCulture) : null, ["end"] = end > 0 ? end.ToString(CultureInfo.InvariantCulture) : null, ["limit"] = Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture) }), ct);
    public Task<JsonDocument> GetFuturesOrderBookAsync(string symbol, CancellationToken ct = default) => SendPublicAsync("/open/futures/order/orderbook/v1/" + Uri.EscapeDataString(symbol), ct);
    public Task<JsonDocument> GetFuturesPositionsAsync(string? symbol = null, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/futures/position/get/v1", new Dictionary<string, string?> { ["symbol"] = symbol }, null, ct);
    public Task<JsonDocument> GetFuturesLeverageAsync(string symbol, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/futures/leverage/current/v1", new Dictionary<string, string?> { ["symbol"] = symbol }, null, ct);
    public Task<JsonDocument> GetFuturesOpenOrdersAsync(string? symbol = null, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/futures/order/pending/v1", new Dictionary<string, string?> { ["symbol"] = symbol }, null, ct);
    public Task<JsonDocument> GetFuturesOrderHistoryAsync(int pageSize = 100, long fromId = 0, long startTime = 0, long endTime = 0, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/futures/order/history/v1", new Dictionary<string, string?> { ["pageSize"] = Math.Clamp(pageSize, 1, 100).ToString(CultureInfo.InvariantCulture), ["fromId"] = fromId > 0 ? fromId.ToString(CultureInfo.InvariantCulture) : null, ["startTime"] = startTime > 0 ? startTime.ToString(CultureInfo.InvariantCulture) : null, ["endTime"] = endTime > 0 ? endTime.ToString(CultureInfo.InvariantCulture) : null }, null, ct);
    public Task<JsonDocument> GetFuturesTradeHistoryAsync(int pageSize = 100, long fromId = 0, long startTime = 0, long endTime = 0, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/futures/trade/history/v1", new Dictionary<string, string?> { ["pageSize"] = Math.Clamp(pageSize, 1, 100).ToString(CultureInfo.InvariantCulture), ["fromId"] = fromId > 0 ? fromId.ToString(CultureInfo.InvariantCulture) : null, ["startTime"] = startTime > 0 ? startTime.ToString(CultureInfo.InvariantCulture) : null, ["endTime"] = endTime > 0 ? endTime.ToString(CultureInfo.InvariantCulture) : null }, null, ct);
    public Task<JsonDocument> GetFuturesFundingHistoryAsync(string symbol, int pageSize = 50, int page = 1, long startTime = 0, long endTime = 0, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/settle/future/record/v1", new Dictionary<string, string?> { ["symbol"] = symbol, ["pageSize"] = Math.Clamp(pageSize, 1, 50).ToString(CultureInfo.InvariantCulture), ["page"] = page.ToString(CultureInfo.InvariantCulture), ["startTime"] = startTime > 0 ? startTime.ToString(CultureInfo.InvariantCulture) : null, ["endTime"] = endTime > 0 ? endTime.ToString(CultureInfo.InvariantCulture) : null }, null, ct);
    public Task<JsonDocument> GetFuturesPremiumIndexHistoryAsync(string symbol, int pageSize = 1, int page = 1, long startTime = 0, long endTime = 0, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/futures/market/premiumIndexHistory/v1", new Dictionary<string, string?> { ["symbol"] = symbol, ["pageSize"] = Math.Clamp(pageSize, 1, 200).ToString(CultureInfo.InvariantCulture), ["page"] = page.ToString(CultureInfo.InvariantCulture), ["startTime"] = startTime > 0 ? startTime.ToString(CultureInfo.InvariantCulture) : null, ["endTime"] = endTime > 0 ? endTime.ToString(CultureInfo.InvariantCulture) : null }, null, ct);
    public async Task<CoincallFuturesWebSocketAuth> BuildFuturesWebSocketAuthAsync(CancellationToken ct = default)
    {
        var session = await EnsureUiGuestAuthAsync(ct);
        var query = new Dictionary<string, string?>
        {
            ["code"] = "10",
            ["uuid"] = session.Uuid,
            ["Authorization"] = "Bearer " + session.Token
        };
        return new CoincallFuturesWebSocketAuth(FuturesWebSocketBaseUrl + Query(query), session.Uuid, string.Empty, DateTime.UtcNow.AddMinutes(9));
    }
    public CoincallFuturesWebSocketAuth BuildAccountFuturesWebSocketAuth()
    {
        var auth = BuildAccountPrivateWebSocketSignature("futures");
        // CoinCall futures private WS rejects URL-encoded apiKey/uuid values. Keep the documented raw order.
        string url = FuturesWebSocketBaseUrl
            + "?code=10"
            + "&uuid=" + auth.ApiKey
            + "&ts=" + auth.Ts
            + "&sign=" + auth.Sign
            + "&apiKey=" + auth.ApiKey;
        return new CoincallFuturesWebSocketAuth(url, auth.ApiKey, auth.Ts, DateTime.UtcNow.AddSeconds(25));
    }

    public CoincallFuturesWebSocketAuth BuildAccountSpotWebSocketAuth()
    {
        var auth = BuildAccountPrivateWebSocketSignature("spot");
        string url = SpotWebSocketPrivateBaseUrl
            + "?ts=" + auth.Ts
            + "&sign=" + auth.Sign
            + "&apiKey=" + auth.ApiKey;
        return new CoincallFuturesWebSocketAuth(url, auth.ApiKey, auth.Ts, DateTime.UtcNow.AddSeconds(25));
    }

    private (string ApiKey, string Ts, string Sign) BuildAccountPrivateWebSocketSignature(string market)
    {
        string apiKey = CleanSecretPart(_account.apiKey);
        string apiSecret = CleanSecretPart(_account.apiSecret);
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            throw new InvalidOperationException("CoinCall API key and secret are required for " + market + " websocket auth.");

        string ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string signPayload = "GET" + FuturesWebSocketVerifyPath + "?apiKey=" + apiKey + "&ts=" + ts;
        string sign;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret)))
        {
            sign = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signPayload))).ToUpperInvariant();
        }
        return (apiKey, ts, sign);
    }
    public Task<JsonDocument> GetFuturesTradeContractConfigAsync(string symbol, CancellationToken ct = default) => SendUiGuestAsync("/futures/trade/public/contractConfig/v3", new Dictionary<string, string?> { ["symbol"] = symbol }, ct);
    public Task<JsonDocument> GetFuturesTradeLadderConfigAsync(string symbol, CancellationToken ct = default) => SendUiGuestAsync("/futures/trade/public/ladderConfig/v3", new Dictionary<string, string?> { ["symbol"] = symbol }, ct);
    public Task<JsonDocument> GetOptionInstrumentsAsync(string baseCurrency, CancellationToken ct = default) => SendPublicAsync("/open/option/getInstruments/" + Uri.EscapeDataString(baseCurrency), ct);
    public Task<JsonDocument> GetOptionChainAsync(string index, long? endTime = null, CancellationToken ct = default) => SendPublicAsync("/open/option/get/v1/" + Uri.EscapeDataString(index) + Query(new Dictionary<string, string?> { ["endTime"] = endTime.HasValue && endTime.Value > 0 ? endTime.Value.ToString(CultureInfo.InvariantCulture) : null }), ct);
    public Task<JsonDocument> GetSpotOpenOrdersAsync(string? symbol = null, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/spot/trade/orders/v1", new Dictionary<string, string?> { ["symbol"] = symbol }, null, ct);
    public Task<JsonDocument> GetSpotOrderHistoryAsync(string? symbol = null, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/spot/trade/allorders/v1", new Dictionary<string, string?> { ["symbol"] = symbol, ["limit"] = "100" }, null, ct);
    public Task<JsonDocument> GetSpotFillsAsync(string? symbol = null, CancellationToken ct = default) => SendAsync(HttpMethod.Get, "/open/spot/trade/fills/v1", new Dictionary<string, string?> { ["symbol"] = symbol, ["limit"] = "100" }, null, ct);
    public Task<JsonDocument> PlaceSpotOrderAsync(object payload, CancellationToken ct = default)
    {
        var orderPayload = BuildSpotOrderPayload(payload);
        return SendAsync(HttpMethod.Post, "/open/spot/trade/order/v1", null, orderPayload, ct);
    }
    public Task<JsonDocument> PlaceFuturesOrderAsync(object payload, CancellationToken ct = default) => SendAsync(HttpMethod.Post, "/open/futures/order/create/v1", null, payload, ct);
    public Task<JsonDocument> SetFuturesLeverageAsync(string symbol, decimal leverage, CancellationToken ct = default) => SendAsync(HttpMethod.Post, "/open/futures/leverage/set/v1", null, new Dictionary<string, object> { ["symbol"] = symbol, ["leverage"] = leverage }, ct);
    public Task<JsonDocument> PlaceOptionOrderAsync(object payload, CancellationToken ct = default) => SendAsync(HttpMethod.Post, "/open/options/create/v1", null, payload, ct);
    public Task<JsonDocument> CancelSpotOrderAsync(object payload, CancellationToken ct = default)
    {
        var cancelPayload = BuildSpotCancelPayload(payload);
        return SendAsync(HttpMethod.Post, "/open/spot/trade/cancel/v1", cancelPayload, cancelPayload, ct);
    }
    public Task<JsonDocument> CancelFuturesOrderAsync(object payload, CancellationToken ct = default)
    {
        var cancelPayload = BuildFuturesCancelPayload(payload);
        return SendAsync(HttpMethod.Post, "/open/futures/order/cancel/v1", null, cancelPayload, ct);
    }
    public Task<JsonDocument> CancelOptionOrderAsync(object payload, CancellationToken ct = default) => throw new NotSupportedException("CoinCall options cancel endpoint was not confirmed in the public API docs.");

    public async Task<decimal> GetBtcUsdtLastPriceAsync(CancellationToken ct = default)
    {
        return await GetSpotUsdtLastPriceAsync("BTC", ct);
    }

    public async Task<decimal> GetSpotUsdtLastPriceAsync(string baseCurrency, CancellationToken ct = default)
    {
        string baseCcy = (baseCurrency ?? string.Empty).Trim().ToUpperInvariant();
        if (baseCcy == "USDT") return 1m;
        if (baseCcy is not ("BTC" or "ETH")) throw new InvalidOperationException("CoinCall " + baseCcy + "USDT price source is not confirmed");
        using var doc = await GetSpotKlinesAsync(baseCcy + "USDT", "1min", 1, ct);
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            var row = data[data.GetArrayLength() - 1];
            if (row.TryGetProperty("close", out var close))
            {
                if (close.ValueKind == JsonValueKind.Number && close.TryGetDecimal(out var n)) return n;
                if (close.ValueKind == JsonValueKind.String && decimal.TryParse(close.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
            }
        }
        throw new InvalidOperationException("CoinCall " + baseCcy + "USDT price is unavailable");
    }


    private static Dictionary<string, string?> BuildSpotOrderPayload(object payload)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("CoinCall order payload must be an object");
        static string? Read(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value)) return null;
            string? text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        static string? TradeSide(string? value)
        {
            var v = (value ?? string.Empty).Trim().ToUpperInvariant();
            return v switch { "1" or "BUY" or "LONG" => "1", "2" or "SELL" or "SHORT" => "2", _ => null };
        }
        static string? TradeType(string? value, string? postOnly)
        {
            if (string.Equals(postOnly, "true", StringComparison.OrdinalIgnoreCase)) return "3";
            var v = (value ?? string.Empty).Trim().ToUpperInvariant().Replace("-", "_").Replace(" ", "_");
            return v switch { "1" or "LIMIT" => "1", "2" or "MARKET" => "2", "3" or "POST_ONLY" or "POSTONLY" => "3", _ => null };
        }
        var result = new Dictionary<string, string?>();
        var symbol = Read(doc.RootElement, "symbol");
        var qty = Read(doc.RootElement, "qty") ?? Read(doc.RootElement, "quantity") ?? Read(doc.RootElement, "amount");
        var price = Read(doc.RootElement, "price");
        var tradeSide = TradeSide(Read(doc.RootElement, "tradeSide") ?? Read(doc.RootElement, "side"));
        var tradeType = TradeType(Read(doc.RootElement, "tradeType") ?? Read(doc.RootElement, "type") ?? Read(doc.RootElement, "orderType"), Read(doc.RootElement, "postOnly"));
        if (string.IsNullOrWhiteSpace(symbol)) throw new InvalidOperationException("CoinCall spot order requires symbol");
        if (string.IsNullOrWhiteSpace(qty)) throw new InvalidOperationException("CoinCall spot order requires qty");
        if (string.IsNullOrWhiteSpace(tradeSide)) throw new InvalidOperationException("CoinCall spot order requires side BUY/SELL");
        if (string.IsNullOrWhiteSpace(tradeType)) throw new InvalidOperationException("CoinCall spot order requires type LIMIT/MARKET/POST_ONLY");
        result["symbol"] = symbol;
        result["tradeSide"] = tradeSide;
        result["tradeType"] = tradeType;
        result["qty"] = qty;
        if (tradeType != "2")
        {
            if (string.IsNullOrWhiteSpace(price)) throw new InvalidOperationException("CoinCall limit/post-only order requires price");
            result["price"] = price;
        }
        return result;
    }

    private static Dictionary<string, string?> BuildSpotCancelPayload(object payload)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("CoinCall cancel payload must be an object");
        static string? Read(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value)) return null;
            string? text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        var clientOrderId = Read(doc.RootElement, "clientOrderId");
        if (!string.IsNullOrWhiteSpace(clientOrderId)) return new Dictionary<string, string?> { ["clientOrderId"] = clientOrderId };
        var orderId = Read(doc.RootElement, "orderId");
        if (!string.IsNullOrWhiteSpace(orderId)) return new Dictionary<string, string?> { ["orderId"] = orderId };
        throw new InvalidOperationException("CoinCall cancel requires clientOrderId or orderId");
    }

    private static Dictionary<string, string?> BuildFuturesCancelPayload(object payload)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("CoinCall futures cancel payload must be an object");
        static string? Read(JsonElement root, params string[] names)
        {
            foreach (var name in names)
            {
                if (!root.TryGetProperty(name, out var value)) continue;
                string? text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            return null;
        }

        var clientOrderId = Read(doc.RootElement, "clientOrderId", "clientOid", "clOrdId") ?? FindDeepText(doc.RootElement, "clientOrderId", "clientOid", "clOrdId");
        var orderId = Read(doc.RootElement, "orderId", "ordId", "order_id", "id", "oid") ?? FindDeepText(doc.RootElement, "orderId", "ordId", "order_id", "id", "oid") ?? Read(doc.RootElement, "data");
        if (!string.IsNullOrWhiteSpace(clientOrderId)) return new Dictionary<string, string?> { ["clientOrderId"] = clientOrderId };
        if (!string.IsNullOrWhiteSpace(orderId)) return new Dictionary<string, string?> { ["orderId"] = orderId };
        throw new InvalidOperationException("CoinCall futures cancel requires clientOrderId or orderId");
    }

    private static string? FindDeepText(JsonElement value, params string[] names)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (!value.TryGetProperty(name, out var direct)) continue;
                string? text = direct.ValueKind switch
                {
                    JsonValueKind.String => direct.GetString(),
                    JsonValueKind.Number => direct.GetRawText(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            foreach (var prop in value.EnumerateObject())
            {
                var nested = FindDeepText(prop.Value, names);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var nested = FindDeepText(item, names);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return null;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, IReadOnlyDictionary<string, string?>? query, object? body, CancellationToken ct)
    {
        string apiKey = CleanSecretPart(_account.apiKey);
        string apiSecret = CleanSecretPart(_account.apiSecret);
        string ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string signedPath = path + BuildSigningQuery(query, body, ts, apiKey);
        string sign;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret)))
        {
            sign = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(method.Method.ToUpperInvariant() + signedPath))).ToUpperInvariant();
        }
        using var req = new HttpRequestMessage(method, BaseUrl + path + Query(query));
        req.Headers.TryAddWithoutValidation("X-CC-APIKEY", apiKey);
        req.Headers.TryAddWithoutValidation("sign", sign);
        req.Headers.TryAddWithoutValidation("ts", ts);
        req.Headers.TryAddWithoutValidation("X-REQ-TS-DIFF", DefaultTsDiff);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct);
        return await ParseResponse(resp, ct);
    }

    private static async Task<JsonDocument> SendPublicAsync(string requestPath, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(BaseUrl + requestPath, ct);
        return await ParseResponse(resp, ct);
    }

    private static async Task<JsonDocument> SendUiGuestAsync(string path, IReadOnlyDictionary<string, string?>? query, CancellationToken ct)
    {
        var session = await EnsureUiGuestAuthAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path + Query(query));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        req.Headers.TryAddWithoutValidation("uuid", session.Uuid);
        req.Headers.TryAddWithoutValidation("api_uuid", session.Uuid);
        req.Headers.TryAddWithoutValidation("api_key", session.Key);
        req.Headers.TryAddWithoutValidation("key", session.Key);
        req.Headers.TryAddWithoutValidation("tsdiff", session.TsDiff);
        req.Headers.TryAddWithoutValidation("api_tsdiff", session.TsDiff);
        req.Headers.TryAddWithoutValidation("x-req-ts-diff", session.TsDiff);
        req.Headers.TryAddWithoutValidation("Cookie", "token=" + session.Token);
        using var resp = await UiGuestHttp.SendAsync(req, ct);
        return await ParseResponse(resp, ct);
    }

    private static async Task<(string Uuid, string Key, string Token, string TsDiff)> EnsureUiGuestAuthAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_uiGuestToken) && !string.IsNullOrWhiteSpace(_uiGuestUuid) && !string.IsNullOrWhiteSpace(_uiGuestKey) && DateTime.UtcNow < _uiGuestAuthExpiresUtc)
            return (_uiGuestUuid!, _uiGuestKey!, _uiGuestToken!, _uiGuestTsDiff ?? DefaultTsDiff);

        await UiGuestAuthLock.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(_uiGuestToken) && !string.IsNullOrWhiteSpace(_uiGuestUuid) && !string.IsNullOrWhiteSpace(_uiGuestKey) && DateTime.UtcNow < _uiGuestAuthExpiresUtc)
                return (_uiGuestUuid!, _uiGuestKey!, _uiGuestToken!, _uiGuestTsDiff ?? DefaultTsDiff);

            using var resp = await UiGuestHttp.GetAsync(BaseUrl + "/auth/start/v1", ct);
            using var doc = await ParseResponse(resp, ct);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("CoinCall auth/start response has no data payload.");

            _uiGuestUuid = data.TryGetProperty("uuid", out var uuidEl) ? ReadString(uuidEl) : string.Empty;
            _uiGuestKey = data.TryGetProperty("key", out var keyEl) ? ReadString(keyEl) : string.Empty;
            _uiGuestToken = data.TryGetProperty("token", out var tokenEl) ? ReadString(tokenEl) : string.Empty;
            _uiGuestTsDiff = data.TryGetProperty("serverTs", out var tsEl) ? ReadString(tsEl) : DefaultTsDiff;
            if (string.IsNullOrWhiteSpace(_uiGuestUuid) || string.IsNullOrWhiteSpace(_uiGuestKey) || string.IsNullOrWhiteSpace(_uiGuestToken))
                throw new InvalidOperationException("CoinCall auth/start response is missing guest credentials.");
            _uiGuestAuthExpiresUtc = DateTime.UtcNow.AddMinutes(10);
            return (_uiGuestUuid!, _uiGuestKey!, _uiGuestToken!, _uiGuestTsDiff ?? DefaultTsDiff);
        }
        finally
        {
            UiGuestAuthLock.Release();
        }
    }

    private static async Task<JsonDocument> ParseResponse(HttpResponseMessage resp, CancellationToken ct)
    {
        string raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"CoinCall HTTP {(int)resp.StatusCode}: {raw}");
        var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("code", out var codeEl) && ReadString(codeEl) != "0")
        {
            string msg = doc.RootElement.TryGetProperty("msg", out var msgEl) ? ReadString(msgEl) : "CoinCall error";
            throw new InvalidOperationException($"CoinCall API error {ReadString(codeEl)}: {msg}; raw={raw}");
        }
        return doc;
    }

    private string BuildSigningQuery(IReadOnlyDictionary<string, string?>? query, object? body, string ts, string apiKey)
    {
        var requestPairs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (query != null) foreach (var kv in query) if (!string.IsNullOrWhiteSpace(kv.Value)) requestPairs[kv.Key] = kv.Value!;
        if (body != null) foreach (var kv in FlattenBody(body)) requestPairs[kv.Key] = kv.Value;
        var parts = requestPairs.Select(kv => kv.Key + "=" + kv.Value).ToList();
        parts.Add("uuid=" + apiKey);
        parts.Add("ts=" + ts);
        parts.Add("x-req-ts-diff=" + DefaultTsDiff);
        return "?" + string.Join("&", parts);
    }

    private static string Query(IReadOnlyDictionary<string, string?>? query)
    {
        if (query == null) return string.Empty;
        var pairs = query.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value!)).ToList();
        return pairs.Count == 0 ? string.Empty : "?" + string.Join("&", pairs);
    }

    private static IEnumerable<KeyValuePair<string, string>> FlattenBody(object body)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(body));
        if (doc.RootElement.ValueKind != JsonValueKind.Object) yield break;
        foreach (var prop in doc.RootElement.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            yield return new KeyValuePair<string, string>(prop.Name, JsonValue(prop.Value));
        }
    }

    private static string JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array or JsonValueKind.Object => CanonicalJsonWithoutNulls(value),
        _ => value.ToString()
    };

    private static string CanonicalJsonWithoutNulls(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var parts = value.EnumerateObject()
                .Where(p => p.Value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                .Select(p => JsonSerializer.Serialize(p.Name) + ":" + CanonicalJsonWithoutNulls(p.Value));
            return "{" + string.Join(",", parts) + "}";
        }
        if (value.ValueKind == JsonValueKind.Array) return "[" + string.Join(",", value.EnumerateArray().Where(v => v.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined).Select(CanonicalJsonWithoutNulls)) + "]";
        return value.ValueKind switch
        {
            JsonValueKind.String => JsonSerializer.Serialize(value.GetString() ?? string.Empty),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.ToString()
        };
    }

    private static string CleanSecretPart(string? value) => (value ?? string.Empty).Trim().TrimStart('\uFEFF');

    private static string ReadString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.Number => el.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => el.ToString()
    };
}

public sealed record CoincallFuturesWebSocketAuth(string Url, string Uuid, string Ts, DateTime ExpiresAtUtc);
