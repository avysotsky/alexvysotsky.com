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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VANWebService.Auth;
using VANWebService.Models;

namespace VANWebService;

public static class BybitEndpoints
{
    private const string MainBaseUrl = "https://api.bybit.com";
    private const string DemoBaseUrl = "https://api-demo.bybit.com";
    private static readonly HttpClient Http = new HttpClient();

    public static void MapBybitEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/bybit/accounts", async (HttpContext context, BybitRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var accounts = await repo.GetAccountsAsync(false, context.RequestAborted);
            return Results.Json(new { items = accounts.Select(a => ToPublicAccount(a)) });
        });

        app.MapPost("/api/admin/bybit/accounts", async (HttpContext context, BybitRepository repo, BybitCredentialsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var credentials = NormalizeCredentials(dto);
            if (!credentials.ok) return Results.BadRequest(new { message = credentials.message });
            int id = await repo.CreateAccountAsync(credentials.value!.name!, credentials.value!.apiKey!, credentials.value!.apiSecret!, credentials.value!.passphrase ?? string.Empty, credentials.value!.isDemo, dto.setActive || dto.isActive, credentials.value!.storeMinuteEquity, credentials.value!.equityCurrency!, context.RequestAborted);
            var created = await repo.GetAccountByIdAsync(id, context.RequestAborted);
            return Results.Json(ToPublicAccount(created!));
        });

        app.MapPut("/api/admin/bybit/accounts/{id:int}", async (HttpContext context, BybitRepository repo, int id, BybitCredentialsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.UpdateAccountSettingsAsync(id, dto.storeMinuteEquity, NormalizeEquityCurrency(dto.equityCurrency), context.RequestAborted);
            return ok ? Results.Ok(new { ok = true }) : Results.NotFound(new { message = "Bybit account not found" });
        });

        app.MapPut("/api/admin/bybit/accounts/{id:int}/activate", async (HttpContext context, BybitRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.SetActiveAccountAsync(id, context.RequestAborted);
            return ok ? Results.Ok(new { ok = true }) : Results.NotFound(new { message = "Bybit account not found" });
        });

        app.MapDelete("/api/admin/bybit/accounts/{id:int}", async (HttpContext context, BybitRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.DeleteAccountAsync(id, context.RequestAborted);
            return ok ? Results.Ok(new { ok = true }) : Results.NotFound(new { message = "Bybit account not found" });
        });

        app.MapPost("/api/admin/bybit/test-credentials", async (HttpContext context, BybitRepository repo, BybitCredentialsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var credentials = await ResolveCredentialsAsync(repo, dto, context.RequestAborted);
            if (!credentials.ok) return Results.BadRequest(new { ok = false, message = credentials.message });

            var result = await FetchWalletBalanceAsync(credentials.value!, context.RequestAborted);
            if (!result.ok)
            {
                return Results.Json(new { ok = false, message = result.message, retCode = result.retCode }, statusCode: 400);
            }
            return Results.Json(new { ok = true, message = "Credentials accepted by Bybit. Account access is working." });
        });

        app.MapPost("/api/admin/bybit/summary", async (HttpContext context, BybitRepository repo, BybitCredentialsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var credentials = await ResolveCredentialsAsync(repo, dto, context.RequestAborted);
            if (!credentials.ok) return Results.BadRequest(new { message = credentials.message });

            var result = await FetchWalletBalanceAsync(credentials.value!, context.RequestAborted);
            if (!result.ok) return Results.Json(new { message = result.message, retCode = result.retCode }, statusCode: 400);
            var snapshot = ParseWalletSnapshot(result.document!.RootElement);
            return Results.Json(new
            {
                account = ToPublicAccount(credentials.value!),
                totalEquityUsdt = snapshot.totalEquityUsdt,
                availableEquityUsdt = snapshot.availableEquityUsdt,
                unrealizedPnlUsdt = snapshot.unrealizedPnlUsdt,
                marginRatio = snapshot.marginRatio,
                balanceDetails = snapshot.balanceDetails,
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapPost("/api/admin/bybit/equity", async (HttpContext context, BybitRepository repo, BybitCredentialsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var credentials = await ResolveCredentialsAsync(repo, dto, context.RequestAborted);
            if (!credentials.ok) return Results.BadRequest(new { message = credentials.message });

            var result = await FetchWalletBalanceAsync(credentials.value!, context.RequestAborted);
            if (!result.ok) return Results.Json(new { message = result.message, retCode = result.retCode }, statusCode: 400);
            var snapshot = ParseWalletSnapshot(result.document!.RootElement);
            string currency = NormalizeEquityCurrency(credentials.value!.equityCurrency);
            DateTime tsUtc = DateTime.UtcNow;
            string accountUid = BuildAccountUid(credentials.value!);
            await repo.EnsureSchemaAsync(context.RequestAborted);
            await repo.UpsertEquityAsync(accountUid, credentials.value!.name ?? "Selected Bybit account", tsUtc, currency, snapshot.totalEquityUsdt, snapshot.availableEquityUsdt, snapshot.unrealizedPnlUsdt, snapshot.marginRatio, credentials.value!.storeMinuteEquity, context.RequestAborted);
            var daily = await repo.GetDailyEquityAsync(accountUid, 3650, context.RequestAborted);
            var cashBalance = await repo.GetDailyCashBalanceAsync(accountUid, 3650, context.RequestAborted);
            var minute = credentials.value!.storeMinuteEquity
                ? await repo.GetMinuteEquityAsync(accountUid, 24, context.RequestAborted)
                : new List<BybitEquityPoint>();
            var dailyDrawdownMinute = credentials.value!.storeMinuteEquity
                ? await repo.GetMinuteEquityForDailyDrawdownAsync(accountUid, 3650, context.RequestAborted)
                : new List<BybitEquityPoint>();
            return Results.Json(new
            {
                account = ToPublicAccount(credentials.value!),
                metrics = new
                {
                    equityCurrency = currency,
                    storeMinuteEquity = credentials.value!.storeMinuteEquity,
                    currentEquity = snapshot.totalEquityUsdt,
                    availableEquity = snapshot.availableEquityUsdt,
                    unrealizedPnl = snapshot.unrealizedPnlUsdt,
                    marginRatio = snapshot.marginRatio,
                    lastSnapshotUtc = tsUtc,
                    historyConnected = true,
                    dailyPointCount = daily.Count,
                    minutePointCount = minute.Count
                },
                live = new
                {
                    totalEquityUsdt = snapshot.totalEquityUsdt,
                    availableEquityUsdt = snapshot.availableEquityUsdt,
                    unrealizedPnlUsdt = snapshot.unrealizedPnlUsdt,
                    marginRatio = snapshot.marginRatio,
                    balanceDetails = snapshot.balanceDetails
                },
                daily = new { points = daily.Select(p => new { tsUtc = p.tsUtc, value = p.totalEquity }), drawdownPoints = BuildDailyDrawdownSeriesWithMinuteOverlay(daily, dailyDrawdownMinute), historyConnected = true },
                cashBalance = new { points = cashBalance.Select(p => new { tsUtc = p.tsUtc, currency = p.currency, value = p.cashBalance }), historyConnected = true, source = "bybit_transaction_log_cashBalance" },
                minute = new { points = minute.Select(p => new { tsUtc = p.tsUtc, value = p.totalEquity }), drawdownPoints = BuildDrawdownSeriesWith30MinuteConfirmedHwm(minute, collapseToDate: false), historyConnected = true }
            });
        });

        app.MapGet("/api/admin/bybit/futures/funding/minute", async (HttpContext context, BybitRepository repo) =>
        {
            string symbol = NormalizeBybitSymbol(context.Request.Query["symbol"].ToString(), "BTCUSDT");
            int limit = int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 5000;
            var rows = await repo.GetFundingMinuteAsync(symbol, Math.Clamp(limit, 1, 10000), context.RequestAborted);
            var items = rows.Select(ToFundingMinutePayloadRow).ToList();
            return Results.Json(new { source = "van_bybit_quote_minute", symbol, order = "oldest-to-newest", rows = items, data = new { list = items }, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/bybit/futures/funding/history", async (HttpContext context) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            string symbol = NormalizeBybitSymbol(context.Request.Query["symbol"].ToString(), "BTCUSDT");
            string category = NormalizeBybitCategory(context.Request.Query["category"].ToString(), symbol);
            int limit = int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 200;
            string query = "/v5/market/funding/history?category=" + Uri.EscapeDataString(category)
                + "&symbol=" + Uri.EscapeDataString(symbol)
                + "&limit=" + Math.Clamp(limit, 1, 200).ToString(CultureInfo.InvariantCulture);
            try
            {
                using var doc = await FetchBybitPublicAsync(query, context.RequestAborted);
                var items = ParseFundingHistoryRows(doc.RootElement, symbol, category)
                    .Select(row => new
                    {
                        symbol = row.symbol,
                        category = row.category,
                        tsUtc = row.tsUtc,
                        recordTimeUtc = row.tsUtc,
                        time = row.time,
                        fundingRate = row.fundingRate,
                        fundRate = row.fundingRate,
                        interest8h = row.fundingRate,
                        interest_8h = row.fundingRate
                    })
                    .ToList();
                return Results.Json(new { source = "bybit:/v5/market/funding/history", symbol, category, order = "oldest-to-newest", rows = items, data = new { list = items }, fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = ex.Message });
            }
        });

    }


    private static string BuildAccountUid(BybitCredentialsRequest account)
    {
        return BybitRepository.BuildAccountUid(account.id, account.apiKey, account.isDemo);
    }

    private static List<object> BuildDailyDrawdownSeriesWithMinuteOverlay(List<BybitEquityPoint> dailyPoints, List<BybitEquityPoint> minutePoints)
    {
        var byDate = BuildDrawdownSeries(dailyPoints, collapseToDate: true)
            .ToDictionary(p => DateOnly.FromDateTime(p.tsUtc), p => p.value);

        foreach (var point in BuildDrawdownSeriesWith30MinuteConfirmedHwm(minutePoints, collapseToDate: true))
        {
            byDate[DateOnly.FromDateTime(point.tsUtc)] = point.value;
        }

        return byDate
            .OrderBy(kv => kv.Key)
            .Select(kv => new { tsUtc = kv.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), value = Math.Round(kv.Value, 4) })
            .Cast<object>()
            .ToList();
    }

    private static List<BybitDrawdownPoint> BuildDrawdownSeriesWith30MinuteConfirmedHwm(List<BybitEquityPoint> points, bool collapseToDate)
    {
        var result = new List<BybitDrawdownPoint>();
        var ordered = points.OrderBy(p => p.tsUtc).ToList();
        if (ordered.Count == 0) return result;

        decimal acceptedHwm = ordered[0].totalEquity;
        decimal? candidatePeak = null;
        int candidateCount = 0;

        foreach (var point in ordered)
        {
            if (point.totalEquity > acceptedHwm)
            {
                candidatePeak = candidatePeak.HasValue ? Math.Max(candidatePeak.Value, point.totalEquity) : point.totalEquity;
                candidateCount++;
                if (candidateCount > 30 && candidatePeak.HasValue)
                {
                    acceptedHwm = candidatePeak.Value;
                    candidatePeak = null;
                    candidateCount = 0;
                }
            }
            else
            {
                candidatePeak = null;
                candidateCount = 0;
            }

            decimal dd = acceptedHwm <= 0m ? 0m : (acceptedHwm - point.totalEquity) / acceptedHwm * 100m;
            if (dd < 0m) dd = 0m;
            DateTime ts = collapseToDate ? DateTime.SpecifyKind(point.tsUtc.Date, DateTimeKind.Utc) : point.tsUtc;
            if (collapseToDate && result.Count > 0 && result[^1].tsUtc == ts)
            {
                if (dd > result[^1].value) result[^1] = new BybitDrawdownPoint(ts, Math.Round(dd, 4));
            }
            else
            {
                result.Add(new BybitDrawdownPoint(ts, Math.Round(dd, 4)));
            }
        }
        return result;
    }

    private static List<BybitDrawdownPoint> BuildDrawdownSeries(List<BybitEquityPoint> points, bool collapseToDate)
    {
        var result = new List<BybitDrawdownPoint>();
        var ordered = points.OrderBy(p => p.tsUtc).ToList();
        if (ordered.Count == 0) return result;
        decimal peak = ordered[0].totalEquity;
        foreach (var point in ordered)
        {
            if (point.totalEquity > peak) peak = point.totalEquity;
            decimal dd = peak <= 0m ? 0m : (peak - point.totalEquity) / peak * 100m;
            DateTime ts = collapseToDate ? DateTime.SpecifyKind(point.tsUtc.Date, DateTimeKind.Utc) : point.tsUtc;
            if (collapseToDate && result.Count > 0 && result[^1].tsUtc == ts)
            {
                if (dd > result[^1].value) result[^1] = new BybitDrawdownPoint(ts, Math.Round(dd, 4));
            }
            else
            {
                result.Add(new BybitDrawdownPoint(ts, Math.Round(dd, 4)));
            }
        }
        return result;
    }

    private sealed record BybitDrawdownPoint(DateTime tsUtc, decimal value);


    private static async Task<(bool ok, string? message, BybitCredentialsRequest? value)> ResolveCredentialsAsync(BybitRepository repo, BybitCredentialsRequest dto, CancellationToken ct)
    {
        if ((string.IsNullOrWhiteSpace(dto.apiKey) || string.IsNullOrWhiteSpace(dto.apiSecret)) && int.TryParse(dto.id, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            var account = await repo.GetAccountByIdAsync(id, ct);
            if (account == null) return (false, "Bybit account not found", null);
            return (true, null, new BybitCredentialsRequest { id = account.id.ToString(CultureInfo.InvariantCulture), name = account.name, apiKey = account.apiKey, apiSecret = account.apiSecret, passphrase = account.passphrase, isDemo = account.isDemo, isActive = account.isActive, storeMinuteEquity = account.storeMinuteEquity, equityCurrency = account.equityCurrency });
        }
        return NormalizeCredentials(dto);
    }

    private static object ToPublicAccount(BybitAccountRecord account) => new
    {
        id = account.id.ToString(CultureInfo.InvariantCulture),
        name = account.name,
        apiKeyMasked = MaskApiKey(account.apiKey),
        isActive = account.isActive,
        isDemo = account.isDemo,
        storeMinuteEquity = account.storeMinuteEquity,
        equityCurrency = NormalizeEquityCurrency(account.equityCurrency)
    };

    private static (bool ok, string? message, BybitCredentialsRequest? value) NormalizeCredentials(BybitCredentialsRequest dto)
    {
        dto.apiKey = (dto.apiKey ?? string.Empty).Trim();
        dto.apiSecret = (dto.apiSecret ?? string.Empty).Trim();
        dto.name = string.IsNullOrWhiteSpace(dto.name) ? "Selected Bybit account" : dto.name.Trim();
        dto.equityCurrency = NormalizeEquityCurrency(dto.equityCurrency);
        if (string.IsNullOrWhiteSpace(dto.apiKey) || string.IsNullOrWhiteSpace(dto.apiSecret))
        {
            return (false, "apiKey and apiSecret are required", null);
        }
        return (true, null, dto);
    }

    private static async Task<(bool ok, string message, int retCode, JsonDocument? document)> FetchWalletBalanceAsync(BybitCredentialsRequest credentials, CancellationToken ct)
    {
        const string recvWindow = "5000";
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string query = "accountType=UNIFIED";
        string signPayload = timestamp + credentials.apiKey + recvWindow + query;
        string sign;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(credentials.apiSecret!)))
        {
            sign = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signPayload))).ToLowerInvariant();
        }

        string baseUrl = credentials.isDemo ? DemoBaseUrl : MainBaseUrl;
        using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v5/account/wallet-balance?" + query);
        req.Headers.TryAddWithoutValidation("X-BAPI-API-KEY", credentials.apiKey);
        req.Headers.TryAddWithoutValidation("X-BAPI-TIMESTAMP", timestamp);
        req.Headers.TryAddWithoutValidation("X-BAPI-RECV-WINDOW", recvWindow);
        req.Headers.TryAddWithoutValidation("X-BAPI-SIGN", sign);

        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        try
        {
            var doc = JsonDocument.Parse(body);
            int retCode = doc.RootElement.TryGetProperty("retCode", out var codeEl) && codeEl.TryGetInt32(out var c) ? c : -1;
            string retMsg = doc.RootElement.TryGetProperty("retMsg", out var msgEl) ? (msgEl.GetString() ?? string.Empty) : string.Empty;
            if (res.IsSuccessStatusCode && retCode == 0)
            {
                return (true, "OK", 0, doc);
            }
            doc.Dispose();
            return (false, string.IsNullOrWhiteSpace(retMsg) ? $"Bybit returned HTTP {(int)res.StatusCode}" : retMsg, retCode, null);
        }
        catch
        {
            return (false, $"Bybit returned HTTP {(int)res.StatusCode}", -1, null);
        }
    }

    private static BybitWalletSnapshot ParseWalletSnapshot(JsonElement root)
    {
        var list = root.GetProperty("result").GetProperty("list").EnumerateArray().FirstOrDefault();
        var details = new List<object>();
        decimal totalEquity = ReadDecimal(list, "totalEquity");
        decimal available = ReadDecimal(list, "totalAvailableBalance");
        decimal upl = ReadDecimal(list, "totalPerpUPL");
        decimal marginRatio = ReadDecimal(list, "accountMMRate");

        if (list.ValueKind != JsonValueKind.Undefined && list.TryGetProperty("coin", out var coins) && coins.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in coins.EnumerateArray())
            {
                details.Add(new
                {
                    currency = ReadString(c, "coin"),
                    equity = ReadString(c, "equity"),
                    usdValue = ReadString(c, "usdValue"),
                    walletBalance = ReadString(c, "walletBalance"),
                    available = ReadString(c, "availableToWithdraw"),
                    unrealizedPnl = ReadString(c, "unrealisedPnl")
                });
            }
        }
        return new BybitWalletSnapshot(totalEquity, available, upl, marginRatio, details);
    }


    private sealed record BybitFundingHistoryRow(string symbol, string category, DateTime? tsUtc, long? time, decimal? fundingRate);

    private static object ToFundingMinutePayloadRow(BybitQuoteMinuteRow row) => new
    {
        exchange = row.exchange,
        market = row.market,
        category = row.category,
        symbol = row.symbol,
        instrumentName = row.instrumentName,
        displayName = row.displayName,
        tickerId = row.tickerId,
        baseCurrency = row.baseCurrency,
        quoteCurrency = row.quoteCurrency,
        productType = row.productType,
        observedMinuteUtc = row.minuteUtc,
        tsUtc = row.minuteUtc,
        time = new DateTimeOffset(DateTime.SpecifyKind(row.minuteUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
        currentFunding = row.currentFunding,
        current_funding = row.currentFunding,
        interest8h = null as decimal?,
        interest_8h = null as decimal?,
        markPrice = row.mark,
        indexPrice = row.index
    };

    private static List<BybitFundingHistoryRow> ParseFundingHistoryRows(JsonElement root, string symbol, string category)
    {
        var rows = new List<BybitFundingHistoryRow>();
        if (!root.TryGetProperty("result", out var result) || !result.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var item in list.EnumerateArray())
        {
            decimal? fundingRate = ReadNullableDecimal(item, "fundingRate");
            long? tsMs = ReadNullableLong(item, "fundingRateTimestamp");
            DateTime? tsUtc = tsMs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(tsMs.Value).UtcDateTime : null;
            string itemSymbol = ReadString(item, "symbol").Trim().ToUpperInvariant();
            rows.Add(new BybitFundingHistoryRow(string.IsNullOrWhiteSpace(itemSymbol) ? symbol : itemSymbol, category, tsUtc, tsMs, fundingRate));
        }

        return rows.OrderBy(row => row.tsUtc ?? DateTime.MinValue).ToList();
    }

    private static async Task<JsonDocument> FetchBybitPublicAsync(string path, CancellationToken ct)
    {
        using var res = await Http.GetAsync(MainBaseUrl + path, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch
        {
            throw new InvalidOperationException("Bybit returned HTTP " + (int)res.StatusCode);
        }

        int retCode = doc.RootElement.TryGetProperty("retCode", out var codeEl) && codeEl.TryGetInt32(out var c) ? c : -1;
        string retMsg = doc.RootElement.TryGetProperty("retMsg", out var msgEl) ? (msgEl.GetString() ?? string.Empty) : string.Empty;
        if (res.IsSuccessStatusCode && retCode == 0)
        {
            return doc;
        }

        doc.Dispose();
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(retMsg) ? "Bybit returned HTTP " + (int)res.StatusCode : retMsg);
    }

    private static string NormalizeBybitSymbol(string? value, string fallback)
    {
        string v = (value ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(v) ? fallback : v;
    }

    private static string NormalizeBybitCategory(string? value, string symbol)
    {
        string v = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (v == "linear" || v == "inverse") return v;
        return symbol.EndsWith("USD", StringComparison.OrdinalIgnoreCase) && !symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
            ? "inverse"
            : "linear";
    }

    private static decimal? ReadNullableDecimal(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Undefined || !el.TryGetProperty(prop, out var p) || p.ValueKind == JsonValueKind.Null) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
        if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    private static long? ReadNullableLong(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Undefined || !el.TryGetProperty(prop, out var p) || p.ValueKind == JsonValueKind.Null) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
        if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out n)) return n;
        return null;
    }

    private static decimal ReadDecimal(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Undefined || !el.TryGetProperty(prop, out var p)) return 0m;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
        if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
        return 0m;
    }

    private static string ReadString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var p)) return string.Empty;
        return p.ValueKind == JsonValueKind.String ? (p.GetString() ?? string.Empty) : p.ToString();
    }

    private static string NormalizeEquityCurrency(string? value)
    {
        string v = (value ?? "USD").Trim().ToUpperInvariant();
        return v == "BTC" ? "BTC" : "USD";
    }

    private static object ToPublicAccount(BybitCredentialsRequest account) => new
    {
        id = account.id,
        name = account.name,
        apiKeyMasked = MaskApiKey(account.apiKey),
        isActive = account.isActive,
        isDemo = account.isDemo,
        storeMinuteEquity = account.storeMinuteEquity,
        equityCurrency = NormalizeEquityCurrency(account.equityCurrency)
    };

    private static string MaskApiKey(string? apiKey)
    {
        string v = apiKey ?? string.Empty;
        if (v.Length <= 8) return new string('*', Math.Max(0, v.Length));
        return v[..4] + "…" + v[^4..];
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

public sealed class BybitCredentialsRequest
{
    public string? id { get; set; }
    public string? name { get; set; }
    public string? apiKey { get; set; }
    public string? apiSecret { get; set; }
    public string? passphrase { get; set; }
    public bool isDemo { get; set; }
    public bool isActive { get; set; }
    public bool storeMinuteEquity { get; set; }
    public bool setActive { get; set; }
    public string? equityCurrency { get; set; }
}

public sealed record BybitWalletSnapshot(decimal totalEquityUsdt, decimal availableEquityUsdt, decimal unrealizedPnlUsdt, decimal marginRatio, List<object> balanceDetails);
