using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VANWebService.Auth;
using VANWebService.Models;
using VANWebService.Services;

namespace VANWebService;

public static class MexcEndpoints
{
    private static readonly Regex SpotSymbolPattern = new Regex("^[A-Z0-9]{2,40}(,[A-Z0-9]{2,40}){0,4}$", RegexOptions.Compiled);
    private static readonly Regex SingleSpotSymbolPattern = new Regex("^[A-Z0-9]{2,40}$", RegexOptions.Compiled);
    private static readonly Regex FuturesSymbolPattern = new Regex("^[A-Z0-9]{1,24}_[A-Z0-9]{1,24}$", RegexOptions.Compiled);
    private static readonly Regex StatesPattern = new Regex("^[1-5](,[1-5])*$", RegexOptions.Compiled);
    private static readonly HashSet<string> SpotIntervals = new HashSet<string>(StringComparer.Ordinal) { "1m", "5m", "15m", "30m", "60m", "4h", "1d", "1W", "1M" };
    private static readonly HashSet<string> FuturesIntervals = new HashSet<string>(StringComparer.Ordinal) { "Min1", "Min5", "Min15", "Min30", "Min60", "Hour4", "Hour8", "Day1", "Week1", "Month1" };
    private static readonly HashSet<int> SpotDepthLimits = new HashSet<int> { 5, 10, 20, 50, 100, 500, 1000, 5000 };

