using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VANWebService.Models;

namespace VANWebService.Services;

public sealed class OkxLiveSnapshot
{
    public decimal totalEquityUsdt { get; set; }
    public decimal availableEquityUsdt { get; set; }
    public decimal unrealizedPnlUsdt { get; set; }
    public decimal? marginRatio { get; set; }
    public object[] balanceDetails { get; set; } = Array.Empty<object>();
}

public sealed class OkxApiClient
{
    private const string BaseUrl = "https://www.okx.com";
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    private readonly OkxAccountRecord _account;

    static OkxApiClient()
    {
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VANWebService/1.0");
    }

    public OkxApiClient(OkxAccountRecord account)
    {
        _account = account;
    }

    public async Task<JsonDocument> GetBalanceAsync(CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Get, "/api/v5/account/balance", null, ct);
    }

    public async Task<JsonDocument> GetFundingBalancesAsync(CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Get, "/api/v5/asset/balances", null, ct);
    }

    public async Task<JsonDocument> GetFundingBillsAsync(CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Get, "/api/v5/asset/bills?limit=100", null, ct);
    }

    public async Task<JsonDocument> GetPositionsAsync(CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Get, "/api/v5/account/positions", null, ct);
    }

    public async Task<JsonDocument> GetAccountConfigAsync(CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Get, "/api/v5/account/config", null, ct);
    }

    public async Task<JsonDocument> SetAccountLevelAsync(object payload, CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Post, "/api/v5/account/set-account-level", payload, ct);
    }

    public async Task<JsonDocument> SetFeeTypeAsync(object payload, CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Post, "/api/v5/account/set-fee-type", payload, ct);
    }

    public async Task<JsonDocument> GetOpenOrdersAsync(CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Get, "/api/v5/trade/orders-pending", null, ct);
    }

    public async Task<JsonDocument> GetOrderHistoryAsync(string instType = "SPOT", CancellationToken ct = default)
    {
        string path = $"/api/v5/trade/orders-history?instType={Uri.EscapeDataString(instType)}&limit=100";
        return await SendAsync(HttpMethod.Get, path, null, ct);
    }

    public async Task<JsonDocument> GetFillsHistoryAsync(string instType = "SPOT", CancellationToken ct = default)
    {
        string path = $"/api/v5/trade/fills-history?instType={Uri.EscapeDataString(instType)}&limit=100";
        return await SendAsync(HttpMethod.Get, path, null, ct);
    }

    public async Task<JsonDocument> PlaceOrderAsync(object payload, CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Post, "/api/v5/trade/order", payload, ct);
    }

    public async Task<JsonDocument> PlaceSpreadOrderAsync(object payload, CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Post, "/api/v5/sprd/order", payload, ct);
    }

    public async Task<JsonDocument> CancelOrderAsync(object payload, CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Post, "/api/v5/trade/cancel-order", payload, ct);
    }

    public async Task<JsonDocument> TransferAsync(object payload, CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Post, "/api/v5/asset/transfer", payload, ct);
    }

    public async Task<JsonDocument> GetLeverageInfoAsync(string instId, string mgnMode = "cross", CancellationToken ct = default)
    {
        string path = $"/api/v5/account/leverage-info?instId={Uri.EscapeDataString(instId)}&mgnMode={Uri.EscapeDataString(mgnMode)}";
        return await SendAsync(HttpMethod.Get, path, null, ct);
    }

    public async Task<JsonDocument> SetLeverageAsync(object payload, CancellationToken ct = default)
    {
        return await SendAsync(HttpMethod.Post, "/api/v5/account/set-leverage", payload, ct);
    }

    public async Task<string> GetNearestBtcUsdFutureInstrumentAsync(CancellationToken ct = default)
    {
        using var doc = await SendPublicAsync("/api/v5/public/instruments?instType=FUTURES&uly=BTC-USD", ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("OKX public instruments response did not contain data for BTC-USD futures");
        }
        string best = "";
        long bestExp = long.MaxValue;
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var row in data.EnumerateArray())
        {
            string instId = ReadString(row, "instId");
            if (string.IsNullOrWhiteSpace(instId) || instId.Contains("_UM", StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(ReadString(row, "state"), "live", StringComparison.OrdinalIgnoreCase)) continue;
            if (!long.TryParse(ReadString(row, "expTime"), NumberStyles.Any, CultureInfo.InvariantCulture, out var expMs)) continue;
            if (expMs <= nowMs) continue;
            if (expMs < bestExp) { bestExp = expMs; best = instId; }
        }
        if (string.IsNullOrWhiteSpace(best))
        {
            throw new InvalidOperationException("No live BTC-USD futures instrument found in OKX instruments response");
        }
        return best;
    }

    public async Task<decimal> GetBtcUsdtLastPriceAsync(CancellationToken ct = default)
    {
        using var doc = await SendPublicAsync("/api/v5/market/ticker?instId=BTC-USDT", ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return 0m;
        }
        var row = data[0];
        decimal px = ReadDecimal(row, "last");
        if (px == 0m) px = ReadDecimal(row, "askPx");
        if (px == 0m) px = ReadDecimal(row, "bidPx");
        return px;
    }

    public static async Task<JsonDocument> GetPublicAsync(string requestPath, CancellationToken ct = default)
    {
        return await SendPublicAsync(requestPath, ct);
    }

    public async Task<OkxLiveSnapshot> GetLiveSnapshotAsync(CancellationToken ct = default)
    {
        using var balanceDoc = await GetBalanceAsync(ct);
        using var positionsDoc = await GetPositionsAsync(ct);
        JsonElement balanceRoot = balanceDoc.RootElement;
        JsonElement positionsRoot = positionsDoc.RootElement;
        JsonElement data0 = balanceRoot.GetProperty("data")[0];
        JsonElement details = data0.TryGetProperty("details", out var detailsEl) && detailsEl.ValueKind == JsonValueKind.Array ? detailsEl : default;
        decimal totalEq = ReadDecimal(data0, "totalEq");
        decimal availEq = ReadDecimal(data0, "availEq");
        if (availEq == 0m)
        {
            availEq = ReadDecimal(data0, "adjEq");
        }
        decimal upl = 0m;
        decimal? marginRatio = null;
        if (positionsRoot.TryGetProperty("data", out var posData) && posData.ValueKind == JsonValueKind.Array)
        {
            decimal mmrTotal = 0m;
            int mmrCount = 0;
            foreach (var pos in posData.EnumerateArray())
            {
                upl += ReadDecimal(pos, "upl");
                decimal mmr = ReadDecimal(pos, "mgnRatio");
                if (mmr != 0m)
                {
                    mmrTotal += mmr;
                    mmrCount++;
                }
            }
            if (mmrCount > 0) marginRatio = mmrTotal / mmrCount;
        }
        var detailRows = new List<object>();
        if (details.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in details.EnumerateArray())
            {
                detailRows.Add(new
                {
                    ccy = ReadString(row, "ccy"),
                    eq = ReadString(row, "eq"),
                    eqUsd = ReadString(row, "eqUsd"),
                    availEq = ReadString(row, "availEq"),
                    cashBal = ReadString(row, "cashBal"),
                    upl = ReadString(row, "upl")
                });
            }
        }
        return new OkxLiveSnapshot
        {
            totalEquityUsdt = totalEq,
            availableEquityUsdt = availEq,
            unrealizedPnlUsdt = upl,
            marginRatio = marginRatio,
            balanceDetails = detailRows.ToArray()
        };
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string requestPath, object? body, CancellationToken ct)
    {
        string bodyJson = body == null ? string.Empty : JsonSerializer.Serialize(body);
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        string prehash = timestamp + method.Method.ToUpperInvariant() + requestPath + bodyJson;
        string sign;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_account.apiSecret)))
        {
            sign = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(prehash)));
        }
        using var req = new HttpRequestMessage(method, BaseUrl + requestPath);
        req.Headers.TryAddWithoutValidation("OK-ACCESS-KEY", _account.apiKey);
        req.Headers.TryAddWithoutValidation("OK-ACCESS-SIGN", sign);
        req.Headers.TryAddWithoutValidation("OK-ACCESS-TIMESTAMP", timestamp);
        req.Headers.TryAddWithoutValidation("OK-ACCESS-PASSPHRASE", _account.passphrase);
        if (_account.isDemo)
        {
            req.Headers.TryAddWithoutValidation("x-simulated-trading", "1");
        }
        if (body != null)
        {
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        }
        using var resp = await Http.SendAsync(req, ct);
        string raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OKX HTTP {(int)resp.StatusCode}: {raw}");
        }
        var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetString() != "0")
        {
            string msg = doc.RootElement.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() ?? "OKX error" : "OKX error";
            throw new InvalidOperationException($"OKX API error {codeEl.GetString()}: {msg}; raw={raw}");
        }
        return doc;
    }

    private static async Task<JsonDocument> SendPublicAsync(string requestPath, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(BaseUrl + requestPath, ct);
        string raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OKX HTTP {(int)resp.StatusCode}: {raw}");
        }
        var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetString() != "0")
        {
            string msg = doc.RootElement.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() ?? "OKX error" : "OKX error";
            throw new InvalidOperationException($"OKX API error {codeEl.GetString()}: {msg}; raw={raw}");
        }
        return doc;
    }

    public static decimal ReadDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var value)) return 0m;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetDecimal(out var d)) return d;
            return 0m;
        }
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return 0m;
    }

    public static string ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => value.ToString()
        };
    }
}
