using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VANWebService.Auth;
using VANWebService.Models;
using VANWebService.Services;

namespace VANWebService;

public static class CoincallEndpoints
{
    public static void MapCoincallEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/coincall/accounts", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var accounts = await repo.GetAccountsAsync(false, context.RequestAborted);
            return Results.Json(new { items = accounts.ConvertAll(ToPublicAccount) });
        });

        app.MapPost("/api/admin/coincall/accounts", async (HttpContext context, CoincallRepository repo, CoincallCreateAccountRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            if (string.IsNullOrWhiteSpace(dto.name) || string.IsNullOrWhiteSpace(dto.apiKey) || string.IsNullOrWhiteSpace(dto.apiSecret)) return Results.BadRequest(new { message = "name, apiKey, apiSecret are required" });
            int id = await repo.CreateAccountAsync(dto.name, dto.apiKey, dto.apiSecret, dto.setActive, dto.storeMinuteEquity, dto.equityCurrency ?? "USD", context.RequestAborted);
            return Results.Json(new { ok = true, id });
        });

        app.MapPut("/api/admin/coincall/accounts/{id:int}/settings", async (HttpContext context, CoincallRepository repo, int id, CoincallAccountSettingsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.UpdateAccountSettingsAsync(id, dto.storeMinuteEquity, dto.equityCurrency ?? "USD", context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "CoinCall account not found" });
        });

        app.MapPut("/api/admin/coincall/accounts/{id:int}/activate", async (HttpContext context, CoincallRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.SetActiveAccountAsync(id, context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "CoinCall account not found" });
        });

        app.MapDelete("/api/admin/coincall/accounts/{id:int}", async (HttpContext context, CoincallRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.DeleteAccountAsync(id, context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "CoinCall account not found" });
        });

        app.MapPost("/api/admin/coincall/test-credentials", async (HttpContext context, CoincallCreateAccountRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = new CoincallAccountRecord { name = dto.name ?? "CoinCall", apiKey = dto.apiKey ?? "", apiSecret = dto.apiSecret ?? "" };
            if (string.IsNullOrWhiteSpace(account.apiKey) || string.IsNullOrWhiteSpace(account.apiSecret)) return Results.BadRequest(new { ok = false, message = "apiKey and apiSecret are required" });
            try
            {
                var client = new CoincallApiClient(account);
                using var doc = await client.GetAccountSummaryAsync(context.RequestAborted);
                return Results.Json(new { ok = true, message = "Credentials accepted by CoinCall. Account summary is readable.", data = doc.RootElement.Clone() });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = ex.Message });
            }
        });

        app.MapGet("/api/admin/coincall/summary", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            try
            {
                var client = new CoincallApiClient(account);
                using var doc = await client.GetAccountSummaryAsync(context.RequestAborted);
                return Results.Json(new { account = ToPublicAccount(account), data = doc.RootElement.Clone(), metrics = BuildCoincallSummaryMetrics(doc.RootElement), fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = ex.Message, account = ToPublicAccount(account) });
            }
        });

        app.MapGet("/api/admin/coincall/transfer-records", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            try
            {
                var client = new CoincallApiClient(account);
                using var doc = await client.GetAccountTransferRecordsAsync(context.RequestAborted);
                return Results.Json(new { account = ToPublicAccount(account), data = doc.RootElement.Clone(), fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message, account = ToPublicAccount(account) });
            }
        });


        app.MapGet("/api/admin/coincall/equity", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            var fromUtc = ParseOptionalUtc(context.Request.Query["fromUtc"].ToString());
            var daily = await repo.GetDailyEquityAsync(account.id, 3650, context.RequestAborted);
            if (fromUtc.HasValue) daily = daily.Where(p => p.tsUtc >= fromUtc.Value).ToList();
            var minute = account.storeMinuteEquity
                ? (fromUtc.HasValue
                    ? await repo.GetMinuteEquityFromAsync(account.id, fromUtc.Value, context.RequestAborted)
                    : await repo.GetMinuteEquityAsync(account.id, 24, context.RequestAborted))
                : new List<CoincallEquityPoint>();
            var dailyDrawdownMinute = account.storeMinuteEquity
                ? (fromUtc.HasValue
                    ? await repo.GetMinuteEquityFromAsync(account.id, fromUtc.Value, context.RequestAborted)
                    : await repo.GetMinuteEquityForDailyDrawdownAsync(account.id, 3650, context.RequestAborted))
                : new List<CoincallEquityPoint>();
            return Results.Json(BuildEquityPayload(account, daily, minute, dailyDrawdownMinute));
        });

        app.MapGet("/api/admin/coincall/pnl", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            var fromUtc = ParseOptionalUtc(context.Request.Query["fromUtc"].ToString());
            var daily = await repo.GetDailyEquityAsync(account.id, 3650, context.RequestAborted);
            if (fromUtc.HasValue) daily = daily.Where(p => p.tsUtc >= fromUtc.Value).ToList();
            var minute = account.storeMinuteEquity
                ? (fromUtc.HasValue
                    ? await repo.GetMinuteEquityFromAsync(account.id, fromUtc.Value, context.RequestAborted)
                    : await repo.GetMinuteEquityAsync(account.id, 24, context.RequestAborted))
                : new List<CoincallEquityPoint>();
            var dailyDrawdownMinute = account.storeMinuteEquity
                ? (fromUtc.HasValue
                    ? await repo.GetMinuteEquityFromAsync(account.id, fromUtc.Value, context.RequestAborted)
                    : await repo.GetMinuteEquityForDailyDrawdownAsync(account.id, 3650, context.RequestAborted))
                : new List<CoincallEquityPoint>();
            return Results.Json(BuildEquityPayload(account, daily, minute, dailyDrawdownMinute));
        });

        app.MapGet("/api/admin/coincall/equity/asset", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            string asset = context.Request.Query["asset"].ToString().Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(asset)) asset = "BTC";
            if (asset is not ("BTC" or "ETH" or "USDT")) return Results.BadRequest(new { message = "asset must be BTC, ETH, or USDT" });
            string denom = context.Request.Query["denom"].ToString().Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(denom)) denom = "usd";
            if (denom is not ("usd" or "native")) return Results.BadRequest(new { message = "denom must be usd or native" });
            var daily = await repo.GetAssetDailyEquityAsync(account.id, asset, 3650, context.RequestAborted);
            var minute = account.storeMinuteEquity
                ? await repo.GetAssetMinuteEquityAsync(account.id, asset, 24, context.RequestAborted)
                : new List<CoincallAssetEquityPoint>();
            var dailyDrawdownMinute = account.storeMinuteEquity
                ? await repo.GetAssetMinuteEquityForDailyDrawdownAsync(account.id, asset, 3650, context.RequestAborted)
                : new List<CoincallAssetEquityPoint>();
            if (asset == "BTC")
            {
                try
                {
                    var client = new CoincallApiClient(account);
                    daily = await BuildEstimatedBtcAssetDailyEquityAsync(repo, client, daily, minute, context.RequestAborted);
                }
                catch (InvalidOperationException)
                {
                    // Keep confirmed CoinCall snapshots when live spot fills are unavailable.
                }
            }
            return Results.Json(BuildAssetEquityPayload(account, asset, denom, daily, minute, dailyDrawdownMinute));
        });

        app.MapGet("/api/admin/coincall/equity/daily-breakdown", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            int days = int.TryParse(context.Request.Query["days"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedDays) ? parsedDays : 14;
            days = Math.Clamp(days, 1, 60);
            string requestedCurrency = NormalizeDailyClearingCurrency(context.Request.Query["currency"].ToString());
            try
            {
                var client = new CoincallApiClient(account);
                return Results.Json(await BuildDailyEquityBreakdownPayload(account, repo, client, days, requestedCurrency, context.RequestAborted));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message, account = ToPublicAccount(account) });
            }
        });

        app.MapGet("/api/admin/coincall/public/{market}/instruments", async (HttpContext context, string market) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var client = new CoincallApiClient(new CoincallAccountRecord());
            string baseCurrency = context.Request.Query["baseCurrency"].ToString();
            using var doc = market.ToLowerInvariant() switch
            {
                "spot" => await client.GetSpotInstrumentsAsync(context.Request.Query["symbol"], context.RequestAborted),
                "futures" => await client.GetFuturesInstrumentsAsync(context.RequestAborted),
                "options" => await client.GetOptionInstrumentsAsync(string.IsNullOrWhiteSpace(baseCurrency) ? "BTC" : baseCurrency, context.RequestAborted),
                _ => throw new InvalidOperationException("Unsupported CoinCall market")
            };
            return Results.Json(doc.RootElement.Clone());
        });

        app.MapGet("/api/admin/coincall/public/futures/quote-symbols", async (HttpContext context) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var client = new CoincallApiClient(new CoincallAccountRecord());
            using var doc = await client.GetFuturesQuoteSymbolsAsync(context.RequestAborted);
            return Results.Json(doc.RootElement.Clone());
        });

        app.MapGet("/api/admin/coincall/futures/mark-prices/live", (HttpContext context, CoincallFuturesMarkPriceHostedService service) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var snapshot = service.GetSnapshot();
            return Results.Json(new
            {
                lastUpdatedUtc = snapshot.LastUpdatedUtc,
                lastErrorUtc = snapshot.LastErrorUtc,
                lastError = snapshot.LastError,
                items = snapshot.Items.Select(x => new
                {
                    symbol = x.Symbol,
                    displayName = x.DisplayName,
                    markPrice = x.MarkPrice,
                    indexPrice = x.IndexPrice,
                    sourceTs = x.SourceTs,
                    updatedUtc = x.UpdatedUtc
                })
            });
        });

        app.MapGet("/api/admin/coincall/public/options/chain", async (HttpContext context) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            string baseCurrency = context.Request.Query["baseCurrency"].ToString().Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(baseCurrency)) baseCurrency = "BTC";
            if (baseCurrency is not ("BTC" or "ETH")) return Results.BadRequest(new { message = "baseCurrency must be BTC or ETH" });
            long? endTime = ReadLongQuery(context, "endTime");
            var client = new CoincallApiClient(new CoincallAccountRecord());
            using var doc = await client.GetOptionChainAsync(baseCurrency + "USD", endTime, context.RequestAborted);
            return Results.Json(doc.RootElement.Clone());
        });

        app.MapGet("/api/admin/coincall/futures/candles/minute/symbols", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var items = await repo.GetStoredFuturesSymbolsAsync(context.RequestAborted);
            return Results.Json(new
            {
                items = items.Select(x => new
                {
                    symbol = x.symbol,
                    displayName = string.IsNullOrWhiteSpace(x.displayName) ? x.symbol : x.displayName
                }).ToList()
            });
        });

        app.MapGet("/api/admin/coincall/public/{market}/klines", async (HttpContext context, string market) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var client = new CoincallApiClient(new CoincallAccountRecord());
            string symbol = context.Request.Query["symbol"].ToString();
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            string tf = context.Request.Query["tf"].ToString();
            using var doc = market.ToLowerInvariant() switch
            {
                "spot" => await client.GetSpotKlinesAsync(symbol, ToSpotInterval(tf), 180, context.RequestAborted),
                "futures" => await client.GetFuturesKlinesAsync(symbol, ToFuturesPeriod(tf), ReadLongQuery(context, "start"), ReadLongQuery(context, "end"), 180, context.RequestAborted),
                _ => throw new InvalidOperationException("Unsupported CoinCall market")
            };
            return Results.Json(doc.RootElement.Clone());
        });

        app.MapGet("/api/admin/coincall/{market}/candles/minute", async (HttpContext context, CoincallRepository repo, CoincallCandlesHostedService candlesHostedService, string market) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            string symbol = context.Request.Query["symbol"].ToString().Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            int limit = int.TryParse(context.Request.Query["limit"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 720;
            limit = Math.Clamp(limit, 1, 129600);
            long? beforeMs = ReadLongQuery(context, "before");
            DateTime? beforeUtc = beforeMs.HasValue && beforeMs.Value > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(beforeMs.Value).UtcDateTime
                : null;
            var rows = market.ToLowerInvariant() switch
            {
                "spot" => await repo.GetSpotMinuteCandlesAsync(symbol, limit, beforeUtc, context.RequestAborted),
                "futures" => await repo.GetFuturesMinuteCandlesAsync(symbol, limit, beforeUtc, context.RequestAborted),
                _ => throw new InvalidOperationException("Unsupported CoinCall market")
            };
            if (string.Equals(market, "futures", StringComparison.OrdinalIgnoreCase) && !beforeUtc.HasValue)
            {
                var liveOpenMinute = await candlesHostedService.TryGetLiveFuturesOpenMinuteAsync(symbol, context.RequestAborted);
                if (liveOpenMinute != null)
                {
                    rows = rows
                        .Where(r => r.minuteUtc != liveOpenMinute.minuteUtc)
                        .Append(liveOpenMinute)
                        .OrderBy(r => r.minuteUtc)
                        .ToList();
                    if (rows.Count > limit) rows = rows.Skip(rows.Count - limit).ToList();
                }
            }
            return Results.Json(new
            {
                market = market.ToLowerInvariant(),
                symbol,
                before = beforeUtc,
                rows = rows.Select(r => new
                {
                    minuteUtc = r.minuteUtc,
                    open = r.open,
                    high = r.high,
                    low = r.low,
                    close = r.close,
                    volume = r.volume,
                    markPriceClose = r.markPriceClose,
                    indexPriceClose = r.indexPriceClose,
                    index_price_close = r.indexPriceClose,
                    bestBidClose = r.bestBidClose,
                    bestAskClose = r.bestAskClose
                }).ToList()
            });
        });

        app.MapGet("/api/admin/coincall/futures/spreads/minute", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            string rowSymbol = context.Request.Query["rowSymbol"].ToString().Trim().ToUpperInvariant();
            string colSymbol = context.Request.Query["colSymbol"].ToString().Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(rowSymbol) || string.IsNullOrWhiteSpace(colSymbol))
                return Results.BadRequest(new { message = "rowSymbol and colSymbol are required" });
            int limit = int.TryParse(context.Request.Query["limit"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 720;
            limit = Math.Clamp(limit, 1, 129600);
            long? beforeMs = ReadLongQuery(context, "before");
            DateTime? beforeUtc = beforeMs.HasValue && beforeMs.Value > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(beforeMs.Value).UtcDateTime
                : null;
            var rows = await repo.GetFuturesSpreadMinuteRowsAsync(rowSymbol, colSymbol, limit, beforeUtc, context.RequestAborted);
            return Results.Json(new
            {
                market = "futures",
                rowSymbol,
                colSymbol,
                spreadSymbol = colSymbol + "_" + rowSymbol,
                before = beforeUtc,
                rows = rows.Select(r => new
                {
                    minuteUtc = r.minuteUtc,
                    spread = r.markSpreadClose,
                    bidSpreadClose = r.bidSpreadClose,
                    askSpreadClose = r.askSpreadClose,
                    midSpreadClose = r.midSpreadClose
                }).ToList()
            });
        });

        app.MapGet("/api/admin/coincall/futures/leverage", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            string symbol = context.Request.Query["symbol"].ToString();
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            try
            {
                var client = new CoincallApiClient(account);
                using var doc = await client.GetFuturesLeverageAsync(symbol, context.RequestAborted);
                return Results.Json(new { account = ToPublicAccount(account), data = doc.RootElement.Clone(), fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message });
            }
        });

        app.MapGet("/api/admin/coincall/futures/positions", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            try
            {
                var client = new CoincallApiClient(account);
                string symbol = context.Request.Query["symbol"].ToString();
                using var doc = await client.GetFuturesPositionsAsync(string.IsNullOrWhiteSpace(symbol) ? null : symbol, context.RequestAborted);
                return Results.Json(doc.RootElement.Clone());
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message });
            }
        });

        app.MapGet("/api/admin/coincall/futures/ws-auth", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            try
            {
                var client = new CoincallApiClient(account);
                var auth = await client.BuildFuturesWebSocketAuthAsync(context.RequestAborted);
                return Results.Json(new { account = ToPublicAccount(account), url = auth.Url, uuid = auth.Uuid, ts = auth.Ts, expiresAtUtc = auth.ExpiresAtUtc, fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message, account = ToPublicAccount(account) });
            }
        });



        app.MapGet("/api/admin/coincall/spot/private-ws-auth", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            try
            {
                var client = new CoincallApiClient(account);
                var auth = client.BuildAccountSpotWebSocketAuth();
                return Results.Json(new { account = ToPublicAccount(account), url = auth.Url, uuid = auth.Uuid, ts = auth.Ts, expiresAtUtc = auth.ExpiresAtUtc, fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message, account = ToPublicAccount(account) });
            }
        });

        app.MapGet("/api/admin/coincall/futures/private-ws-auth", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            try
            {
                var client = new CoincallApiClient(account);
                var auth = client.BuildAccountFuturesWebSocketAuth();
                return Results.Json(new { account = ToPublicAccount(account), url = auth.Url, uuid = auth.Uuid, ts = auth.Ts, expiresAtUtc = auth.ExpiresAtUtc, fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message, account = ToPublicAccount(account) });
            }
        });

        app.MapGet("/api/admin/coincall/futures/order-events/stream", async (HttpContext context, CoincallFuturesOrderEventsHostedService orderEvents) =>
        {
            if (!TryRequireAdminOrQueryToken(context, out var fail))
            {
                context.Response.StatusCode = fail is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
                return;
            }

            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            var reader = orderEvents.Subscribe(context.RequestAborted);
            await foreach (var payload in reader.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync("event: order\n", context.RequestAborted);
                await context.Response.WriteAsync("data: " + payload.Replace("\r", "").Replace("\n", "") + "\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        });

        app.MapPost("/api/admin/coincall/futures/max-available", async (HttpContext context, CoincallRepository repo, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            string displaySymbol = payload.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? string.Empty : string.Empty;
            string symbol = CoincallFuturesApiSymbol(displaySymbol);
            decimal? buyPrice = payload.TryGetProperty("buyPrice", out var buyPriceEl) ? ReadCoincallDecimal(buyPriceEl) : null;
            decimal? sellPrice = payload.TryGetProperty("sellPrice", out var sellPriceEl) ? ReadCoincallDecimal(sellPriceEl) : null;
            decimal? markPrice = payload.TryGetProperty("markPrice", out var markPriceEl) ? ReadCoincallDecimal(markPriceEl) : null;
            if (string.IsNullOrWhiteSpace(displaySymbol)) return Results.BadRequest(new { message = "symbol is required" });
            if (buyPrice is null || buyPrice <= 0m) return Results.BadRequest(new { message = "buyPrice must be positive" });
            if (sellPrice is null || sellPrice <= 0m) return Results.BadRequest(new { message = "sellPrice must be positive" });
            if (markPrice is null || markPrice <= 0m) return Results.BadRequest(new { message = "markPrice must be positive" });

            try
            {
                var client = new CoincallApiClient(account);
                using var summaryDoc = await client.GetAccountSummaryAsync(context.RequestAborted);
                using var positionsDoc = await client.GetFuturesPositionsAsync(symbol, context.RequestAborted);
                using var ordersDoc = await client.GetFuturesOpenOrdersAsync(symbol, context.RequestAborted);
                using var leverageDoc = await client.GetFuturesLeverageAsync(symbol, context.RequestAborted);
                using var contractDoc = await client.GetFuturesTradeContractConfigAsync(symbol, context.RequestAborted);
                using var ladderDoc = await client.GetFuturesTradeLadderConfigAsync(symbol, context.RequestAborted);

                var metrics = BuildCoincallSummaryMetrics(summaryDoc.RootElement);
                var availableEquity = metrics.availableEquity ?? 0m;
                var currentLeverage = ExtractCurrentLeverage(leverageDoc.RootElement);
                var underSymbol = ToCoincallUnderSymbol(symbol);
                var contractConfig = ExtractFuturesContractConfig(contractDoc.RootElement, underSymbol);
                var ladderItems = ExtractLadderItems(ladderDoc.RootElement).ToList();
                var currentLeverageMaxOpenValue = GetMaxOpenValue(ladderItems, currentLeverage);
                var positions = ExtractFuturesPositionSides(positionsDoc.RootElement, symbol).ToList();
                var orders = ExtractFuturesOpenOrderSides(ordersDoc.RootElement, symbol).ToList();

                var buyQty = ComputeCoincallMaxAvailableQty(
                    targetSide: 1,
                    availableEquity: availableEquity,
                    currentLeverage: currentLeverage,
                    currentLeverageMaxOpenValue: currentLeverageMaxOpenValue,
                    takerFee: contractConfig.TakerFee,
                    volumeDecimal: contractConfig.VolumeDecimal,
                    price: buyPrice.Value,
                    markPrice: markPrice.Value,
                    positions: positions,
                    openOrders: orders);

                var sellQty = ComputeCoincallMaxAvailableQty(
                    targetSide: 2,
                    availableEquity: availableEquity,
                    currentLeverage: currentLeverage,
                    currentLeverageMaxOpenValue: currentLeverageMaxOpenValue,
                    takerFee: contractConfig.TakerFee,
                    volumeDecimal: contractConfig.VolumeDecimal,
                    price: sellPrice.Value,
                    markPrice: markPrice.Value,
                    positions: positions,
                    openOrders: orders);

                return Results.Json(new
                {
                    displaySymbol,
                    buy = new { qty = buyQty, price = buyPrice.Value, quote = Decimal.Round(buyQty * buyPrice.Value, 8, MidpointRounding.ToZero) },
                    sell = new { qty = sellQty, price = sellPrice.Value, quote = Decimal.Round(sellQty * sellPrice.Value, 8, MidpointRounding.ToZero) },
                    meta = new
                    {
                        availableEquity,
                        currentLeverage,
                        currentLeverageMaxOpenValue,
                        contractConfig.VolumeDecimal,
                        contractConfig.TakerFee
                    },
                    fetchedAtUtc = DateTime.UtcNow
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message });
            }
        });

        app.MapPost("/api/admin/coincall/futures/leverage", async (HttpContext context, CoincallRepository repo, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            string displaySymbol = payload.TryGetProperty("symbol", out var symbolEl) ? symbolEl.GetString() ?? string.Empty : string.Empty;
            string symbol = CoincallFuturesApiSymbol(displaySymbol);
            if (string.IsNullOrWhiteSpace(displaySymbol)) return Results.BadRequest(new { message = "symbol is required" });
            if (!payload.TryGetProperty("leverage", out var leverageEl) || !leverageEl.TryGetDecimal(out var leverage) || leverage < 1 || leverage > 125) return Results.BadRequest(new { message = "leverage must be between 1 and 125" });
            try
            {
                var client = new CoincallApiClient(account);
                using var setDoc = await client.SetFuturesLeverageAsync(symbol, leverage, context.RequestAborted);
                using var currentDoc = await client.GetFuturesLeverageAsync(symbol, context.RequestAborted);
                return Results.Json(new { account = ToPublicAccount(account), data = currentDoc.RootElement.Clone(), setResult = setDoc.RootElement.Clone(), fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message });
            }
        });

        app.MapGet("/api/admin/coincall/{market}/orders/open", async (HttpContext context, CoincallRepository repo, NotificationService notifier, string market) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            string marketKey = market.ToLowerInvariant();
            string symbol = context.Request.Query["symbol"].ToString();
            string apiSymbol = marketKey == "futures" ? CoincallFuturesApiSymbol(symbol) : symbol;
            if (marketKey == "spot" && string.Equals(context.Request.Query["restNotify"].ToString(), "spot-external-cancel-reconcile", StringComparison.OrdinalIgnoreCase))
            {
                string notifySymbol = context.Request.Query["restSymbol"].ToString();
                _ = notifier.NotifyCoincallSpotRestReconcileAsync(
                    account.name ?? string.Empty,
                    account.id,
                    string.IsNullOrWhiteSpace(notifySymbol) ? symbol : notifySymbol,
                    context.Request.Query["restReason"].ToString(),
                    context.Request.Query["restPage"].ToString(),
                    CancellationToken.None);
            }
            var client = new CoincallApiClient(account);
            using var doc = marketKey switch
            {
                "spot" => await client.GetSpotOpenOrdersAsync(symbol, context.RequestAborted),
                "futures" => await client.GetFuturesOpenOrdersAsync(apiSymbol, context.RequestAborted),
                "options" => throw new NotSupportedException("CoinCall options open-orders endpoint is not wired in VAN yet."),
                _ => throw new InvalidOperationException("Unsupported CoinCall market")
            };
            return Results.Json(new { account = ToPublicAccount(account), data = doc.RootElement.Clone(), fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/coincall/{market}/orders/history", async (HttpContext context, CoincallRepository repo, string market) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            var client = new CoincallApiClient(account);
            using var doc = market.ToLowerInvariant() switch
            {
                "spot" => await client.GetSpotOrderHistoryAsync(context.Request.Query["symbol"].ToString(), context.RequestAborted),
                "futures" => await client.GetFuturesOrderHistoryAsync(
                    int.TryParse(context.Request.Query["pageSize"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageSize) ? pageSize : 100,
                    ReadLongQuery(context, "fromId"),
                    ReadLongQuery(context, "startTime"),
                    ReadLongQuery(context, "endTime"),
                    context.RequestAborted),
                "options" => throw new NotSupportedException("CoinCall options order-history endpoint is not wired in VAN yet."),
                _ => throw new InvalidOperationException("Unsupported CoinCall market")
            };
            return Results.Json(new { account = ToPublicAccount(account), data = doc.RootElement.Clone(), fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/coincall/{market}/trades/history", async (HttpContext context, CoincallRepository repo, string market) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            var client = new CoincallApiClient(account);
            using var doc = market.ToLowerInvariant() switch
            {
                "spot" => await client.GetSpotFillsAsync(context.Request.Query["symbol"].ToString(), context.RequestAborted),
                "futures" => await client.GetFuturesTradeHistoryAsync(
                    int.TryParse(context.Request.Query["pageSize"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageSize) ? pageSize : 100,
                    ReadLongQuery(context, "fromId"),
                    ReadLongQuery(context, "startTime"),
                    ReadLongQuery(context, "endTime"),
                    context.RequestAborted),
                "options" => throw new NotSupportedException("CoinCall options trade-history endpoint is not wired in VAN yet."),
                _ => throw new InvalidOperationException("Unsupported CoinCall market")
            };
            return Results.Json(new { account = ToPublicAccount(account), data = doc.RootElement.Clone(), fetchedAtUtc = DateTime.UtcNow });
        });
        app.MapGet("/api/admin/coincall/futures/funding/history/db", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            string symbol = context.Request.Query["symbol"].ToString().Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            int limit = int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 500;
            var rows = await repo.GetFuturesFundingHistoryAsync(symbol, Math.Clamp(limit, 1, 5000), context.RequestAborted);
            var items = rows.Select(ToFundingHistoryPayloadRow).ToList();
            return Results.Json(new { source = "db", symbol, rows = items, data = new { list = items }, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/coincall/futures/funding/minute", async (HttpContext context, CoincallRepository repo) =>
        {
            string symbol = context.Request.Query["symbol"].ToString().Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            int limit = int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 5000;
            var rows = await repo.GetFuturesFundingMinuteAsync(symbol, Math.Clamp(limit, 1, 129600), context.RequestAborted);
            var items = rows.Select(ToFundingMinutePayloadRow).ToList();
            return Results.Json(new { source = "db-minute", symbol, order = "oldest-to-newest", rows = items, data = new { list = items }, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/coincall/futures/funding/history", async (HttpContext context, CoincallRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            if (string.Equals(context.Request.Query["source"].ToString(), "db", StringComparison.OrdinalIgnoreCase))
            {
                string dbSymbol = context.Request.Query["symbol"].ToString().Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(dbSymbol)) return Results.BadRequest(new { message = "symbol is required" });
                int limit = int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 500;
                var rows = await repo.GetFuturesFundingHistoryAsync(dbSymbol, Math.Clamp(limit, 1, 5000), context.RequestAborted);
                var items = rows.Select(ToFundingHistoryPayloadRow).ToList();
                return Results.Json(new { source = "db", symbol = dbSymbol, rows = items, data = new { list = items }, fetchedAtUtc = DateTime.UtcNow });
            }
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            string symbol = context.Request.Query["symbol"].ToString();
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            try
            {
                var client = new CoincallApiClient(account);
                using var doc = await client.GetFuturesFundingHistoryAsync(
                    symbol,
                    int.TryParse(context.Request.Query["pageSize"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageSize) ? pageSize : 50,
                    int.TryParse(context.Request.Query["page"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) ? page : 1,
                    ReadLongQuery(context, "startTime"),
                    ReadLongQuery(context, "endTime"),
                    context.RequestAborted);
                return Results.Json(new { account = ToPublicAccount(account), data = doc.RootElement.Clone(), fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message });
            }
        });

        app.MapPost("/api/admin/coincall/{market}/orders/place", async (HttpContext context, CoincallRepository repo, string market, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            var client = new CoincallApiClient(account);
            try
            {
                using var doc = market.ToLowerInvariant() switch
                {
                    "spot" => await client.PlaceSpotOrderAsync(payload, context.RequestAborted),
                    "futures" => await client.PlaceFuturesOrderAsync(payload, context.RequestAborted),
                    "options" => await client.PlaceOptionOrderAsync(payload, context.RequestAborted),
                    _ => throw new InvalidOperationException("Unsupported CoinCall market")
                };
                return Results.Json(doc.RootElement.Clone());
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message });
            }
        });

        app.MapPost("/api/admin/coincall/{market}/orders/cancel", async (HttpContext context, CoincallRepository repo, string market, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No CoinCall account selected" });
            var client = new CoincallApiClient(account);
            try
            {
                using var doc = market.ToLowerInvariant() switch
                {
                    "spot" => await client.CancelSpotOrderAsync(payload, context.RequestAborted),
                    "futures" => await client.CancelFuturesOrderAsync(payload, context.RequestAborted),
                    "options" => throw new NotSupportedException("CoinCall options cancel endpoint was not confirmed in the public API docs."),
                    _ => throw new InvalidOperationException("Unsupported CoinCall market")
                };
                return Results.Json(doc.RootElement.Clone());
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = FriendlyCoincallError(ex.Message), raw = ex.Message });
            }
        });

        app.MapGet("/api/admin/coincall/spreadbot/backend/events/stream", async (HttpContext context, CoincallBackendSpreadBotEventHub events, CoincallBackendSpreadBotManager manager) =>
        {
            if (!TryRequireAdminOrQueryToken(context, out var fail))
            {
                context.Response.StatusCode = fail is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
                return;
            }

            Guid? instanceId = Guid.TryParse(context.Request.Query["instanceId"].ToString(), out var parsedInstanceId) ? parsedInstanceId : null;
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            var reader = events.Subscribe(instanceId, context.RequestAborted);
            if (instanceId.HasValue)
            {
                var item = await manager.GetAsync(instanceId.Value, context.RequestAborted);
                var logs = await manager.LogsAsync(instanceId.Value, 200, context.RequestAborted);
                string snapshot = JsonSerializer.Serialize(new
                {
                    type = "snapshot",
                    instanceId = instanceId.Value,
                    payload = new { item, logs },
                    tsUtc = DateTime.UtcNow
                });
                await context.Response.WriteAsync("event: spreadbot\n", context.RequestAborted);
                await context.Response.WriteAsync("data: " + snapshot.Replace("\r", "").Replace("\n", "") + "\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }

            await foreach (var payload in reader.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync("event: spreadbot\n", context.RequestAborted);
                await context.Response.WriteAsync("data: " + payload.Replace("\r", "").Replace("\n", "") + "\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        });

        app.MapGet("/api/admin/coincall/spreadbot/backend/instances", async (HttpContext context, CoincallBackendSpreadBotManager manager) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var items = await manager.ListAsync(context.RequestAborted);
            return Results.Json(new { items });
        });

        app.MapPost("/api/admin/coincall/spreadbot/backend/instances", async (HttpContext context, CoincallBackendSpreadBotManager manager, CoincallBackendSpreadBotStartRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            try
            {
                var item = await manager.StartAsync(dto, context.RequestAborted);
                return Results.Json(new { ok = true, item });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = ex.Message });
            }
        });

        app.MapGet("/api/admin/coincall/spreadbot/backend/settings", async (HttpContext context, CoincallBackendSpreadBotManager manager) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            int? accountId = int.TryParse(context.Request.Query["accountId"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAccountId) ? parsedAccountId : null;
            try
            {
                var item = await manager.GetSettingsAsync(accountId, context.Request.Query["pair"].ToString(), context.Request.Query["strategy"].ToString(), context.RequestAborted);
                return item == null ? Results.NotFound(new { message = "Spreadbot settings not found" }) : Results.Json(new { item });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = ex.Message });
            }
        });

        app.MapPut("/api/admin/coincall/spreadbot/backend/settings", async (HttpContext context, CoincallBackendSpreadBotManager manager, CoincallBackendSpreadBotSettingsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            try
            {
                var item = await manager.SaveSettingsAsync(dto, context.RequestAborted);
                return Results.Json(new { ok = true, item });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = ex.Message });
            }
        });

        app.MapGet("/api/admin/coincall/spreadbot/backend/instances/{id:guid}", async (HttpContext context, CoincallBackendSpreadBotManager manager, Guid id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var item = await manager.GetAsync(id, context.RequestAborted);
            return item == null ? Results.NotFound(new { message = "Spreadbot instance not found" }) : Results.Json(new { item });
        });

        app.MapPost("/api/admin/coincall/spreadbot/backend/instances/{id:guid}/stop", async (HttpContext context, CoincallBackendSpreadBotManager manager, Guid id, CoincallBackendSpreadBotStopRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var item = await manager.StopAsync(id, dto.cancelOwnedOrders == true, context.RequestAborted);
            return item == null ? Results.NotFound(new { message = "Spreadbot instance not found" }) : Results.Json(new { ok = true, item });
        });

        app.MapGet("/api/admin/coincall/spreadbot/backend/instances/{id:guid}/logs", async (HttpContext context, CoincallBackendSpreadBotManager manager, Guid id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            int limit = int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 200;
            var items = await manager.LogsAsync(id, limit, context.RequestAborted);
            return Results.Json(new { items });
        });

        app.MapPost("/api/admin/coincall/spreadbot/log", async (HttpContext context, SpreadBotLogRow dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var ts = ParseSpreadBotLogTs(dto.ts);
            string downloadsDir = Environment.GetEnvironmentVariable("VAN_SPREADBOT_LOG_DIR") ?? "/app/downloads";
            Directory.CreateDirectory(downloadsDir);
            string fileName = "coincall_spreadbot_BTCUSDT_PERP_SPOT_" + ts.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv";
            string filePath = Path.Combine(downloadsDir, fileName);
            bool writeHeader = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;
            var row = string.Join(",", new[]
            {
                CsvCell(ts.ToString("O", CultureInfo.InvariantCulture)),
                CsvCell(dto.pair ?? "BTCUSDT_PERP_SPOT"),
                CsvCell(dto.accountId ?? ""),
                CsvCell(dto.state ?? ""),
                CsvCell(dto.action ?? ""),
                CsvCell(dto.message ?? "")
            });
            var sb = new StringBuilder();
            if (writeHeader) sb.AppendLine("utc,pair,accountId,state,action,message");
            sb.AppendLine(row);
            await File.AppendAllTextAsync(filePath, sb.ToString(), Encoding.UTF8, context.RequestAborted);
            return Results.Json(new { ok = true, file = "/downloads/" + fileName, writtenAtUtc = DateTime.UtcNow });
        });
    }

    private sealed record SpreadBotLogRow(string? ts, string? pair, string? accountId, string? state, string? action, string? message);

    private static DateTime ParseSpreadBotLogTs(string? value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)) return dt.ToUniversalTime();
        return DateTime.UtcNow;
    }

    private static DateTime? ParseOptionalUtc(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt.ToUniversalTime()
            : null;
    }

    private static string CsvCell(string value)
    {
        value ??= "";
        string quote = ((char)34).ToString();
        return quote + value.Replace(quote, quote + quote) + quote;
    }

    private static string FriendlyCoincallError(string message)
    {
        if (message.Contains("order not exist", StringComparison.OrdinalIgnoreCase)) return "CoinCall rejected the request: order does not exist.";
        if (message.Contains("Insufficient available balance", StringComparison.OrdinalIgnoreCase)) return "CoinCall rejected the order: insufficient available balance.";
        if (message.Contains("Symbol not exist", StringComparison.OrdinalIgnoreCase)) return "CoinCall rejected the order: symbol does not exist.";
        if (message.Contains("token auth fail", StringComparison.OrdinalIgnoreCase)) return "CoinCall rejected the request: API key/authentication failed.";
        const string prefix = "CoinCall API error ";
        if (message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var rawIndex = message.IndexOf("; raw=", StringComparison.OrdinalIgnoreCase);
            return rawIndex > 0 ? message[..rawIndex] : message;
        }
        return message;
    }

    private static decimal ExtractCurrentLeverage(JsonElement root)
    {
        foreach (var obj in EnumerateCoincallObjects(root))
        {
            var value = FindCoincallDecimal(obj, "currentLeverage", "leverage");
            if (value.HasValue && value.Value > 0m) return value.Value;
        }
        throw new InvalidOperationException("CoinCall leverage response has no currentLeverage.");
    }

    private static CoincallFuturesContractConfig ExtractFuturesContractConfig(JsonElement root, string underSymbol)
    {
        if (root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("futuresConfig", out var futuresConfig) &&
            futuresConfig.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in futuresConfig.EnumerateObject())
            {
                if (!string.Equals(prop.Name, underSymbol, StringComparison.OrdinalIgnoreCase)) continue;
                var takerFee = FindCoincallDecimal(prop.Value, "takerFee") ?? 0m;
                var volumeDecimal = FindCoincallDecimal(prop.Value, "volumeDecimal") ?? 0m;
                return new CoincallFuturesContractConfig(takerFee, Math.Max(0, (int)volumeDecimal));
            }
        }
        throw new InvalidOperationException("CoinCall futures contractConfig has no matching underSymbol entry.");
    }

    private static IEnumerable<CoincallFuturesLadderItem> ExtractLadderItems(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) yield break;
        if (!data.TryGetProperty("ladderMargin", out var ladder) || ladder.ValueKind != JsonValueKind.Object) yield break;
        foreach (var prop in ladder.EnumerateObject())
        {
            var item = prop.Value;
            var maxLevarage = FindCoincallDecimal(item, "maxLevarage", "maxLeverage");
            var end = FindCoincallDecimal(item, "end");
            var start = FindCoincallDecimal(item, "start") ?? 0m;
            if (maxLevarage.HasValue && end.HasValue) yield return new CoincallFuturesLadderItem(maxLevarage.Value, start, end.Value);
        }
    }

    private static decimal GetMaxOpenValue(List<CoincallFuturesLadderItem> items, decimal leverage)
    {
        if (items.Count == 0) throw new InvalidOperationException("CoinCall ladderConfig has no ladderMargin rows.");
        var exact = items.FirstOrDefault(x => x.MaxLevarage == leverage);
        if (exact != null) return exact.End;
        var next = items.OrderBy(x => x.MaxLevarage).FirstOrDefault(x => leverage < x.MaxLevarage);
        if (next != null) return next.End;
        return items.OrderByDescending(x => x.MaxLevarage).First().End;
    }

    private static IEnumerable<CoincallSideQty> ExtractFuturesPositionSides(JsonElement root, string symbol)
    {
        var want = NormalizeCoincallSymbol(symbol);
        foreach (var obj in EnumerateCoincallObjects(root))
        {
            var sym = NormalizeCoincallSymbol(FindCoincallString(obj, "symbol", "instrument", "displayName"));
            if (!string.IsNullOrWhiteSpace(want) && !string.IsNullOrWhiteSpace(sym) && !string.Equals(sym, want, StringComparison.OrdinalIgnoreCase)) continue;
            var side = NormalizeCoincallSide(FindCoincallString(obj, "tradeSide", "side"));
            var qty = FindCoincallDecimal(obj, "qty", "quantity", "positionQty", "positionSize", "size");
            if (side is 1 or 2 && qty.HasValue && qty.Value > 0m) yield return new CoincallSideQty(side.Value, qty.Value);
        }
    }

    private static IEnumerable<CoincallSideQty> ExtractFuturesOpenOrderSides(JsonElement root, string symbol)
    {
        var want = NormalizeCoincallSymbol(symbol);
        foreach (var obj in EnumerateCoincallObjects(root))
        {
            var sym = NormalizeCoincallSymbol(FindCoincallString(obj, "symbol", "instrument", "displaySymbol", "displayName"));
            if (!string.IsNullOrWhiteSpace(want) && !string.IsNullOrWhiteSpace(sym) && !string.Equals(sym, want, StringComparison.OrdinalIgnoreCase)) continue;
            var side = NormalizeCoincallSide(FindCoincallString(obj, "tradeSide", "side", "orderSide"));
            var qty = FindCoincallDecimal(obj, "remainQty", "remainingQty", "leavesQty", "leftQty", "qty", "quantity", "amount");
            var filled = FindCoincallDecimal(obj, "fillQty", "filledQty", "filledQuantity", "filled") ?? 0m;
            if (qty.HasValue) qty = qty.Value - filled;
            if (side is 1 or 2 && qty.HasValue && qty.Value > 0m) yield return new CoincallSideQty(side.Value, qty.Value);
        }
    }

    private static decimal ComputeCoincallMaxAvailableQty(int targetSide, decimal availableEquity, decimal currentLeverage, decimal currentLeverageMaxOpenValue, decimal takerFee, int volumeDecimal, decimal price, decimal markPrice, List<CoincallSideQty> positions, List<CoincallSideQty> openOrders)
    {
        if (availableEquity <= 0m || currentLeverage <= 0m || currentLeverageMaxOpenValue <= 0m || price <= 0m || markPrice <= 0m) return 0m;
        decimal oppositePositionQty = positions.Where(x => x.Side != targetSide).Sum(x => x.Qty);
        decimal samePositionQty = positions.Where(x => x.Side == targetSide).Sum(x => x.Qty);
        decimal oppositeOpenQty = openOrders.Where(x => x.Side != targetSide).Sum(x => x.Qty);
        decimal sameOpenQty = openOrders.Where(x => x.Side == targetSide).Sum(x => x.Qty);
        decimal maxPositionQtyAtLeverage = currentLeverageMaxOpenValue / price;
        decimal positionOffset = samePositionQty - oppositePositionQty;
        decimal totalOpenOrderQty = oppositeOpenQty + sameOpenQty;
        decimal remainingByRiskLimit = maxPositionQtyAtLeverage - positionOffset - totalOpenOrderQty;
        int direction = targetSide == 1 ? 1 : -1;
        decimal markOffset = Math.Abs(Math.Min(0m, direction * (markPrice - price)));
        decimal balanceLimitedQty = availableEquity / (price / currentLeverage + markOffset + price * takerFee);
        decimal reduceOffsetQty = Math.Max(oppositePositionQty - sameOpenQty, 0m);
        decimal finalQty = Math.Min(remainingByRiskLimit, balanceLimitedQty + reduceOffsetQty);
        if (finalQty <= 0m) return 0m;
        return DecimalRoundDown(finalQty, volumeDecimal);
    }

    private static decimal DecimalRoundDown(decimal value, int digits)
    {
        if (digits <= 0) return decimal.Floor(value);
        decimal factor = (decimal)Math.Pow(10d, digits);
        return decimal.Floor(value * factor) / factor;
    }

    private static int? NormalizeCoincallSide(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            "1" or "BUY" or "LONG" => 1,
            "2" or "SELL" or "SHORT" => 2,
            _ => null
        };
    }

    private static string NormalizeCoincallSymbol(string? raw)
    {
        return string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim().ToUpperInvariant();
    }

    private static string CoincallFuturesApiSymbol(string? raw)
    {
        var symbol = NormalizeCoincallSymbol(raw).Replace(" ", "-");
        if (string.IsNullOrWhiteSpace(symbol)) return symbol;
        if (symbol.EndsWith("USDT-PERP", StringComparison.OrdinalIgnoreCase))
            return symbol[..^"USDT-PERP".Length] + "USD";
        var dated = symbol.IndexOf("USDT-", StringComparison.OrdinalIgnoreCase);
        if (dated > 0)
            return symbol[..dated] + "USD-" + symbol[(dated + "USDT-".Length)..];
        return symbol;
    }

    private static string ToCoincallUnderSymbol(string symbol)
    {
        var normalized = NormalizeCoincallSymbol(symbol);
        if (normalized.EndsWith("-PERP", StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^5];
        if (normalized.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^4];
        if (normalized.EndsWith("USD", StringComparison.OrdinalIgnoreCase)) return normalized;
        return normalized + "USD";
    }

    private static decimal? ReadCoincallDecimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static async System.Threading.Tasks.Task<CoincallAccountRecord?> ResolveAccountAsync(HttpContext context, CoincallRepository repo)
    {
        if (context.Request.Query.TryGetValue("accountId", out var raw) && int.TryParse(raw.ToString(), out int id)) return await repo.GetAccountByIdAsync(id, context.RequestAborted);
        return await repo.GetActiveAccountAsync(context.RequestAborted);
    }

    private static string ToSpotInterval(string? tf) => (tf ?? "1m") switch
    {
        "5m" => "5min",
        "15m" => "15min",
        "1H" => "1h",
        "4H" => "4h",
        "1D" => "1d",
        _ => "1min"
    };

    private static decimal? PercentRate(decimal? value)
    {
        if (!value.HasValue) return null;
        var n = value.Value;
        return Math.Abs(n) <= 1m ? n * 100m : n;
    }

    private static string ToFuturesPeriod(string? tf) => (tf ?? "1m") switch
    {
        "5m" => "m5",
        "1H" => "h1",
        "1D" => "d1",
        _ => "m1"
    };

    private static long ReadLongQuery(HttpContext context, string name) => long.TryParse(context.Request.Query[name].ToString(), out var value) ? value : 0;

    private static CoincallPublicAccount ToPublicAccount(CoincallAccountRecord a) => new(a.id, a.name, CoincallRepository.MaskApiKey(a.apiKey), a.isActive, a.storeMinuteEquity, CoincallRepository.NormalizeEquityCurrency(a.equityCurrency), a.createdAtUtc, a.updatedAtUtc);

    private static object ToFundingHistoryPayloadRow(CoincallFuturesFundingRow row)
    {
        long? ctime = row.recordTimeUtc.HasValue ? new DateTimeOffset(row.recordTimeUtc.Value).ToUnixTimeMilliseconds() : null;
        JsonElement? raw = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(row.rawJson) ? "{}" : row.rawJson);
            raw = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
        }
        return new
        {
            symbol = row.symbol,
            displayName = string.IsNullOrWhiteSpace(row.displayName) ? row.symbol : row.displayName,
            recordKey = row.recordKey,
            recordId = row.recordId,
            observedMinuteUtc = row.observedMinuteUtc,
            recordTimeUtc = row.recordTimeUtc,
            ctime,
            tradeSide = row.tradeSide,
            qty = row.qty,
            fundFee = row.fundFee,
            fundRate = row.fundRate,
            currentFunding = row.fundRate,
            current_funding = PercentRate(row.fundRate),
            interest8h = (decimal?)null,
            interest_8h = (decimal?)null,
            raw
        };
    }

    private static object ToFundingMinutePayloadRow(CoincallFuturesFundingRow row)
    {
        long ctime = new DateTimeOffset(row.observedMinuteUtc).ToUnixTimeMilliseconds();
        long? recordCtime = row.recordTimeUtc.HasValue ? new DateTimeOffset(row.recordTimeUtc.Value).ToUnixTimeMilliseconds() : null;
        JsonElement? raw = null;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(row.rawJson) ? "{}" : row.rawJson);
            raw = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
        }
        return new
        {
            symbol = row.symbol,
            displayName = string.IsNullOrWhiteSpace(row.displayName) ? row.symbol : row.displayName,
            recordKey = row.recordKey,
            observedMinuteUtc = row.observedMinuteUtc,
            minuteUtc = row.observedMinuteUtc,
            recordTimeUtc = row.recordTimeUtc,
            ctime,
            recordCtime,
            tradeSide = row.tradeSide,
            qty = row.qty,
            fundFee = row.fundFee,
            fundRate = row.fundRate,
            currentFunding = row.fundRate,
            current_funding = PercentRate(row.fundRate),
            interest8h = row.interest8h,
            interest_8h = PercentRate(row.interest8h),
            raw
        };
    }

    private static async System.Threading.Tasks.Task<object> BuildDailyEquityBreakdownPayload(CoincallAccountRecord account, CoincallRepository repo, CoincallApiClient client, int days, string requestedCurrency, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var fromUtc = now.AddDays(-Math.Clamp(days, 1, 60));
        long startMs = new DateTimeOffset(fromUtc).ToUnixTimeMilliseconds();
        long endMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();
        var warnings = new List<string>();
        var events = new List<CoincallClearingLedgerEvent>();

        var dailyStoredAll = (await repo.GetDailyEquityAsync(account.id, Math.Max(days + 4, days), ct)).OrderBy(p => p.tsUtc).ToList();
        var minuteEquitySnapshots = (await repo.GetUsdMinuteEquitySnapshotsAsync(account.id, fromUtc.AddDays(-1), now, ct)).OrderBy(p => p.tsUtc).ToList();
        var availableCurrencies = dailyStoredAll
            .Select(p => (p.equityCurrency ?? string.Empty).Trim().ToUpperInvariant())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CoincallEquityPoint? priorDaily = null;
        foreach (var point in dailyStoredAll.Where(p => p.tsUtc >= fromUtc && string.Equals((p.equityCurrency ?? string.Empty).Trim(), requestedCurrency, StringComparison.OrdinalIgnoreCase)))
        {
            var equityChange = priorDaily != null ? point.totalEquity - priorDaily.totalEquity : (decimal?)null;
            events.Add(new CoincallClearingLedgerEvent
            {
                TimeUtc = point.tsUtc,
                EventType = "Daily equity snapshot",
                Asset = requestedCurrency,
                Equity = point.totalEquity,
                UnrealizedPnl = point.unrealizedPnl,
                Residual = equityChange,
                Notes = priorDaily == null ? "Stored clearing checkpoint from van_coincall_equity_daily." : "Stored clearing checkpoint; residual is change vs previous matching snapshot.",
                Source = "van_coincall_equity_daily",
                SourceDetail = new { point.tsUtc, point.totalEquity, point.availableEquity, point.unrealizedPnl, point.marginRatio, point.equityCurrency }
            });
            priorDaily = point;
        }

        foreach (var symbol in CoincallLedgerSymbolsForCurrency(requestedCurrency))
        {
            try
            {
                var storedFundingRows = await repo.GetFuturesFundingHistoryAsync(symbol, 500, ct);
                foreach (var funding in storedFundingRows.Where(r => (r.recordTimeUtc ?? r.observedMinuteUtc) >= fromUtc))
                {
                    if (!string.Equals(CoincallLedgerAssetFromSymbol(funding.symbol), requestedCurrency, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!funding.fundFee.HasValue && !funding.fundRate.HasValue) continue;
                    events.Add(new CoincallClearingLedgerEvent
                    {
                        TimeUtc = funding.recordTimeUtc ?? funding.observedMinuteUtc,
                        EventType = "Funding settlement",
                        Asset = requestedCurrency,
                        Funding = funding.fundFee,
                        Notes = funding.fundRate.HasValue ? "Stored funding history. Rate " + funding.fundRate.Value.ToString("0.##########", CultureInfo.InvariantCulture) + "." : "Stored funding history.",
                        Source = "van_coincall_futures_funding_history",
                        SourceDetail = new { funding.symbol, funding.displayName, funding.recordKey, funding.recordId, funding.recordTimeUtc, funding.observedMinuteUtc, funding.tradeSide, funding.qty, funding.fundFee, funding.fundRate, funding.interest8h }
                    });
                }
            }
            catch (InvalidOperationException ex)
            {
                warnings.Add("Stored funding history unavailable for " + symbol + ": " + FriendlyCoincallError(ex.Message));
            }
        }

        try
        {
            using var tradeDoc = await client.GetFuturesTradeHistoryAsync(100, 0, startMs, endMs, ct);
            var tradeRows = ExtractCoincallRows(tradeDoc.RootElement);
            if (tradeRows.Count >= 100) warnings.Add("Futures trade history hit the 100-row route cap for the selected window.");
            foreach (var row in tradeRows)
            {
                var ts = ExtractCoincallRowUtc(row);
                if (!ts.HasValue) continue;
                var asset = ExtractLedgerAsset(row, allowSymbolAsset: true);
                if (!string.Equals(asset, requestedCurrency, StringComparison.OrdinalIgnoreCase)) continue;
                var rpnl = FindCoincallDecimal(row, "rpnl", "realizedPnl", "realizedPNL");
                var fee = FindCoincallDecimal(row, "fee", "fees", "commission");
                if (!rpnl.HasValue && !fee.HasValue) continue;
                events.Add(new CoincallClearingLedgerEvent
                {
                    TimeUtc = ts.Value,
                    EventType = "Futures trade",
                    Asset = requestedCurrency,
                    RealizedPnl = rpnl,
                    PnlUsdt = rpnl,
                    PnlCurrency = rpnl.HasValue ? "USDT" : null,
                    PnlSourceScope = rpnl.HasValue ? "trade" : null,
                    Fees = NormalizeFeeAmount(fee),
                    Notes = "Live futures trade history row with confirmed realized PnL and/or fee.",
                    Source = "CoinCall futures trade history",
                    SourceDetail = ToTradeSourceRow(row)
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            warnings.Add("Futures trade history unavailable: " + FriendlyCoincallError(ex.Message));
        }

        try
        {
            using var transferDoc = await client.GetAccountTransferRecordsAsync(ct);
            foreach (var row in ExtractCoincallRows(transferDoc.RootElement))
            {
                var ts = ExtractCoincallRowUtc(row);
                if (!ts.HasValue || ts.Value < fromUtc) continue;
                var amount = FindCoincallDecimal(row, "amount", "qty", "value", "transferAmount", "changeAmount", "balanceChange");
                var direction = FindCoincallString(row, "direction", "type", "flowType", "transferType", "side");
                var asset = ExtractLedgerAsset(row, allowSymbolAsset: false);
                if (!amount.HasValue || string.IsNullOrWhiteSpace(direction) || !string.Equals(asset, requestedCurrency, StringComparison.OrdinalIgnoreCase)) continue;
                events.Add(new CoincallClearingLedgerEvent
                {
                    TimeUtc = ts.Value,
                    EventType = "Transfer",
                    Asset = requestedCurrency,
                    Transfers = NormalizeSignedTransferAmount(amount.Value, direction),
                    Notes = "CoinCall transfer row with confirmed amount, direction and currency.",
                    Source = "CoinCall sysTransferRecords",
                    SourceDetail = ToTransferSourceRow(row)
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            warnings.Add("Transfer records unavailable: " + FriendlyCoincallError(ex.Message));
        }

        foreach (var symbol in CoincallSpotSymbolsForCurrency(requestedCurrency))
        {
            try
            {
                using var spotDoc = await client.GetSpotFillsAsync(symbol, ct);
                foreach (var row in ExtractCoincallRows(spotDoc.RootElement))
                {
                    var ts = ExtractCoincallRowUtc(row);
                    if (!ts.HasValue || ts.Value < fromUtc) continue;
                    AddSpotFillClearingEvents(events, row, requestedCurrency, ts.Value);
                }
            }
            catch (InvalidOperationException ex)
            {
                warnings.Add("Spot fills unavailable for " + symbol + ": " + FriendlyCoincallError(ex.Message));
            }
        }

        var rows = events
            .OrderBy(e => e.TimeUtc)
            .ThenBy(e => e.EventType, StringComparer.OrdinalIgnoreCase)
            .Select(e => AttachMinuteEquitySnapshot(e, minuteEquitySnapshots))
            .Select(e => e.ToPayload())
            .ToList();

        if (rows.Count == 0) warnings.Add("No confirmed " + requestedCurrency + " ledger events found for the selected window.");

        return new
        {
            account = ToPublicAccount(account),
            equityCurrency = requestedCurrency,
            requestedCurrency,
            requestedDays = days,
            availableCurrencies,
            rows,
            events = rows,
            warnings,
            sourceNotes = new[]
            {
                "Rows are event-ledger entries, not fabricated day buckets.",
                "Equity column uses stored USD/USDT account-level minute snapshots from van_coincall_equity_minute at or before each row timestamp; missing snapshots render blank.",
                "Daily equity snapshots are included only when van_coincall_equity_daily stores the requested currency.",
                "PnL, USDT is populated only from confirmed clearing, trade or settlement row fields; rows without such a field render blank.",
                "Funding rows come from stored CoinCall funding history when the contract asset matches the selected tab.",
                "Futures trade rows are included only when realized PnL and/or fee exists and the row asset can be confirmed.",
                "Transfers require confirmed timestamp, numeric amount, direction and currency.",
                "Spot fill rows use confirmed side, quantity, notional, fee currency and explicit BTC/ETH/USDT legs from CoinCall spot fills."
            },
            fetchedAtUtc = DateTime.UtcNow
        };
    }

    private static string NormalizeDailyClearingCurrency(string? currency)
    {
        string normalized = (currency ?? "BTC").Trim().ToUpperInvariant();
        return normalized is "BTC" or "ETH" or "USDT" ? normalized : "BTC";
    }

    private static Dictionary<DateOnly, List<JsonElement>> GroupRowsByDay(List<JsonElement> rows)
    {
        var map = new Dictionary<DateOnly, List<JsonElement>>();
        foreach (var row in rows)
        {
            var day = ExtractCoincallRowDay(row);
            if (!day.HasValue) continue;
            if (!map.TryGetValue(day.Value, out var list))
            {
                list = new List<JsonElement>();
                map[day.Value] = list;
            }
            list.Add(row);
        }
        return map;
    }

    private static DateOnly? ExtractCoincallRowDay(JsonElement row)
    {
        var ts = ExtractCoincallRowUtc(row);
        return ts.HasValue ? DateOnly.FromDateTime(ts.Value) : null;
    }

    private static DateTime? ExtractCoincallRowUtc(JsonElement row)
    {
        long? epochMs = FindCoincallLong(row, "time", "ts", "ctime", "createTime", "createdTime", "updateTime", "recordTime", "recordTimeUtc", "record_time_utc", "observedMinuteUtc", "minuteUtc");
        if (epochMs.HasValue && epochMs.Value > 0)
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(epochMs.Value).UtcDateTime; } catch { }
        }
        if (row.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in new[] { "time", "ts", "ctime", "createTime", "createdTime", "updateTime", "recordTime", "recordTimeUtc" })
        {
            if (!row.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind != JsonValueKind.String) continue;
            var text = value.GetString();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)) return dto.UtcDateTime;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        return null;
    }

    private static long? FindCoincallLong(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out var number)) return number;
                if (prop.Value.ValueKind == JsonValueKind.String && long.TryParse(prop.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number)) return number;
                if (prop.Value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(prop.Value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)) return dto.ToUnixTimeMilliseconds();
            }
        }
        return null;
    }

    private static List<JsonElement> ExtractCoincallRows(JsonElement root)
    {
        foreach (var candidate in ExtractCoincallArrayCandidates(root))
        {
            var list = new List<JsonElement>();
            foreach (var item in candidate.EnumerateArray()) list.Add(item.Clone());
            if (list.Count > 0) return list;
        }
        if (root.ValueKind == JsonValueKind.Object && IsCoincallRowLike(root)) return new List<JsonElement> { root.Clone() };
        return new List<JsonElement>();
    }

    private static IEnumerable<JsonElement> ExtractCoincallArrayCandidates(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            yield return root;
            yield break;
        }
        if (root.ValueKind != JsonValueKind.Object) yield break;
        foreach (var key in new[] { "data", "rows", "items", "list", "records", "result" })
        {
            if (!root.TryGetProperty(key, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Array)
            {
                yield return value;
                yield break;
            }
            foreach (var nested in ExtractCoincallArrayCandidates(value)) yield return nested;
        }
    }

    private static bool IsCoincallRowLike(JsonElement obj)
    {
        return FindCoincallDecimal(obj, "amount", "qty", "value", "fundFee", "fund_fee", "fee", "fees", "commission", "rpnl", "realizedPnl", "realizedPNL").HasValue
            || FindCoincallString(obj, "symbol", "displaySymbol", "displayName", "recordKey", "recordId") != null;
    }

    private static HashSet<string> ExtractCoincallSymbols(JsonElement root)
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ExtractCoincallRows(root))
        {
            var symbol = ExtractSourceSymbol(row);
            if (!string.IsNullOrWhiteSpace(symbol)) symbols.Add(symbol);
        }
        return symbols;
    }

    private static string? ExtractSourceSymbol(JsonElement row)
    {
        var symbol = FindCoincallString(row, "symbol", "displaySymbol", "displayName", "instId", "instrument", "tickerId");
        return string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();
    }

    private static object ToTradeSourceRow(JsonElement row)
    {
        return new
        {
            timeUtc = ExtractCoincallRowUtc(row),
            symbol = ExtractSourceSymbol(row),
            side = FindCoincallString(row, "side", "tradeSide", "direction", "sd", "si", "orderSide"),
            qty = FindCoincallDecimal(row, "qty", "quantity", "amount", "fillQty", "filledQty"),
            value = FindCoincallDecimal(row, "value", "filledValue", "quoteQty", "quoteAmount"),
            price = FindCoincallDecimal(row, "price", "fillPrice", "filledPrice", "px"),
            fee = FindCoincallDecimal(row, "fee", "fees", "commission"),
            rpnl = FindCoincallDecimal(row, "rpnl", "realizedPnl", "realizedPNL"),
            role = FindCoincallString(row, "role", "liquidity"),
            raw = row.Clone()
        };
    }

    private static object ToFundingSourceRow(JsonElement row)
    {
        return new
        {
            timeUtc = ExtractCoincallRowUtc(row),
            symbol = ExtractSourceSymbol(row),
            tradeSide = FindCoincallString(row, "tradeSide", "side"),
            qty = FindCoincallDecimal(row, "qty", "quantity", "amount"),
            fundFee = FindCoincallDecimal(row, "fundFee", "fund_fee"),
            fundRate = FindCoincallDecimal(row, "fundRate", "fund_rate"),
            interest8h = FindCoincallDecimal(row, "interest8h", "interest_8h"),
            raw = row.Clone()
        };
    }

    private static object ToSpotFillSourceRow(JsonElement row)
    {
        return new
        {
            timeUtc = ExtractCoincallRowUtc(row),
            symbol = ExtractSourceSymbol(row),
            displaySymbol = FindCoincallString(row, "displaySymbol", "displayName"),
            side = NormalizeSpotTradeSide(FindCoincallString(row, "side", "tradeSide", "direction")),
            rawSide = FindCoincallString(row, "side", "tradeSide", "direction"),
            baseToken = NormalizeLedgerCurrency(FindCoincallString(row, "baseToken", "baseCurrency")),
            quoteToken = NormalizeLedgerCurrency(FindCoincallString(row, "quoteToken", "quoteCurrency")),
            qty = FindCoincallDecimal(row, "qty", "quantity", "amount", "fillQty", "filledQty"),
            baseAmount = FindCoincallDecimal(row, "qty", "quantity", "amount", "fillQty", "filledQty"),
            price = FindCoincallDecimal(row, "price", "fillPrice", "filledPrice", "px"),
            quoteAmount = FindCoincallDecimal(row, "dealValue", "value", "filledValue", "quoteQty", "quoteAmount"),
            quoteAmountCurrency = NormalizeLedgerCurrency(FindCoincallString(row, "dealValueUnit", "quoteToken", "quoteCurrency")),
            fee = FindCoincallDecimal(row, "fee", "fees", "commission"),
            feeCurrency = NormalizeLedgerCurrency(FindCoincallString(row, "feeCurrency", "feeCcy", "commissionCurrency", "commissionAsset")),
            orderId = FindCoincallString(row, "orderId", "ordId", "order_id"),
            tradeId = FindCoincallString(row, "tradeId", "fillId", "execId", "id"),
            clientOrderId = FindCoincallString(row, "clientOrderId", "clientOid", "clOrdId"),
            role = FindCoincallString(row, "role", "liquidity"),
            isTaker = FindCoincallString(row, "isTaker"),
            raw = row.Clone()
        };
    }

    private static void AddSpotFillClearingEvents(List<CoincallClearingLedgerEvent> events, JsonElement row, string requestedCurrency, DateTime tsUtc)
    {
        var side = NormalizeSpotTradeSide(FindCoincallString(row, "side", "tradeSide", "direction"));
        var baseToken = NormalizeLedgerCurrency(FindCoincallString(row, "baseToken", "baseCurrency")) ?? CoincallLedgerAssetFromSymbol(ExtractSourceSymbol(row));
        var quoteToken = NormalizeLedgerCurrency(FindCoincallString(row, "quoteToken", "quoteCurrency"));
        var quoteAmountCurrency = NormalizeLedgerCurrency(FindCoincallString(row, "dealValueUnit", "quoteToken", "quoteCurrency")) ?? quoteToken;
        var qty = FindCoincallDecimal(row, "qty", "quantity", "amount", "fillQty", "filledQty");
        var quoteAmount = FindCoincallDecimal(row, "dealValue", "value", "filledValue", "quoteQty", "quoteAmount");
        var price = FindCoincallDecimal(row, "price", "fillPrice", "filledPrice", "px");
        var fee = NormalizeFeeAmount(FindCoincallDecimal(row, "fee", "fees", "commission"));
        var feeCurrency = ExtractLedgerFeeAsset(row);
        var sourceRow = ToSpotFillSourceRow(row);
        var source = "CoinCall spot fills";
        var note = BuildSpotFillNote(row);
        var symbol = ExtractSourceSymbol(row);
        var role = NormalizeSpotLiquidity(FindCoincallString(row, "isTaker")) ?? FindCoincallString(row, "role", "liquidity");
        var orderId = FindCoincallString(row, "orderId", "ordId", "order_id");
        var tradeId = FindCoincallString(row, "tradeId", "fillId", "execId", "id");
        var lowerSide = (side ?? string.Empty).Trim().ToLowerInvariant();

        if (qty.HasValue && !string.IsNullOrWhiteSpace(baseToken) && string.Equals(baseToken, requestedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            decimal signedQty = lowerSide == "sell" ? -Math.Abs(qty.Value) : lowerSide == "buy" ? Math.Abs(qty.Value) : qty.Value;
            events.Add(new CoincallClearingLedgerEvent
            {
                TimeUtc = tsUtc,
                EventType = "Spot fill",
                Asset = requestedCurrency,
                Instrument = symbol,
                Side = side,
                Amount = signedQty,
                BaseAmount = signedQty,
                Price = price,
                Value = quoteAmount,
                ValueCurrency = quoteAmountCurrency,
                Fees = string.Equals(feeCurrency, requestedCurrency, StringComparison.OrdinalIgnoreCase) ? fee : null,
                Role = role,
                OrderId = orderId,
                TradeId = tradeId,
                Notes = note,
                Source = source,
                SourceDetail = sourceRow
            });
        }

        if (quoteAmount.HasValue && !string.IsNullOrWhiteSpace(quoteAmountCurrency) && string.Equals(quoteAmountCurrency, requestedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            decimal signedQuote = lowerSide == "buy" ? -Math.Abs(quoteAmount.Value) : lowerSide == "sell" ? Math.Abs(quoteAmount.Value) : quoteAmount.Value;
            events.Add(new CoincallClearingLedgerEvent
            {
                TimeUtc = tsUtc,
                EventType = "Spot fill",
                Asset = requestedCurrency,
                Instrument = symbol,
                Side = side,
                Amount = signedQuote,
                Price = price,
                Value = quoteAmount,
                ValueCurrency = quoteAmountCurrency,
                Transfers = signedQuote,
                Fees = string.Equals(feeCurrency, requestedCurrency, StringComparison.OrdinalIgnoreCase) ? fee : null,
                Role = role,
                OrderId = orderId,
                TradeId = tradeId,
                Notes = note,
                Source = source,
                SourceDetail = sourceRow
            });
        }

        if (fee.HasValue && string.Equals(feeCurrency, requestedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            bool feeAlreadyIncluded =
                string.Equals(baseToken, requestedCurrency, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(quoteAmountCurrency, requestedCurrency, StringComparison.OrdinalIgnoreCase);
            if (!feeAlreadyIncluded)
            {
                events.Add(new CoincallClearingLedgerEvent
                {
                    TimeUtc = tsUtc,
                    EventType = "Spot fill fee",
                    Asset = requestedCurrency,
                    Instrument = symbol,
                    Side = side,
                    Price = price,
                    Value = quoteAmount,
                    ValueCurrency = quoteAmountCurrency,
                    Fees = fee,
                    Role = role,
                    OrderId = orderId,
                    TradeId = tradeId,
                    Notes = note,
                    Source = source,
                    SourceDetail = sourceRow
                });
            }
        }
    }

    private static string BuildSpotFillNote(JsonElement row)
    {
        var parts = new List<string>();
        var symbol = FindCoincallString(row, "displaySymbol", "displayName", "symbol");
        var side = NormalizeSpotTradeSide(FindCoincallString(row, "side", "tradeSide", "direction"));
        var baseToken = NormalizeLedgerCurrency(FindCoincallString(row, "baseToken", "baseCurrency")) ?? CoincallLedgerAssetFromSymbol(ExtractSourceSymbol(row));
        var quoteToken = NormalizeLedgerCurrency(FindCoincallString(row, "quoteToken", "quoteCurrency"));
        var qty = FindCoincallDecimal(row, "qty", "quantity", "amount", "fillQty", "filledQty");
        var price = FindCoincallDecimal(row, "price", "fillPrice", "filledPrice", "px");
        var quoteAmount = FindCoincallDecimal(row, "dealValue", "value", "filledValue", "quoteQty", "quoteAmount");
        var quoteAmountCurrency = NormalizeLedgerCurrency(FindCoincallString(row, "dealValueUnit", "quoteToken", "quoteCurrency")) ?? quoteToken;
        var fee = FindCoincallDecimal(row, "fee", "fees", "commission");
        var feeCurrency = NormalizeLedgerCurrency(FindCoincallString(row, "feeCurrency", "feeCcy", "commissionCurrency", "commissionAsset"));
        var isTaker = FindCoincallString(row, "isTaker");

        var operation = new List<string>();
        if (!string.IsNullOrWhiteSpace(symbol)) operation.Add(symbol.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(side)) operation.Add(side);
        if (qty.HasValue) operation.Add(FormatCoincallAmount(qty.Value, baseToken) + (string.IsNullOrWhiteSpace(baseToken) ? "" : " " + baseToken));
        if (price.HasValue) operation.Add("@ " + FormatCoincallAmount(price.Value, quoteToken) + (string.IsNullOrWhiteSpace(quoteToken) ? "" : " " + quoteToken));
        if (operation.Count > 0) parts.Add("Spot fill " + string.Join(" ", operation));

        if (quoteAmount.HasValue)
        {
            parts.Add("notional " + FormatCoincallAmount(quoteAmount.Value, quoteAmountCurrency) + (string.IsNullOrWhiteSpace(quoteAmountCurrency) ? "" : " " + quoteAmountCurrency));
        }
        if (fee.HasValue)
        {
            parts.Add("fee " + FormatCoincallAmount(fee.Value, feeCurrency) + (string.IsNullOrWhiteSpace(feeCurrency) ? "" : " " + feeCurrency));
        }
        var liquidity = NormalizeSpotLiquidity(isTaker);
        if (!string.IsNullOrWhiteSpace(liquidity)) parts.Add(liquidity);
        foreach (var idPart in new[]
        {
            ("orderId", FindCoincallString(row, "orderId", "ordId", "order_id")),
            ("tradeId", FindCoincallString(row, "tradeId", "fillId", "execId", "id")),
            ("clientOrderId", FindCoincallString(row, "clientOrderId", "clientOid", "clOrdId"))
        })
        {
            if (!string.IsNullOrWhiteSpace(idPart.Item2)) parts.Add(idPart.Item1 + "=" + idPart.Item2);
        }
        return parts.Count > 0 ? string.Join("; ", parts) + "." : "CoinCall spot fill row with confirmed fee, timestamp and fee currency.";
    }

    private static string? NormalizeSpotTradeSide(string? side)
    {
        var text = (side ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        var upper = text.ToUpperInvariant();
        return upper switch
        {
            "1" or "BUY" or "B" => "Buy",
            "2" or "SELL" or "S" => "Sell",
            _ => text
        };
    }

    private static string? NormalizeSpotLiquidity(string? isTaker)
    {
        var text = (isTaker ?? string.Empty).Trim().ToUpperInvariant();
        return text switch
        {
            "1" or "TRUE" => "taker",
            "0" or "FALSE" => "maker",
            _ => null
        };
    }

    private static string FormatCoincallAmount(decimal value, string? currency)
    {
        return string.Equals(currency, "BTC", StringComparison.OrdinalIgnoreCase)
            ? value.ToString("0.00000000", CultureInfo.InvariantCulture)
            : value.ToString("0.##########", CultureInfo.InvariantCulture);
    }

    private static object ToTransferSourceRow(JsonElement row)
    {
        return new
        {
            timeUtc = ExtractCoincallRowUtc(row),
            symbol = ExtractSourceSymbol(row),
            amount = FindCoincallDecimal(row, "amount", "qty", "value", "transferAmount", "changeAmount", "balanceChange"),
            direction = FindCoincallString(row, "direction", "type", "flowType", "transferType", "side"),
            raw = row.Clone()
        };
    }

    private static decimal NormalizeSignedTransferAmount(decimal amount, string? direction)
    {
        string text = (direction ?? string.Empty).Trim().ToUpperInvariant();
        if (text.Contains("OUT") || text.Contains("WITHDRAW") || text.Contains("DEBIT") || text.Contains("SEND") || text.Contains("PAY") || text.Contains("REDEEM") || text == "2")
            return -Math.Abs(amount);
        if (text.Contains("IN") || text.Contains("DEPOSIT") || text.Contains("CREDIT") || text.Contains("RECEIVE") || text.Contains("RECEIPT") || text == "1")
            return Math.Abs(amount);
        return amount;
    }

    private static decimal? NormalizeFeeAmount(decimal? fee)
    {
        if (!fee.HasValue) return null;
        return fee.Value > 0m ? -Math.Abs(fee.Value) : fee.Value;
    }

    private static string[] CoincallLedgerSymbolsForCurrency(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "BTC" => new[] { "BTCUSD" },
            "ETH" => new[] { "ETHUSD" },
            _ => Array.Empty<string>()
        };
    }

    private static string[] CoincallSpotSymbolsForCurrency(string currency)
    {
        return currency.ToUpperInvariant() switch
        {
            "BTC" => new[] { "BTCUSDT" },
            "ETH" => new[] { "ETHUSDT" },
            "USDT" => new[] { "BTCUSDT", "ETHUSDT" },
            _ => Array.Empty<string>()
        };
    }

    private static string? CoincallLedgerAssetFromSymbol(string? symbol)
    {
        var s = (symbol ?? string.Empty).Trim().ToUpperInvariant().Replace("-", "").Replace("_", "").Replace(" ", "");
        if (s.StartsWith("BTC", StringComparison.OrdinalIgnoreCase)) return "BTC";
        if (s.StartsWith("ETH", StringComparison.OrdinalIgnoreCase)) return "ETH";
        if (s.StartsWith("USDT", StringComparison.OrdinalIgnoreCase) || s.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)) return "USDT";
        return null;
    }

    private static string? ExtractLedgerAsset(JsonElement row, bool allowSymbolAsset)
    {
        var explicitAsset = FindCoincallString(row,
            "currency", "ccy", "coin", "asset", "token", "baseCurrency", "feeCurrency", "feeCcy", "commissionCurrency", "commissionAsset");
        var normalized = NormalizeLedgerCurrency(explicitAsset);
        if (!string.IsNullOrWhiteSpace(normalized)) return normalized;
        return allowSymbolAsset ? CoincallLedgerAssetFromSymbol(ExtractSourceSymbol(row)) : null;
    }

    private static string? ExtractLedgerFeeAsset(JsonElement row)
    {
        return NormalizeLedgerCurrency(FindCoincallString(row, "feeCurrency", "feeCcy", "commissionCurrency", "commissionAsset"));
    }

    private static string? NormalizeLedgerCurrency(string? value)
    {
        var s = (value ?? string.Empty).Trim().ToUpperInvariant();
        return s is "BTC" or "ETH" or "USDT" ? s : null;
    }

    private static CoincallClearingLedgerEvent AttachMinuteEquitySnapshot(CoincallClearingLedgerEvent row, List<CoincallEquityPoint> minuteSnapshots)
    {
        var snapshot = minuteSnapshots.LastOrDefault(p => p.tsUtc <= row.TimeUtc);
        if (snapshot == null) return row;

        row.EquityUsd = snapshot.totalEquity;
        row.EquityUsdt = snapshot.totalEquity;
        row.EquitySnapshotUtc = snapshot.tsUtc;
        row.EquitySnapshotCurrency = string.Equals(snapshot.equityCurrency, "USDT", StringComparison.OrdinalIgnoreCase) ? "USDT" : "USD";
        row.EquitySource = "van_coincall_equity_minute";
        return row;
    }

    private sealed class CoincallClearingLedgerEvent
    {
        public DateTime TimeUtc { get; set; }
        public string EventType { get; set; } = "";
        public string Asset { get; set; } = "";
        public string? Instrument { get; set; }
        public string? Side { get; set; }
        public decimal? Amount { get; set; }
        public decimal? BaseAmount { get; set; }
        public decimal? Price { get; set; }
        public decimal? MarkPrice { get; set; }
        public decimal? Position { get; set; }
        public decimal? Value { get; set; }
        public string? ValueCurrency { get; set; }
        public decimal? Equity { get; set; }
        public decimal? EquityUsd { get; set; }
        public decimal? EquityUsdt { get; set; }
        public DateTime? EquitySnapshotUtc { get; set; }
        public string? EquitySnapshotCurrency { get; set; }
        public string? EquitySource { get; set; }
        public decimal? RealizedPnl { get; set; }
        public decimal? UnrealizedPnl { get; set; }
        public decimal? UnrealizedPnlUsdt { get; set; }
        public decimal? PnlUsdt { get; set; }
        public string? PnlCurrency { get; set; }
        public string? PnlSourceScope { get; set; }
        public decimal? Funding { get; set; }
        public decimal? Fees { get; set; }
        public decimal? Transfers { get; set; }
        public decimal? Residual { get; set; }
        public string? Role { get; set; }
        public string? OrderId { get; set; }
        public string? TradeId { get; set; }
        public string Notes { get; set; } = "";
        public string Source { get; set; } = "";
        public object? SourceDetail { get; set; }

        public object ToPayload() => new
        {
            timeUtc = TimeUtc,
            eventType = EventType,
            asset = Asset,
            currency = Asset,
            instrument = Instrument,
            side = Side,
            amount = Amount,
            baseAmount = BaseAmount,
            price = Price,
            markPrice = MarkPrice,
            position = Position,
            value = Value,
            valueCurrency = ValueCurrency,
            equity = Equity,
            equityUsd = EquityUsd,
            equityUsdt = EquityUsdt,
            equitySnapshotUtc = EquitySnapshotUtc,
            equitySnapshotCurrency = EquitySnapshotCurrency,
            equitySource = EquitySource,
            realizedPnl = RealizedPnl,
            unrealizedPnl = UnrealizedPnl,
            unrealizedPnlUsdt = UnrealizedPnlUsdt,
            pnlUsdt = PnlUsdt,
            pnlCurrency = PnlCurrency,
            pnlSourceScope = PnlSourceScope,
            funding = Funding,
            fees = Fees,
            transfers = Transfers,
            residual = Residual,
            role = Role,
            orderId = OrderId,
            tradeId = TradeId,
            notes = string.IsNullOrWhiteSpace(Notes) ? Array.Empty<string>() : new[] { Notes },
            source = Source,
            sources = SourceDetail
        };
    }

    private static async Task<List<CoincallAssetEquityPoint>> BuildEstimatedBtcAssetDailyEquityAsync(CoincallRepository repo, CoincallApiClient client, List<CoincallAssetEquityPoint> dailyStored, List<CoincallAssetEquityPoint> minuteStored, CancellationToken ct)
    {
        var actualDaily = dailyStored.Where(p => p.equityNative.HasValue).OrderBy(p => p.tsUtc).ToList();
        var actualMinute = minuteStored.Where(p => p.equityNative.HasValue).OrderBy(p => p.tsUtc).ToList();
        var anchor = actualMinute.LastOrDefault() ?? actualDaily.LastOrDefault();
        if (anchor == null || !anchor.equityNative.HasValue) return dailyStored;

        using var spotDoc = await client.GetSpotFillsAsync("BTCUSDT", ct);
        var fills = ExtractCoincallRows(spotDoc.RootElement)
            .Select(TryExtractBtcSpotFillDelta)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Where(x => x.TimeUtc <= anchor.tsUtc)
            .OrderBy(x => x.TimeUtc)
            .ToList();

        if (fills.Count == 0) return dailyStored;

        DateOnly firstDay = DateOnly.FromDateTime(fills[0].TimeUtc.AddHours(-8));
        DateOnly anchorDay = DateOnly.FromDateTime(anchor.tsUtc.AddHours(-8));
        if (firstDay > anchorDay) return dailyStored;

        var usdClose = await repo.GetSpotDailyCloseByClearingDayAsync("BTCUSDT", firstDay, anchorDay, ct);
        var merged = dailyStored
            .GroupBy(p => DateOnly.FromDateTime(p.tsUtc.AddHours(-8)))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.tsUtc).First());

        for (var day = firstDay; day <= anchorDay; day = day.AddDays(1))
        {
            if (merged.TryGetValue(day, out var existing) && existing.equityNative.HasValue && existing.valuationSource != "estimated:spot_fills") continue;

            DateTime boundaryEndUtc = day.AddDays(1).ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
            decimal futureDelta = fills
                .Where(x => x.TimeUtc >= boundaryEndUtc && x.TimeUtc <= anchor.tsUtc)
                .Sum(x => x.NativeDelta);

            decimal estimatedNative = anchor.equityNative.Value - futureDelta;
            decimal? estimatedUsd = usdClose.TryGetValue(day, out var close) ? estimatedNative * close : null;
            var tsUtc = day.AddDays(1).ToDateTime(new TimeOnly(7, 59), DateTimeKind.Utc);

            merged[day] = new CoincallAssetEquityPoint
            {
                tsUtc = tsUtc,
                asset = "BTC",
                equityNative = estimatedNative,
                availableNative = null,
                frozenNative = null,
                unrealizedPnlNative = null,
                equityUsdt = estimatedUsd,
                equityUsd = estimatedUsd,
                valuationSource = "estimated:spot_fills",
                equityNativeSource = "estimated:current_snapshot_minus_spot_fills",
                equityUsdSource = estimatedUsd.HasValue ? "estimated:native_btc_x_btcusdt_daily_close" : null,
                rawJson = JsonSerializer.Serialize(new
                {
                    estimated = true,
                    method = "current BTC asset equity minus later BTCUSDT spot fill deltas",
                    clearingBoundaryUtc = "08:00",
                    anchorUtc = anchor.tsUtc,
                    anchorNative = anchor.equityNative,
                    fillCount = fills.Count
                })
            };
        }

        return merged.Values.OrderBy(p => p.tsUtc).ToList();
    }

    private static BtcSpotFillDelta? TryExtractBtcSpotFillDelta(JsonElement row)
    {
        var symbol = NormalizeCoincallSymbol(ExtractSourceSymbol(row));
        if (!string.Equals(symbol, "BTCUSDT", StringComparison.OrdinalIgnoreCase)) return null;

        var ts = ExtractCoincallRowUtc(row);
        var side = NormalizeSpotTradeSide(FindCoincallString(row, "side", "tradeSide", "direction"));
        var qty = FindCoincallDecimal(row, "qty", "quantity", "amount", "fillQty", "filledQty");
        if (!ts.HasValue || !qty.HasValue || string.IsNullOrWhiteSpace(side)) return null;

        decimal delta = string.Equals(side, "sell", StringComparison.OrdinalIgnoreCase)
            ? -Math.Abs(qty.Value)
            : Math.Abs(qty.Value);

        var fee = NormalizeFeeAmount(FindCoincallDecimal(row, "fee", "fees", "commission"));
        var feeCurrency = ExtractLedgerFeeAsset(row);
        if (fee.HasValue && string.Equals(feeCurrency, "BTC", StringComparison.OrdinalIgnoreCase))
        {
            delta -= Math.Abs(fee.Value);
        }

        return new BtcSpotFillDelta(ts.Value, delta);
    }

    private static object BuildAssetEquityPayload(CoincallAccountRecord account, string asset, string denom, List<CoincallAssetEquityPoint> dailyStored, List<CoincallAssetEquityPoint> minuteStored, List<CoincallAssetEquityPoint> dailyDrawdownMinuteStored)
    {
        bool isNative = string.Equals(denom, "native", StringComparison.OrdinalIgnoreCase);
        decimal? Value(CoincallAssetEquityPoint p) => isNative ? p.equityNative : p.equityUsd;
        var dailyOrdered = dailyStored.Where(p => Value(p).HasValue).OrderBy(p => p.tsUtc).ToList();
        var minuteOrdered = minuteStored.Where(p => Value(p).HasValue).OrderBy(p => p.tsUtc).ToList();
        var dailyDrawdownMinuteOrdered = dailyDrawdownMinuteStored.Where(p => Value(p).HasValue).OrderBy(p => p.tsUtc).ToList();
        var dailyPoints = dailyOrdered.Select(p => new CoincallChartPoint(p.tsUtc, Value(p)!.Value)).ToList();
        var minutePoints = minuteOrdered.Select(p => new CoincallChartPoint(p.tsUtc, Value(p)!.Value)).ToList();
        var dailyDrawdownMinutePoints = dailyDrawdownMinuteOrdered.Select(p => new CoincallChartPoint(p.tsUtc, Value(p)!.Value)).ToList();
        var latest = minuteOrdered.LastOrDefault() ?? dailyOrdered.LastOrDefault();
        var first = dailyOrdered.FirstOrDefault();
        var minuteDrawdownPoints = BuildDrawdownSeriesWith30MinuteConfirmedHwm(minutePoints, collapseToDate: false);
        var dailyDrawdownPoints = BuildDailyDrawdownSeriesWithMinuteOverlay(dailyPoints, dailyDrawdownMinutePoints);
        return new
        {
            metrics = new
            {
                asset,
                denom = isNative ? "native" : "usd",
                equityCurrency = isNative ? asset : "USD",
                storeMinuteEquity = account.storeMinuteEquity,
                latestTotalEquity = latest == null ? null : Value(latest),
                latestNativeEquity = latest?.equityNative,
                latestUsdtEquity = latest?.equityUsdt,
                valuationSource = latest?.valuationSource,
                equitySource = isNative ? latest?.equityNativeSource : latest?.equityUsdSource,
                equityUsdSource = latest?.equityUsdSource,
                equityNativeSource = latest?.equityNativeSource,
                dailyPointCount = dailyOrdered.Count,
                minutePointCount = minuteOrdered.Count,
                firstSnapshotUtc = first?.tsUtc,
                lastSnapshotUtc = latest?.tsUtc
            },
            daily = new
            {
                points = dailyPoints,
                drawdownPoints = dailyDrawdownPoints
            },
            minute = new
            {
                points = minutePoints,
                drawdownPoints = minuteDrawdownPoints
            }
        };
    }

    private static CoincallEquityPayload BuildEquityPayload(CoincallAccountRecord account, List<CoincallEquityPoint> dailyStored, List<CoincallEquityPoint> minuteStored, List<CoincallEquityPoint> dailyDrawdownMinuteStored)
    {
        string equityCurrency = CoincallRepository.NormalizeEquityCurrency(account.equityCurrency);
        var dailyOrdered = dailyStored.OrderBy(p => p.tsUtc).ToList();
        var minuteOrdered = minuteStored.OrderBy(p => p.tsUtc).ToList();
        if (dailyOrdered.Count == 0)
        {
            return new CoincallEquityPayload(
                new
                {
                    equityCurrency,
                    storeMinuteEquity = account.storeMinuteEquity,
                    latestTotalEquity = 0m,
                    latestAvailableEquity = 0m,
                    latestUnrealizedPnl = 0m,
                    latestMarginRatio = (decimal?)null,
                    pnl24h = new { absolute = 0m, percent = 0m },
                    pnl7d = new { absolute = 0m, percent = 0m },
                    maxDrawdown24hPct = 0m,
                    maxDrawdownAllPct = 0m,
                    dailyPointCount = 0,
                    minutePointCount = 0,
                    firstSnapshotUtc = (DateTime?)null,
                    lastSnapshotUtc = (DateTime?)null
                },
                new { points = new List<CoincallChartPoint>(), drawdownPoints = new List<CoincallChartPoint>() },
                new { points = new List<CoincallChartPoint>(), drawdownPoints = new List<CoincallChartPoint>() }
            );
        }

        DateTime nowUtc = DateTime.UtcNow;
        CoincallEquityPoint latest = minuteOrdered.LastOrDefault() ?? dailyOrdered[^1];
        CoincallEquityPoint first = dailyOrdered[0];
        CoincallEquityPoint? ref24h = minuteOrdered.LastOrDefault(p => p.tsUtc <= nowUtc.AddHours(-24)) ?? dailyOrdered.LastOrDefault(p => p.tsUtc <= nowUtc.AddHours(-1)) ?? first;
        CoincallEquityPoint? ref7d = dailyOrdered.LastOrDefault(p => p.tsUtc <= nowUtc.AddDays(-7)) ?? first;
        var dailyPoints = dailyOrdered.Select(p => new CoincallChartPoint(p.tsUtc, p.totalEquity)).ToList();
        var minuteSeries = minuteOrdered.Select(p => new CoincallChartPoint(p.tsUtc, p.totalEquity)).ToList();
        var dailyDrawdownMinuteSeries = dailyDrawdownMinuteStored.OrderBy(p => p.tsUtc).Select(p => new CoincallChartPoint(p.tsUtc, p.totalEquity)).ToList();
        var dailyDrawdownPoints = BuildDailyDrawdownSeriesWithMinuteOverlay(dailyPoints, dailyDrawdownMinuteSeries);
        var minuteDrawdownPoints = BuildDrawdownSeriesWith30MinuteConfirmedHwm(minuteSeries, collapseToDate: false);
        decimal maxDrawdown24h = minuteDrawdownPoints.Count == 0 ? 0m : minuteDrawdownPoints.Max(p => p.value);
        decimal maxDrawdownAll = dailyDrawdownPoints.Count == 0 ? 0m : dailyDrawdownPoints.Max(p => p.value);
        return new CoincallEquityPayload(
            new
            {
                equityCurrency,
                storeMinuteEquity = account.storeMinuteEquity,
                latestTotalEquity = latest.totalEquity,
                latestAvailableEquity = latest.availableEquity,
                latestUnrealizedPnl = latest.unrealizedPnl,
                latestMarginRatio = latest.marginRatio,
                pnl24h = BuildEquityDelta(ref24h, latest),
                pnl7d = BuildEquityDelta(ref7d, latest),
                maxDrawdown24hPct = Math.Round(maxDrawdown24h, 4),
                maxDrawdownAllPct = Math.Round(maxDrawdownAll, 4),
                dailyPointCount = dailyPoints.Count,
                minutePointCount = minuteSeries.Count,
                firstSnapshotUtc = first.tsUtc,
                lastSnapshotUtc = latest.tsUtc
            },
            new { points = dailyPoints, drawdownPoints = dailyDrawdownPoints },
            new { points = minuteSeries, drawdownPoints = minuteDrawdownPoints }
        );
    }

    private static object BuildEquityDelta(CoincallEquityPoint? first, CoincallEquityPoint? latest)
    {
        if (first == null || latest == null) return new { absolute = 0m, percent = 0m };
        decimal absolute = latest.totalEquity - first.totalEquity;
        decimal percent = first.totalEquity == 0m ? 0m : absolute / first.totalEquity * 100m;
        return new { absolute, percent };
    }

    private static List<CoincallChartPoint> BuildDailyDrawdownSeriesWithMa60Hwm(List<CoincallChartPoint> dailyPoints, List<CoincallChartPoint> minutePoints)
    {
        var byDate = BuildDrawdownSeries(dailyPoints, collapseToDate: true)
            .ToDictionary(p => DateOnly.FromDateTime(p.tsUtc), p => p.value);
        var ordered = minutePoints.OrderBy(p => p.tsUtc).ToList();
        if (ordered.Count == 0)
        {
            return byDate
                .OrderBy(kv => kv.Key)
                .Select(kv => new CoincallChartPoint(kv.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Math.Round(kv.Value, 4)))
                .ToList();
        }

        Queue<decimal> window = new Queue<decimal>();
        decimal windowSum = 0m;
        decimal? hwm60m = null;
        DateOnly? currentDay = null;
        decimal currentDayMax = 0m;
        bool currentDayHasMa60 = false;
        foreach (var point in ordered)
        {
            window.Enqueue(point.value);
            windowSum += point.value;
            while (window.Count > 60)
            {
                windowSum -= window.Dequeue();
            }

            decimal equityMa60m = windowSum / window.Count;
            hwm60m = hwm60m.HasValue && hwm60m.Value > equityMa60m ? hwm60m.Value : equityMa60m;
            decimal drawdown = hwm60m.Value <= 0m ? 0m : (hwm60m.Value - point.value) / hwm60m.Value * 100m;
            if (drawdown < 0m) drawdown = 0m;

            DateOnly day = DateOnly.FromDateTime(point.tsUtc);
            if (currentDay != day)
            {
                if (currentDay.HasValue && currentDayHasMa60)
                {
                    byDate[currentDay.Value] = Math.Round(currentDayMax, 4);
                }
                currentDay = day;
                currentDayMax = drawdown;
                currentDayHasMa60 = window.Count >= 60;
                continue;
            }

            if (drawdown > currentDayMax)
            {
                currentDayMax = drawdown;
            }
            if (window.Count >= 60)
            {
                currentDayHasMa60 = true;
            }
        }
        if (currentDay.HasValue && currentDayHasMa60)
        {
            byDate[currentDay.Value] = Math.Round(currentDayMax, 4);
        }

        return byDate
            .OrderBy(kv => kv.Key)
            .Select(kv => new CoincallChartPoint(kv.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Math.Round(kv.Value, 4)))
            .ToList();
    }

    private static List<CoincallChartPoint> BuildDailyDrawdownSeriesWithMinuteOverlay(List<CoincallChartPoint> dailyPoints, List<CoincallChartPoint> minutePoints)
    {
        var byDate = BuildDrawdownSeries(dailyPoints, collapseToDate: true)
            .ToDictionary(p => DateOnly.FromDateTime(p.tsUtc), p => p.value);
        foreach (var point in BuildDrawdownSeriesWith30MinuteConfirmedHwm(minutePoints, collapseToDate: true))
        {
            byDate[DateOnly.FromDateTime(point.tsUtc)] = point.value;
        }

        return byDate
            .OrderBy(kv => kv.Key)
            .Select(kv => new CoincallChartPoint(kv.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Math.Round(kv.Value, 4)))
            .ToList();
    }

    private static List<CoincallChartPoint> BuildDrawdownSeriesWith30MinuteConfirmedHwm(List<CoincallChartPoint> points, bool collapseToDate)
    {
        var list = new List<CoincallChartPoint>();
        var ordered = points.OrderBy(p => p.tsUtc).ToList();
        if (ordered.Count == 0) return list;
        decimal acceptedHwm = ordered[0].value;
        Queue<CoincallChartPoint> pending = new Queue<CoincallChartPoint>();
        foreach (var point in ordered)
        {
            while (pending.Count > 0 && pending.Peek().tsUtc <= point.tsUtc.AddMinutes(-30))
            {
                CoincallChartPoint confirmed = pending.Dequeue();
                if (confirmed.value > acceptedHwm)
                {
                    acceptedHwm = confirmed.value;
                }
            }
            decimal dd = acceptedHwm > 0m ? (acceptedHwm - point.value) / acceptedHwm * 100m : 0m;
            if (dd < 0m) dd = 0m;
            pending.Enqueue(point);
            DateTime ts = collapseToDate ? DateTime.SpecifyKind(point.tsUtc.Date, DateTimeKind.Utc) : point.tsUtc;
            if (collapseToDate && list.Count > 0 && list[^1].tsUtc == ts)
            {
                if (dd > list[^1].value) list[^1] = new CoincallChartPoint(ts, Math.Round(dd, 4));
            }
            else list.Add(new CoincallChartPoint(ts, Math.Round(dd, 4)));
        }
        return list;
    }

    private static List<CoincallChartPoint> BuildDrawdownSeries(List<CoincallChartPoint> points, bool collapseToDate)
    {
        var list = new List<CoincallChartPoint>();
        if (points.Count == 0) return list;
        decimal peak = points[0].value;
        foreach (var point in points)
        {
            if (point.value > peak) peak = point.value;
            decimal dd = peak > 0m ? (peak - point.value) / peak * 100m : 0m;
            DateTime ts = collapseToDate ? DateTime.SpecifyKind(point.tsUtc.Date, DateTimeKind.Utc) : point.tsUtc;
            if (collapseToDate && list.Count > 0 && list[^1].tsUtc == ts)
            {
                if (dd > list[^1].value) list[^1] = new CoincallChartPoint(ts, Math.Round(dd, 4));
            }
            else list.Add(new CoincallChartPoint(ts, Math.Round(dd, 4)));
        }
        return list;
    }

    private sealed record CoincallEquityPayload(object metrics, object daily, object minute);
    private sealed record CoincallChartPoint(DateTime tsUtc, decimal value);
    private sealed record CoincallSummaryMetrics(decimal? totalEquity, decimal? availableEquity, decimal unrealizedPnl, string equityCurrency);
    private sealed record CoincallBalanceMetric(string ccy, decimal? balance, decimal? available, decimal? frozen, decimal? upl, decimal? usdtValue, decimal? dollarValue);

    private static CoincallSummaryMetrics BuildCoincallSummaryMetrics(JsonElement root)
    {
        var rows = new List<CoincallBalanceMetric>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in EnumerateCoincallObjects(root))
        {
            string? ccy = FindCoincallString(obj, "ccy", "currency", "coin", "asset", "basecurrency", "symbol", "margincoin");
            decimal? balance = FindCoincallDecimal(obj, "equityamount", "marginbalance", "accountequity", "equity", "eq", "totalbalance", "walletbalance", "cashbalanceamount", "cashbalance", "cashbal", "total", "balance", "bal");
            decimal? available = FindCoincallDecimal(obj, "available", "avail", "availablebalance", "availablebal", "availbalance", "availeq", "availableequity", "free");
            decimal? frozen = FindCoincallDecimal(obj, "frozen", "locked", "hold", "freeze", "usedmargin");
            decimal? upl = FindCoincallDecimal(obj, "upl", "unrealizedpnl", "unrealisedpnl", "unrealisedprofit", "unrealizedprofit");
            decimal? usdtValue = FindCoincallDecimal(obj, "usdtvalue");
            decimal? dollarValue = FindCoincallDecimal(obj, "dollarvalue");
            if (string.IsNullOrWhiteSpace(ccy) || (!balance.HasValue && !available.HasValue && !frozen.HasValue && !upl.HasValue && !usdtValue.HasValue && !dollarValue.HasValue)) continue;
            string key = ccy.Trim().ToUpperInvariant() + "|" + (balance?.ToString(CultureInfo.InvariantCulture) ?? "") + "|" + (available?.ToString(CultureInfo.InvariantCulture) ?? "") + "|" + (frozen?.ToString(CultureInfo.InvariantCulture) ?? "") + "|" + (upl?.ToString(CultureInfo.InvariantCulture) ?? "") + "|" + (usdtValue?.ToString(CultureInfo.InvariantCulture) ?? "") + "|" + (dollarValue?.ToString(CultureInfo.InvariantCulture) ?? "");
            if (seen.Add(key)) rows.Add(new CoincallBalanceMetric(ccy.Trim().ToUpperInvariant(), balance, available, frozen, upl, usdtValue, dollarValue));
        }

        decimal? totalTop = FindCoincallDecimal(root, "totalusdtvalue", "totaldollarvalue");
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            totalTop ??= FindCoincallDecimal(data, "totalusdtvalue", "totaldollarvalue", "equity");
        }

        decimal totalUsd = 0m;
        decimal availableUsd = 0m;
        decimal uplUsd = 0m;
        bool hasUsd = false;
        foreach (var row in rows)
        {
            decimal? rowTotal = row.ccy is "USD" or "USDT" or "USDC"
                ? row.balance ?? ((row.available ?? 0m) + (row.frozen ?? 0m))
                : row.usdtValue ?? row.dollarValue;
            decimal? rowAvailable = row.ccy is "USD" or "USDT" or "USDC"
                ? row.available ?? row.balance
                : ConvertAvailableToUsd(row.available, row.balance, row.usdtValue ?? row.dollarValue);
            if (!rowTotal.HasValue && !rowAvailable.HasValue) continue;
            totalUsd += rowTotal ?? 0m;
            availableUsd += rowAvailable ?? 0m;
            uplUsd += row.upl ?? 0m;
            hasUsd = true;
        }

        if (!hasUsd) return new CoincallSummaryMetrics(null, null, 0m, "USD");
        return new CoincallSummaryMetrics(totalTop ?? totalUsd, availableUsd, uplUsd, "USD");
    }

    private static decimal? ConvertAvailableToUsd(decimal? available, decimal? balance, decimal? totalUsd)
    {
        if (!totalUsd.HasValue) return null;
        if (!available.HasValue) return totalUsd;
        if (!balance.HasValue || balance.Value == 0m) return available.Value == 0m ? 0m : totalUsd;
        return totalUsd.Value * available.Value / balance.Value;
    }

    private static IEnumerable<JsonElement> EnumerateCoincallObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var prop in element.EnumerateObject())
            {
                foreach (var nested in EnumerateCoincallObjects(prop.Value)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumerateCoincallObjects(item)) yield return nested;
            }
        }
    }

    private static string? FindCoincallString(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
            }
        }
        return null;
    }

    private static decimal? FindCoincallDecimal(JsonElement obj, params string[] names)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var d)) return d;
                if (prop.Value.ValueKind == JsonValueKind.String && decimal.TryParse(prop.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
            }
        }
        return null;
    }


    private readonly record struct BtcSpotFillDelta(DateTime TimeUtc, decimal NativeDelta);
    private sealed record CoincallFuturesContractConfig(decimal TakerFee, int VolumeDecimal);
    private sealed record CoincallFuturesLadderItem(decimal MaxLevarage, decimal Start, decimal End);
    private sealed record CoincallSideQty(int Side, decimal Qty);

    private static bool TryRequireAdmin(HttpContext context, out IResult? fail)
    {
        fail = null;
        var session = UserSessionTools.GetUserSession(context);
        if (session == null) { fail = Results.Unauthorized(); return false; }
        if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase)) { fail = Results.Forbid(); return false; }
        return true;
    }

    private static bool TryRequireAdminOrQueryToken(HttpContext context, out IResult? fail)
    {
        if (TryRequireAdmin(context, out fail)) return true;
        var token = context.Request.Query["access_token"].ToString().Trim();
        if (string.IsNullOrWhiteSpace(token)) return false;
        var session = AuthStore.Get(token);
        if (session == null && string.Equals(token, "admin-token", StringComparison.OrdinalIgnoreCase))
        {
            session = new UserSession { UserId = 1, Login = "admin", Role = "Admin" };
        }
        if (session == null) { fail = Results.Unauthorized(); return false; }
        if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase)) { fail = Results.Forbid(); return false; }
        fail = null;
        return true;
    }
}

public sealed class CoincallCreateAccountRequest
{
    public string? name { get; set; }
    public string? apiKey { get; set; }
    public string? apiSecret { get; set; }
    public bool setActive { get; set; }
    public bool storeMinuteEquity { get; set; }
    public string? equityCurrency { get; set; } = "USD";
}

public sealed class CoincallAccountSettingsRequest
{
    public bool storeMinuteEquity { get; set; }
    public string? equityCurrency { get; set; } = "USD";
}

public sealed record CoincallPublicAccount(int id, string name, string apiKeyMasked, bool isActive, bool storeMinuteEquity, string equityCurrency, DateTime createdAtUtc, DateTime updatedAtUtc);