    public static void MapMexcEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/mexc/accounts", async (HttpContext context, MexcRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var accounts = await repo.GetAccountsAsync(false, context.RequestAborted);
            return Results.Json(new { items = accounts.ConvertAll(ToPublicAccount) });
        });

        app.MapPost("/api/admin/mexc/accounts", async (HttpContext context, MexcRepository repo, MexcCredentialsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            if (string.IsNullOrWhiteSpace(dto.name) || string.IsNullOrWhiteSpace(dto.apiKey) || string.IsNullOrWhiteSpace(dto.apiSecret)) return Results.BadRequest(new { message = "name, apiKey, apiSecret are required" });
            int id = await repo.CreateAccountAsync(dto.name, dto.apiKey, dto.apiSecret, dto.setActive || dto.isActive, context.RequestAborted);
            return Results.Json(new { ok = true, id });
        });

        app.MapPut("/api/admin/mexc/accounts/{id:int}", async (HttpContext context, MexcRepository repo, int id, MexcAccountUpdateRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = true;
            if (!string.IsNullOrWhiteSpace(dto.name)) ok = await repo.UpdateAccountAsync(id, dto.name, context.RequestAborted);
            if (ok && dto.isActive == true) ok = await repo.SetActiveAccountAsync(id, context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "MEXC account not found" });
        });

        app.MapPut("/api/admin/mexc/accounts/{id:int}/activate", async (HttpContext context, MexcRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.SetActiveAccountAsync(id, context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "MEXC account not found" });
        });

        app.MapDelete("/api/admin/mexc/accounts/{id:int}", async (HttpContext context, MexcRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.DeleteAccountAsync(id, context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "MEXC account not found" });
        });

        app.MapPost("/api/admin/mexc/test-credentials", async (HttpContext context, MexcCredentialsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = new MexcAccountRecord { name = dto.name ?? "MEXC", apiKey = dto.apiKey ?? "", apiSecret = dto.apiSecret ?? "" };
            if (string.IsNullOrWhiteSpace(account.apiKey) || string.IsNullOrWhiteSpace(account.apiSecret)) return Results.BadRequest(new { ok = false, message = "apiKey and apiSecret are required" });
            var client = new MexcApiClient(account);
            var spot = await TestPrivateCallAsync(context, () => client.GetSpotAccountAsync(context.RequestAborted));
            var futures = await TestPrivateCallAsync(context, () => client.GetFuturesAssetsAsync(context.RequestAborted));
            return Results.Json(new { ok = spot.ok || futures.ok, spot, futures });
        });

        MapPublicEndpoints(app);
        MapPrivateReadOnlyEndpoints(app);
        MapPrivateTradingEndpoints(app);
    }

    private static void MapPublicEndpoints(WebApplication app)
    {
        app.MapGet("/api/admin/mexc/public/spot/instruments", async System.Threading.Tasks.Task<IResult> (HttpContext context) =>
        {
            string symbol = OptionalSingleSpotSymbol(context.Request.Query["symbol"].ToString());
            return await UpstreamJsonAsync(context, c => c.GetSpotExchangeInfoAsync(symbol, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/public/spot/klines", async System.Threading.Tasks.Task<IResult> (HttpContext context) =>
        {
            if (!TryReadRequiredSingleSpotSymbol(context, out var symbol, out var error)) return error!;
            string interval = ReadString(context, "interval", "1m");
            if (!SpotIntervals.Contains(interval)) return Results.BadRequest(new { message = "Unsupported MEXC spot interval" });
            int limit = ReadInt(context, "limit", 500, 1, 1000);
            if (!TryReadTimeRange(context, "startTime", "endTime", out var startTime, out var endTime, out var timeError)) return timeError!;
            return await UpstreamJsonAsync(context, c => c.GetSpotKlinesAsync(symbol!, interval, limit, startTime, endTime, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/public/spot/orderbook", async System.Threading.Tasks.Task<IResult> (HttpContext context) =>
        {
            if (!TryReadRequiredSingleSpotSymbol(context, out var symbol, out var error)) return error!;
            int limit = ReadInt(context, "limit", 100, 5, 5000);
            if (!SpotDepthLimits.Contains(limit)) return Results.BadRequest(new { message = "Unsupported MEXC spot depth limit" });
            return await UpstreamJsonAsync(context, c => c.GetSpotDepthAsync(symbol!, limit, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/public/futures/instruments", async System.Threading.Tasks.Task<IResult> (HttpContext context) =>
        {
            string symbol = OptionalFuturesSymbol(context.Request.Query["symbol"].ToString());
            return await UpstreamJsonAsync(context, c => c.GetFuturesContractDetailAsync(symbol, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/public/futures/quote-symbols", async System.Threading.Tasks.Task<IResult> (HttpContext context) =>
        {
            return await UpstreamJsonAsync(context, c => c.GetFuturesTickerAsync(context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/public/futures/klines", async System.Threading.Tasks.Task<IResult> (HttpContext context) =>
        {
            if (!TryReadRequiredFuturesSymbol(context, out var symbol, out var error)) return error!;
            string interval = ReadString(context, "interval", "Min1");
            if (!FuturesIntervals.Contains(interval)) return Results.BadRequest(new { message = "Unsupported MEXC futures interval" });
            if (!TryReadTimeRange(context, "start", "end", out var start, out var end, out var timeError)) return timeError!;
            return await UpstreamJsonAsync(context, c => c.GetFuturesKlineAsync(symbol!, interval, start, end, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/public/futures/orderbook", async System.Threading.Tasks.Task<IResult> (HttpContext context) =>
        {
            if (!TryReadRequiredFuturesSymbol(context, out var symbol, out var error)) return error!;
            return await UpstreamJsonAsync(context, c => c.GetFuturesDepthAsync(symbol!, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/public/futures/funding", async System.Threading.Tasks.Task<IResult> (HttpContext context) =>
        {
            if (!TryReadRequiredFuturesSymbol(context, out var symbol, out var error)) return error!;
            return await UpstreamJsonAsync(context, c => c.GetFuturesFundingRateAsync(symbol!, context.RequestAborted));
        });
    }

    private static void MapPrivateReadOnlyEndpoints(WebApplication app)
    {
        app.MapGet("/api/admin/mexc/private/spot/account", async (HttpContext context, MexcRepository repo) =>
            await PrivateJsonAsync(context, repo, c => c.GetSpotAccountAsync(context.RequestAborted)));

        app.MapGet("/api/admin/mexc/private/spot/open-orders", async (HttpContext context, MexcRepository repo) =>
        {
            string symbol = OptionalSpotSymbol(context.Request.Query["symbol"].ToString());
            if (context.Request.Query.ContainsKey("symbol") && symbol == null) return Results.BadRequest(new { message = "Valid uppercase MEXC spot symbol or comma-separated symbols are required" });
            return await PrivateJsonAsync(context, repo, c => c.GetSpotOpenOrdersAsync(symbol, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/private/spot/all-orders", async (HttpContext context, MexcRepository repo) =>
        {
            if (!TryReadRequiredSingleSpotSymbol(context, out var symbol, out var error)) return error!;
            int? limit = ReadOptionalInt(context, "limit", 1, 1000, out var limitError);
            if (limitError != null) return limitError;
            if (!TryReadTimeRange(context, "startTime", "endTime", out var startTime, out var endTime, out var timeError)) return timeError!;
            return await PrivateJsonAsync(context, repo, c => c.GetSpotAllOrdersAsync(symbol!, limit, startTime, endTime, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/private/spot/trades", async (HttpContext context, MexcRepository repo) =>
        {
            if (!TryReadRequiredSingleSpotSymbol(context, out var symbol, out var error)) return error!;
            int? limit = ReadOptionalInt(context, "limit", 1, 100, out var limitError);
            if (limitError != null) return limitError;
            if (!TryReadTimeRange(context, "startTime", "endTime", out var startTime, out var endTime, out var timeError)) return timeError!;
            return await PrivateJsonAsync(context, repo, c => c.GetSpotMyTradesAsync(symbol!, limit, startTime, endTime, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/private/futures/assets", async (HttpContext context, MexcRepository repo) =>
            await PrivateJsonAsync(context, repo, c => c.GetFuturesAssetsAsync(context.RequestAborted)));

        app.MapGet("/api/admin/mexc/private/futures/positions", async (HttpContext context, MexcRepository repo) =>
        {
            string symbol = OptionalFuturesSymbol(context.Request.Query["symbol"].ToString());
            if (context.Request.Query.ContainsKey("symbol") && symbol == null) return Results.BadRequest(new { message = "Valid uppercase MEXC futures symbol is required, for example BTC_USDT" });
            return await PrivateJsonAsync(context, repo, c => c.GetFuturesOpenPositionsAsync(symbol, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/private/futures/open-orders", async (HttpContext context, MexcRepository repo) =>
        {
            if (!TryReadRequiredFuturesSymbol(context, out var symbol, out var error)) return error!;
            int pageNum = ReadInt(context, "page_num", 1, 1, 100000);
            int pageSize = ReadInt(context, "page_size", 20, 1, 100);
            return await PrivateJsonAsync(context, repo, c => c.GetFuturesOpenOrdersAsync(symbol!, pageNum, pageSize, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/private/futures/history-orders", async (HttpContext context, MexcRepository repo) =>
        {
            string symbol = OptionalFuturesSymbol(context.Request.Query["symbol"].ToString());
            if (context.Request.Query.ContainsKey("symbol") && symbol == null) return Results.BadRequest(new { message = "Valid uppercase MEXC futures symbol is required, for example BTC_USDT" });
            string states = ReadString(context, "states", "");
            if (!string.IsNullOrWhiteSpace(states) && !StatesPattern.IsMatch(states)) return Results.BadRequest(new { message = "states must contain values 1-5 separated by commas" });
            int? category = ReadOptionalInt(context, "category", 1, 4, out var categoryError);
            if (categoryError != null) return categoryError;
            int? side = ReadOptionalInt(context, "side", 1, 4, out var sideError);
            if (sideError != null) return sideError;
            if (!TryReadTimeRange(context, "start_time", "end_time", out var startTime, out var endTime, out var timeError)) return timeError!;
            int pageNum = ReadInt(context, "page_num", 1, 1, 100000);
            int pageSize = ReadInt(context, "page_size", 20, 1, 100);
            return await PrivateJsonAsync(context, repo, c => c.GetFuturesHistoryOrdersAsync(symbol, states, category, side, startTime, endTime, pageNum, pageSize, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/private/futures/order-deals", async (HttpContext context, MexcRepository repo) =>
        {
            if (!TryReadRequiredFuturesSymbol(context, out var symbol, out var error)) return error!;
            if (!TryReadTimeRange(context, "start_time", "end_time", out var startTime, out var endTime, out var timeError)) return timeError!;
            int pageNum = ReadInt(context, "page_num", 1, 1, 100000);
            int pageSize = ReadInt(context, "page_size", 20, 1, 100);
            return await PrivateJsonAsync(context, repo, c => c.GetFuturesOrderDealsAsync(symbol!, startTime, endTime, pageNum, pageSize, context.RequestAborted));
        });

        app.MapGet("/api/admin/mexc/private/futures/funding-records", async (HttpContext context, MexcRepository repo) =>
        {
            string symbol = OptionalFuturesSymbol(context.Request.Query["symbol"].ToString());
            if (context.Request.Query.ContainsKey("symbol") && symbol == null) return Results.BadRequest(new { message = "Valid uppercase MEXC futures symbol is required, for example BTC_USDT" });
            long? positionId = ReadOptionalNonNegativeLong(context, "position_id", out var positionError);
            if (positionError != null) return positionError;
            int pageNum = ReadInt(context, "page_num", 1, 1, 100000);
            int pageSize = ReadInt(context, "page_size", 20, 1, 100);
            return await PrivateJsonAsync(context, repo, c => c.GetFuturesFundingRecordsAsync(symbol, positionId, pageNum, pageSize, context.RequestAborted));
        });
    }

    private static void MapPrivateTradingEndpoints(WebApplication app)
    {
        app.MapPost("/api/admin/mexc/spot/orders/place", async (HttpContext context, MexcRepository repo, JsonElement payload) =>
            await PrivateJsonAsync(context, repo, c => c.PlaceSpotOrderAsync(payload, context.RequestAborted)));

        app.MapPost("/api/admin/mexc/spot/orders/cancel", async (HttpContext context, MexcRepository repo, JsonElement payload) =>
            await PrivateJsonAsync(context, repo, c => c.CancelSpotOrderAsync(payload, context.RequestAborted)));
    }

    private static async System.Threading.Tasks.Task<IResult> PrivateJsonAsync(HttpContext context, MexcRepository repo, Func<MexcApiClient, System.Threading.Tasks.Task<System.Text.Json.JsonDocument>> action)
    {
        if (!TryRequireAdmin(context, out var fail)) return fail!;
        var account = await ResolveAccountAsync(context, repo);
        if (account == null) return Results.NotFound(new { message = "No MEXC account selected" });
        try
        {
            var client = new MexcApiClient(account);
            using var doc = await action(client);
            return Results.Json(new { account = ToPublicAccount(account), data = doc.RootElement.Clone(), fetchedAtUtc = DateTime.UtcNow });
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { ok = false, message = ex.Message, account = ToPublicAccount(account) });
        }
    }

    private static async System.Threading.Tasks.Task<IResult> UpstreamJsonAsync(HttpContext context, Func<MexcApiClient, System.Threading.Tasks.Task<System.Text.Json.JsonDocument>> action)
    {
        try
        {
            var client = new MexcApiClient();
            using var doc = await action(client);
            return Results.Json(doc.RootElement.Clone());
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: 502);
        }
    }

    private static async System.Threading.Tasks.Task<MexcTestResult> TestPrivateCallAsync(HttpContext context, Func<System.Threading.Tasks.Task<System.Text.Json.JsonDocument>> action)
    {
        try
        {
            using var doc = await action();
            return new MexcTestResult { ok = true, data = doc.RootElement.Clone() };
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return new MexcTestResult { ok = false, message = "Request canceled" };
        }
        catch (InvalidOperationException ex)
        {
            return new MexcTestResult { ok = false, message = ex.Message };
        }
    }

    private static async System.Threading.Tasks.Task<MexcAccountRecord?> ResolveAccountAsync(HttpContext context, MexcRepository repo)
    {
        if (context.Request.Query.TryGetValue("accountId", out var raw) && int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) return await repo.GetAccountByIdAsync(id, context.RequestAborted);
        return await repo.GetActiveAccountAsync(context.RequestAborted);
    }

    private static bool TryRequireAdmin(HttpContext context, out IResult fail)
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

    private static bool TryReadRequiredSingleSpotSymbol(HttpContext context, out string symbol, out IResult error)
    {
        symbol = OptionalSingleSpotSymbol(context.Request.Query["symbol"].ToString());
        error = symbol == null ? Results.BadRequest(new { message = "Valid uppercase MEXC spot symbol is required" }) : null;
        return error == null;
    }

    private static bool TryReadRequiredFuturesSymbol(HttpContext context, out string symbol, out IResult error)
    {
        symbol = OptionalFuturesSymbol(context.Request.Query["symbol"].ToString());
        error = symbol == null ? Results.BadRequest(new { message = "Valid uppercase MEXC futures symbol is required, for example BTC_USDT" }) : null;
        return error == null;
    }

    private static string OptionalSpotSymbol(string value)
    {
        string symbol = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (symbol.Length == 0) return null;
        return SpotSymbolPattern.IsMatch(symbol) ? symbol : null;
    }

    private static string OptionalSingleSpotSymbol(string value)
    {
        string symbol = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (symbol.Length == 0) return null;
        return SingleSpotSymbolPattern.IsMatch(symbol) ? symbol : null;
    }

    private static string OptionalFuturesSymbol(string value)
    {
        string symbol = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (symbol.Length == 0) return null;
        return FuturesSymbolPattern.IsMatch(symbol) ? symbol : null;
    }

    private static string ReadString(HttpContext context, string name, string fallback)
    {
        string value = context.Request.Query[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int ReadInt(HttpContext context, string name, int fallback, int min, int max)
    {
        string raw = context.Request.Query[name].ToString();
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) value = fallback;
        return Math.Clamp(value, min, max);
    }

    private static int? ReadOptionalInt(HttpContext context, string name, int min, int max, out IResult error)
    {
        error = null;
        string raw = context.Request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            error = Results.BadRequest(new { message = name + " must be an integer between " + min.ToString(CultureInfo.InvariantCulture) + " and " + max.ToString(CultureInfo.InvariantCulture) });
            return null;
        }
        return value;
    }

    private static long? ReadOptionalNonNegativeLong(HttpContext context, string name, out IResult error)
    {
        error = null;
        string raw = context.Request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            error = Results.BadRequest(new { message = name + " must be a non-negative integer" });
            return null;
        }
        return value;
    }

    private static bool TryReadTimeRange(HttpContext context, string startName, string endName, out long? start, out long? end, out IResult error)
    {
        start = ReadOptionalNonNegativeLong(context, startName, out var startError);
        if (startError != null)
        {
            end = null;
            error = startError;
            return false;
        }
        end = ReadOptionalNonNegativeLong(context, endName, out var endError);
        if (endError != null)
        {
            error = endError;
            return false;
        }
        if (start.HasValue && end.HasValue && start.Value > end.Value)
        {
            error = Results.BadRequest(new { message = startName + " must be <= " + endName });
            return false;
        }
        error = null;
        return true;
    }

    private static MexcPublicAccount ToPublicAccount(MexcAccountRecord a) => new(a.id, a.name, MexcRepository.MaskApiKey(a.apiKey), a.isActive, a.createdAtUtc, a.updatedAtUtc);

    private sealed class MexcCredentialsRequest
    {
        public string? name { get; set; }
        public string? apiKey { get; set; }
        public string? apiSecret { get; set; }
        public bool setActive { get; set; }
        public bool isActive { get; set; }
    }

    private sealed class MexcAccountUpdateRequest
    {
        public string? name { get; set; }
        public bool? isActive { get; set; }
    }

    private sealed class MexcTestResult
    {
        public bool ok { get; set; }
        public string? message { get; set; }
        public object? data { get; set; }
    }

    private sealed record MexcPublicAccount(int id, string name, string apiKeyMasked, bool isActive, DateTime createdAtUtc, DateTime updatedAtUtc);
}
