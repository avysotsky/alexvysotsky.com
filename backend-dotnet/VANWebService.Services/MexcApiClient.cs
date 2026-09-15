using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VANWebService.Models;

namespace VANWebService.Services;

public sealed class MexcApiClient
{
    private const string SpotBaseUrl = "https://api.mexc.com";
    private const string FuturesBaseUrl = "https://contract.mexc.com";
    private const long DefaultRecvWindowMs = 5000;
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    private readonly MexcAccountRecord? _account;

    public MexcApiClient()
    {
    }

    public MexcApiClient(MexcAccountRecord account)
    {
        _account = account;
    }

    public Task<JsonDocument> GetSpotExchangeInfoAsync(string symbol, CancellationToken ct = default)
        => SendAsync(SpotBaseUrl, "/api/v3/exchangeInfo", Query(new Dictionary<string, string?> { ["symbol"] = symbol }), ct);

    public Task<JsonDocument> GetSpotKlinesAsync(string symbol, string interval, int limit, long? startTime, long? endTime, CancellationToken ct = default)
        => SendAsync(SpotBaseUrl, "/api/v3/klines", Query(new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["interval"] = interval,
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["startTime"] = startTime?.ToString(CultureInfo.InvariantCulture),
            ["endTime"] = endTime?.ToString(CultureInfo.InvariantCulture)
        }), ct);

    public Task<JsonDocument> GetSpotDepthAsync(string symbol, int limit, CancellationToken ct = default)
        => SendAsync(SpotBaseUrl, "/api/v3/depth", Query(new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
        }), ct);

    public Task<JsonDocument> GetFuturesContractDetailAsync(string symbol, CancellationToken ct = default)
        => SendAsync(FuturesBaseUrl, "/api/v1/contract/detail", Query(new Dictionary<string, string?> { ["symbol"] = symbol }), ct);

    public Task<JsonDocument> GetFuturesTickerAsync(CancellationToken ct = default)
        => SendAsync(FuturesBaseUrl, "/api/v1/contract/ticker", string.Empty, ct);

    public Task<JsonDocument> GetFuturesKlineAsync(string symbol, string interval, long? start, long? end, CancellationToken ct = default)
        => SendAsync(FuturesBaseUrl, "/api/v1/contract/kline/" + Uri.EscapeDataString(symbol), Query(new Dictionary<string, string?>
        {
            ["interval"] = interval,
            ["start"] = start?.ToString(CultureInfo.InvariantCulture),
            ["end"] = end?.ToString(CultureInfo.InvariantCulture)
        }), ct);

    public Task<JsonDocument> GetFuturesDepthAsync(string symbol, CancellationToken ct = default)
        => SendAsync(FuturesBaseUrl, "/api/v1/contract/depth/" + Uri.EscapeDataString(symbol), string.Empty, ct);

    public Task<JsonDocument> GetFuturesFundingRateAsync(string symbol, CancellationToken ct = default)
        => SendAsync(FuturesBaseUrl, "/api/v1/contract/funding_rate/" + Uri.EscapeDataString(symbol), string.Empty, ct);

    public Task<JsonDocument> GetSpotAccountAsync(CancellationToken ct = default)
        => SendSpotSignedGetAsync("/api/v3/account", new Dictionary<string, string?>(), ct);

    public Task<JsonDocument> GetSpotOpenOrdersAsync(string symbol, CancellationToken ct = default)
        => SendSpotSignedGetAsync("/api/v3/openOrders", new Dictionary<string, string?> { ["symbol"] = symbol }, ct);

    public Task<JsonDocument> GetSpotAllOrdersAsync(string symbol, int? limit, long? startTime, long? endTime, CancellationToken ct = default)
        => SendSpotSignedGetAsync("/api/v3/allOrders", new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["limit"] = limit?.ToString(CultureInfo.InvariantCulture),
            ["startTime"] = startTime?.ToString(CultureInfo.InvariantCulture),
            ["endTime"] = endTime?.ToString(CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonDocument> GetSpotMyTradesAsync(string symbol, int? limit, long? startTime, long? endTime, CancellationToken ct = default)
        => SendSpotSignedGetAsync("/api/v3/myTrades", new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["limit"] = limit?.ToString(CultureInfo.InvariantCulture),
            ["startTime"] = startTime?.ToString(CultureInfo.InvariantCulture),
            ["endTime"] = endTime?.ToString(CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonDocument> PlaceSpotOrderAsync(JsonElement payload, CancellationToken ct = default)
        => SendSpotSignedAsync(HttpMethod.Post, "/api/v3/order", BuildSpotOrderPayload(payload), ct);

    public Task<JsonDocument> CancelSpotOrderAsync(JsonElement payload, CancellationToken ct = default)
        => SendSpotSignedAsync(HttpMethod.Delete, "/api/v3/order", BuildSpotCancelPayload(payload), ct);


    public Task<JsonDocument> GetFuturesAssetsAsync(CancellationToken ct = default)
        => SendContractSignedGetAsync("/api/v1/private/account/assets", new Dictionary<string, string?>(), ct);

    public Task<JsonDocument> GetFuturesOpenPositionsAsync(string symbol, CancellationToken ct = default)
        => SendContractSignedGetAsync("/api/v1/private/position/open_positions", new Dictionary<string, string?> { ["symbol"] = symbol }, ct);

    public Task<JsonDocument> GetFuturesOpenOrdersAsync(string symbol, int pageNum, int pageSize, CancellationToken ct = default)
        => SendContractSignedGetAsync("/api/v1/private/order/list/open_orders/" + Uri.EscapeDataString(symbol), new Dictionary<string, string?>
        {
            ["page_num"] = pageNum.ToString(CultureInfo.InvariantCulture),
            ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonDocument> GetFuturesHistoryOrdersAsync(string symbol, string states, int? category, int? side, long? startTime, long? endTime, int pageNum, int pageSize, CancellationToken ct = default)
        => SendContractSignedGetAsync("/api/v1/private/order/list/history_orders", new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["states"] = states,
            ["category"] = category?.ToString(CultureInfo.InvariantCulture),
            ["side"] = side?.ToString(CultureInfo.InvariantCulture),
            ["start_time"] = startTime?.ToString(CultureInfo.InvariantCulture),
            ["end_time"] = endTime?.ToString(CultureInfo.InvariantCulture),
            ["page_num"] = pageNum.ToString(CultureInfo.InvariantCulture),
            ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonDocument> GetFuturesOrderDealsAsync(string symbol, long? startTime, long? endTime, int pageNum, int pageSize, CancellationToken ct = default)
        => SendContractSignedGetAsync("/api/v1/private/order/list/order_deals", new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["start_time"] = startTime?.ToString(CultureInfo.InvariantCulture),
            ["end_time"] = endTime?.ToString(CultureInfo.InvariantCulture),
            ["page_num"] = pageNum.ToString(CultureInfo.InvariantCulture),
            ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture)
        }, ct);

    public Task<JsonDocument> GetFuturesFundingRecordsAsync(string symbol, long? positionId, int pageNum, int pageSize, CancellationToken ct = default)
        => SendContractSignedGetAsync("/api/v1/private/position/funding_records", new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["position_id"] = positionId?.ToString(CultureInfo.InvariantCulture),
            ["page_num"] = pageNum.ToString(CultureInfo.InvariantCulture),
            ["page_size"] = pageSize.ToString(CultureInfo.InvariantCulture)
        }, ct);

    private Task<JsonDocument> SendSpotSignedGetAsync(string path, Dictionary<string, string?> values, CancellationToken ct)
        => SendSpotSignedAsync(HttpMethod.Get, path, values, ct);

    private Task<JsonDocument> SendSpotSignedAsync(HttpMethod method, string path, Dictionary<string, string?> values, CancellationToken ct)
    {
        var account = RequireAccount();
        values["recvWindow"] = DefaultRecvWindowMs.ToString(CultureInfo.InvariantCulture);
        values["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string queryToSign = Query(values, includeQuestionMark: false, sort: false);
        values["signature"] = HmacSha256Hex(account.apiSecret, queryToSign).ToLowerInvariant();
        string query = Query(values, includeQuestionMark: true, sort: false);
        var headers = new Dictionary<string, string> { ["X-MEXC-APIKEY"] = account.apiKey };
        return SendAsync(SpotBaseUrl, path, query, ct, headers, method);
    }

    private static Dictionary<string, string?> BuildSpotOrderPayload(JsonElement payload)
    {
        string symbol = Read(payload, "symbol", "instId", "instrument").ToUpperInvariant();
        string side = Read(payload, "side", "tradeSide", "orderSide").ToUpperInvariant();
        string tradeType = Read(payload, "type", "tradeType", "orderType").ToUpperInvariant().Replace("-", "_");
        string price = Read(payload, "price", "px", "orderPrice");
        string quantity = Read(payload, "quantity", "qty", "amount", "sz");
        if (string.IsNullOrWhiteSpace(symbol)) throw new InvalidOperationException("symbol is required");
        if (side != "BUY" && side != "SELL") throw new InvalidOperationException("side must be BUY or SELL");
        if (string.IsNullOrWhiteSpace(quantity)) throw new InvalidOperationException("qty is required");
        var values = new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["side"] = side,
            ["quantity"] = quantity
        };
        if (tradeType == "MARKET")
        {
            values["type"] = "MARKET";
            return values;
        }
        if (string.IsNullOrWhiteSpace(price)) throw new InvalidOperationException("price is required for limit orders");
        if (tradeType == "POST_ONLY" || tradeType == "LIMIT_MAKER")
        {
            values["type"] = "LIMIT_MAKER";
            values["price"] = price;
            return values;
        }
        values["type"] = "LIMIT";
        values["timeInForce"] = "GTC";
        values["price"] = price;
        return values;
    }

    private static Dictionary<string, string?> BuildSpotCancelPayload(JsonElement payload)
    {
        string symbol = Read(payload, "symbol", "instId", "instrument").ToUpperInvariant();
        string orderId = Read(payload, "orderId", "ordId", "id", "order_id");
        string clientOrderId = Read(payload, "origClientOrderId", "clientOrderId", "clientOid", "clOrdId");
        if (string.IsNullOrWhiteSpace(symbol)) throw new InvalidOperationException("symbol is required");
        if (string.IsNullOrWhiteSpace(orderId) && string.IsNullOrWhiteSpace(clientOrderId)) throw new InvalidOperationException("orderId or clientOrderId is required");
        return new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["orderId"] = orderId,
            ["origClientOrderId"] = clientOrderId
        };
    }

    private static string Read(JsonElement payload, params string[] names)
    {
        if (payload.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var name in names)
        {
            if (!payload.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Trim() ?? string.Empty;
            if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) return value.ToString().Trim();
        }
        return string.Empty;
    }

    private Task<JsonDocument> SendContractSignedGetAsync(string path, Dictionary<string, string?> values, CancellationToken ct)
    {
        var account = RequireAccount();
        string requestTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string paramString = Query(values, includeQuestionMark: false, sort: true);
        string signature = HmacSha256Hex(account.apiSecret, account.apiKey + requestTime + paramString).ToLowerInvariant();
        string query = Query(values, includeQuestionMark: true, sort: true);
        var headers = new Dictionary<string, string>
        {
            ["ApiKey"] = account.apiKey,
            ["Request-Time"] = requestTime,
            ["Signature"] = signature,
            ["Recv-Window"] = "10000"
        };
        return SendAsync(FuturesBaseUrl, path, query, ct, headers);
    }

    private MexcAccountRecord RequireAccount()
    {
        if (_account == null || string.IsNullOrWhiteSpace(_account.apiKey) || string.IsNullOrWhiteSpace(_account.apiSecret))
        {
            throw new InvalidOperationException("MEXC apiKey and apiSecret are required");
        }
        return _account;
    }

    private static async Task<JsonDocument> SendAsync(string baseUrl, string path, string query, CancellationToken ct, IReadOnlyDictionary<string, string>? headers = null, HttpMethod? method = null)
    {
        using var req = new HttpRequestMessage(method ?? HttpMethod.Get, baseUrl + path + query);
        if (headers != null)
        {
            foreach (var header in headers)
            {
                req.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            string trimmed = body.Length > 1000 ? body[..1000] : body;
            throw new InvalidOperationException("MEXC upstream returned HTTP " + (int)resp.StatusCode + ": " + trimmed);
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("MEXC upstream returned invalid JSON: " + ex.Message);
        }
    }

    private static string Query(IReadOnlyDictionary<string, string?> values, bool includeQuestionMark = true, bool sort = false)
    {
        IEnumerable<KeyValuePair<string, string?>> pairs = values;
        if (sort) pairs = pairs.OrderBy(kv => kv.Key, StringComparer.Ordinal);
        var parts = new List<string>();
        foreach (var kv in pairs)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value))
            {
                parts.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value!));
            }
        }
        if (parts.Count == 0) return string.Empty;
        string query = string.Join("&", parts);
        return includeQuestionMark ? "?" + query : query;
    }

    private static string HmacSha256Hex(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }
}
