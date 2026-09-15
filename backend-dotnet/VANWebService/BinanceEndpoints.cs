using System;
using System.Collections.Concurrent;
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

public static class BinanceEndpoints
{
    private const string BinanceBaseUrl = "https://api.binance.com";
    private const string BinanceFuturesBaseUrl = "https://fapi.binance.com";
    private const string BinancePortfolioMarginBaseUrl = "https://papi.binance.com";
    private static readonly HttpClient Http = new HttpClient();
    private static readonly ConcurrentDictionary<string, int> FuturesLeverageCache = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public static void MapBinanceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/binance/accounts", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var accounts = await repo.GetAccountsAsync(includeSecrets: false, context.RequestAborted);
            return Results.Json(new { items = accounts.Select(ToPublicAccount).ToList() });
        });

        app.MapPost("/api/admin/binance/accounts", async (HttpContext context, BinanceRepository repo, BinanceAccountRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            if (string.IsNullOrWhiteSpace(dto.name) || string.IsNullOrWhiteSpace(dto.apiKey) || string.IsNullOrWhiteSpace(dto.apiSecret))
            {
                return Results.BadRequest(new { message = "name, apiKey and apiSecret are required" });
            }
            int id = await repo.CreateAccountAsync(
                dto.name!,
                dto.apiKey!,
                dto.apiSecret!,
                dto.setActive || dto.isActive,
                dto.storeMinuteEquity,
                dto.equityCurrency ?? "USD",
                context.RequestAborted);
            var account = await repo.GetAccountByIdAsync(id, includeSecrets: false, context.RequestAborted);
            return Results.Json(new { ok = true, id, account = account == null ? null : ToPublicAccount(account) });
        });

        app.MapPost("/api/admin/binance/test-credentials", async (HttpContext context, BinanceAccountRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var credentials = NormalizeCredentials(dto);
            if (!credentials.ok) return Results.BadRequest(new { ok = false, message = credentials.message });

            var result = await FetchSpotAccountAsync(credentials.value!, context.RequestAborted);
            if (!result.ok)
            {
                return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            }
            return Results.Json(new { ok = true, message = "Credentials accepted by Binance. Spot account access is working." });
        });

        app.MapGet("/api/admin/binance/summary", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });

            var summary = await BuildAccountSummaryAsync(account, context.RequestAborted);
            if (!summary.ok && summary.code == -2015 && summary.message.Contains("Portfolio", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new
                {
                    ok = false,
                    account = ToPublicAccount(account),
                    accountMode = "Classic Trading",
                    message = summary.message,
                    code = summary.code,
                    fetchedAtUtc = DateTime.UtcNow
                });
            }
            if (!summary.ok) return Results.Json(new { ok = false, message = summary.message, code = summary.code }, statusCode: 400);
            await StoreSummarySnapshotAsync(repo, account, summary, account.storeMinuteEquity, context.RequestAborted);
            return Results.Json(new
            {
                ok = true,
                account = ToPublicAccount(account),
                accountMode = IsPortfolioMarginSummary(summary) ? "Portfolio Margin" : "Classic Trading",
                metrics = summary.metrics,
                balances = summary.balances,
                futuresAccount = summary.futuresAccount,
                accountType = summary.accountType,
                canTrade = summary.canTrade,
                canWithdraw = summary.canWithdraw,
                canDeposit = summary.canDeposit,
                nonZeroBalances = summary.nonZeroBalances,
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapGet("/api/admin/binance/pnl", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });

            DateTime now = DateTime.UtcNow;
            var summary = await BuildAccountSummaryAsync(account, context.RequestAborted);
            string warning = string.Empty;
            decimal equity = 0m;
            decimal availableEquity = 0m;
            decimal unrealizedPnl = 0m;
            if (summary.ok)
            {
                await StoreSummarySnapshotAsync(repo, account, summary, account.storeMinuteEquity, context.RequestAborted);
                equity = ReadMetricDecimal(summary.metrics, "totalEquity");
                availableEquity = ReadMetricDecimal(summary.metrics, "availableEquity");
                unrealizedPnl = ReadMetricDecimal(summary.metrics, "unrealizedPnl");
            }
            else
            {
                warning = summary.message;
            }
            var dailyStored = await repo.GetDailyEquityAsync(account.id, 3650, context.RequestAborted);
            var minuteStored = account.storeMinuteEquity ? await repo.GetMinuteEquityAsync(account.id, 24, context.RequestAborted) : new List<BinanceEquityPoint>();
            var latestStored = minuteStored.OrderByDescending(x => x.tsUtc).FirstOrDefault() ?? dailyStored.OrderByDescending(x => x.tsUtc).FirstOrDefault();
            if (!summary.ok && latestStored == null)
            {
                return Results.Json(new { ok = false, message = summary.message, code = summary.code }, statusCode: 400);
            }
            if (!summary.ok && latestStored != null)
            {
                equity = latestStored.totalEquity;
                availableEquity = latestStored.availableEquity;
                unrealizedPnl = latestStored.unrealizedPnl;
            }
            if (dailyStored.Count == 0) dailyStored.Add(new BinanceEquityPoint { tsUtc = now.Date, totalEquity = equity, availableEquity = availableEquity, unrealizedPnl = unrealizedPnl, equityCurrency = "USD" });
            if (account.storeMinuteEquity && minuteStored.Count == 0) minuteStored.Add(new BinanceEquityPoint { tsUtc = now, totalEquity = equity, availableEquity = availableEquity, unrealizedPnl = unrealizedPnl, equityCurrency = "USD" });
            return Results.Json(new
            {
                ok = true,
                account = ToPublicAccount(account),
                warning = string.IsNullOrWhiteSpace(warning) ? null : warning,
                metrics = new
                {
                    equityCurrency = "USD",
                    latestTotalEquity = equity,
                    totalEquity = equity,
                    lastSnapshotUtc = summary.ok ? now : latestStored?.tsUtc,
                    storeMinuteEquity = account.storeMinuteEquity,
                    source = summary.ok ? "van_binance_equity_daily/minute" : "van_binance_equity_daily/minute:last-stored"
                },
                daily = new { points = dailyStored.Select(ToEquityPointPayload).ToList(), drawdownPoints = BuildDrawdownPoints(dailyStored) },
                minute = new { points = minuteStored.Select(ToEquityPointPayload).ToList(), drawdownPoints = BuildDrawdownPoints(minuteStored) }
            });
        });

        app.MapGet("/api/admin/binance/equity/daily-breakdown", (HttpContext context) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            string currency = context.Request.Query["currency"].ToString();
            if (string.IsNullOrWhiteSpace(currency)) currency = "BTC";
            return Results.Json(new
            {
                ok = true,
                requestedCurrency = currency.ToUpperInvariant(),
                equityCurrency = "USD",
                rows = Array.Empty<object>(),
                warnings = new[] { "Binance daily ledger breakdown is not collected yet." }
            });
        });

        app.MapGet("/api/admin/binance/public/futures/exchange-info", async (HttpContext context) =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BinanceFuturesBaseUrl + "/fapi/v1/exchangeInfo");
            using var res = await Http.SendAsync(req, context.RequestAborted);
            string body = await res.Content.ReadAsStringAsync(context.RequestAborted);
            if (!res.IsSuccessStatusCode) return Results.Json(new { ok = false, message = $"Binance futures exchangeInfo HTTP {(int)res.StatusCode}" }, statusCode: (int)res.StatusCode);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var items = BuildFuturesExchangeInfoItems(doc.RootElement);
            return Results.Json(new { ok = true, items, data = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/public/futures/quote-symbols", async (HttpContext context) =>
        {
            var items = await FetchFuturesExchangeInfoItemsAsync(context.RequestAborted);
            return Results.Json(new { ok = true, items, data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/public/futures/instruments", async (HttpContext context) =>
        {
            var items = await FetchFuturesExchangeInfoItemsAsync(context.RequestAborted);
            return Results.Json(new { ok = true, items, data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/candles/minute/symbols", async (HttpContext context) =>
        {
            var items = await FetchFuturesExchangeInfoItemsAsync(context.RequestAborted);
            return Results.Json(new { ok = true, items, data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/spot/private-ws-auth", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            var result = await FetchListenKeyAsync(BinanceBaseUrl, "/api/v3/userDataStream", account.apiKey, context.RequestAborted);
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code, account = ToPublicAccount(account) }, statusCode: 400);
            string url = "wss://stream.binance.com:9443/ws/" + Uri.EscapeDataString(result.listenKey);
            return Results.Json(new { ok = true, account = ToPublicAccount(account), url, listenKey = result.listenKey, expiresAtUtc = DateTime.UtcNow.AddMinutes(55), fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/private-ws-auth", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            var result = await FetchListenKeyAsync(BinanceFuturesBaseUrl, "/fapi/v1/listenKey", account.apiKey, context.RequestAborted);
            string urlBase = "wss://fstream.binance.com/ws/";
            string source = "fapi:/fapi/v1/listenKey";
            if (!result.ok && result.code == -2015)
            {
                result = await FetchListenKeyAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/listenKey", account.apiKey, context.RequestAborted);
                urlBase = "wss://fstream.binance.com/pm/ws/";
                source = "papi:/papi/v1/listenKey";
            }
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code, account = ToPublicAccount(account) }, statusCode: 400);
            string url = urlBase + Uri.EscapeDataString(result.listenKey);
            return Results.Json(new { ok = true, account = ToPublicAccount(account), url, listenKey = result.listenKey, source, expiresAtUtc = DateTime.UtcNow.AddMinutes(55), fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/leverage", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeFuturesTradingSymbol(context.Request.Query["symbol"].ToString());
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });

            var result = await FetchFuturesSymbolConfigDocumentAsync(account.apiKey, account.apiSecret, symbol, context.RequestAborted);
            string source = "fapi:/fapi/v1/symbolConfig";
            if (!result.ok && result.code == -2015)
            {
                result = await FetchPortfolioMarginSymbolConfigDocumentAsync(account.apiKey, account.apiSecret, symbol, context.RequestAborted);
                source = "papi:/papi/v1/um/symbolConfig";
            }

            if (!result.ok || result.document == null)
            {
                result = await FetchFuturesPositionRiskDocumentAsync(account.apiKey, account.apiSecret, symbol, context.RequestAborted);
                source = "fapi:/fapi/v2/positionRisk";
                if (!result.ok || result.document == null)
                {
                    result = await FetchFuturesPositionRiskV3DocumentAsync(account.apiKey, account.apiSecret, symbol, context.RequestAborted);
                    source = "fapi:/fapi/v3/positionRisk";
                }
                if (!result.ok && result.code == -2015)
                {
                    result = await FetchPortfolioMarginPositionRiskDocumentAsync(account.apiKey, account.apiSecret, symbol, context.RequestAborted);
                    source = "papi:/papi/v1/um/positionRisk";
                }
            }

            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code, source }, statusCode: 400);
            using var doc = result.document!;
            JsonElement row = FindSymbolConfigRow(doc.RootElement, symbol);
            decimal leverage = row.ValueKind == JsonValueKind.Object ? ReadDecimal(row, "leverage") : 0m;
            if (leverage <= 0m && FuturesLeverageCache.TryGetValue(FuturesLeverageCacheKey(account.id, symbol), out int cachedLeverage))
            {
                leverage = cachedLeverage;
                source += ":cache";
            }
            if (leverage <= 0m)
            {
                leverage = 1m;
                source += ":default";
            }

            return Results.Json(new
            {
                ok = true,
                account = ToPublicAccount(account),
                symbol,
                data = new
                {
                    symbol,
                    currentLeverage = leverage,
                    leverage,
                    marginType = row.ValueKind == JsonValueKind.Object ? ReadString(row, "marginType") : string.Empty,
                    maxNotionalValue = row.ValueKind == JsonValueKind.Object ? ReadDecimal(row, "maxNotionalValue") : 0m,
                    raw = row.ValueKind == JsonValueKind.Object ? row.Clone() : doc.RootElement.Clone()
                },
                source,
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapPost("/api/admin/binance/futures/leverage", async (HttpContext context, BinanceRepository repo, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeFuturesTradingSymbol(ReadString(payload, "symbol"));
            decimal leverageDecimal = ReadDecimal(payload, "leverage");
            int leverage = (int)Math.Round(leverageDecimal, MidpointRounding.AwayFromZero);
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            if (leverage < 1 || leverage > 125) return Results.BadRequest(new { message = "leverage must be between 1 and 125" });

            var result = await SetFuturesLeverageDocumentAsync(account.apiKey, account.apiSecret, symbol, leverage, context.RequestAborted);
            string source = "fapi:/fapi/v1/leverage";
            if (!result.ok && result.code == -2015)
            {
                result = await SetPortfolioMarginFuturesLeverageDocumentAsync(account.apiKey, account.apiSecret, symbol, leverage, context.RequestAborted);
                source = "papi:/papi/v1/um/leverage";
            }
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            using var doc = result.document!;
            decimal currentLeverage = ReadDecimal(doc.RootElement, "leverage");
            if (currentLeverage <= 0m) currentLeverage = leverage;
            FuturesLeverageCache[FuturesLeverageCacheKey(account.id, symbol)] = (int)currentLeverage;
            return Results.Json(new
            {
                ok = true,
                account = ToPublicAccount(account),
                symbol,
                data = new
                {
                    symbol,
                    currentLeverage,
                    leverage = currentLeverage,
                    maxNotionalValue = ReadDecimal(doc.RootElement, "maxNotionalValue"),
                    raw = doc.RootElement.Clone()
                },
                source,
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapPost("/api/admin/binance/futures/max-available", async (HttpContext context, BinanceRepository repo, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeFuturesTradingSymbol(ReadString(payload, "symbol"));
            decimal buyPrice = ReadDecimal(payload, "buyPrice");
            decimal sellPrice = ReadDecimal(payload, "sellPrice");
            decimal markPrice = ReadDecimal(payload, "markPrice");
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });

            var accountResult = await FetchFuturesAccountDocumentAsync(account.apiKey, account.apiSecret, context.RequestAborted);
            decimal available = 0m;
            string availableSource = "fapi:/fapi/v3/account";
            if (accountResult.ok && accountResult.document != null)
            {
                using var accountDoc = accountResult.document;
                available = ReadDecimal(accountDoc.RootElement, "availableBalance");
            }
            var pmAccountResult = await FetchPortfolioMarginAccountDocumentAsync(account.apiKey, account.apiSecret, context.RequestAborted);
            if (pmAccountResult.ok && pmAccountResult.document != null)
            {
                using var pmAccountDoc = pmAccountResult.document;
                decimal pmAvailable = ReadDecimal(pmAccountDoc.RootElement, "totalAvailableBalance");
                if (pmAvailable == 0m) pmAvailable = ReadDecimal(pmAccountDoc.RootElement, "availableBalance");
                if (pmAvailable > 0m || ReadDecimal(pmAccountDoc.RootElement, "accountEquity") > 0m || ReadDecimal(pmAccountDoc.RootElement, "actualEquity") > 0m)
                {
                    available = pmAvailable;
                    availableSource = "papi:/papi/v1/account";
                }
            }
            if (!accountResult.ok && !pmAccountResult.ok) return Results.Json(new { ok = false, message = accountResult.message, code = accountResult.code }, statusCode: 400);

            decimal leverage = 1m;
            var levResult = await FetchFuturesSymbolConfigDocumentAsync(account.apiKey, account.apiSecret, symbol, context.RequestAborted);
            if (!levResult.ok && levResult.code == -2015)
            {
                levResult = await FetchPortfolioMarginSymbolConfigDocumentAsync(account.apiKey, account.apiSecret, symbol, context.RequestAborted);
            }
            if (levResult.ok && levResult.document != null)
            {
                using var levDoc = levResult.document;
                JsonElement row = FindSymbolConfigRow(levDoc.RootElement, symbol);
                decimal parsedLeverage = ReadDecimal(row, "leverage");
                if (parsedLeverage > 0m) leverage = parsedLeverage;
            }

            decimal LongQty(decimal price) => price > 0m ? Math.Max(0m, available * leverage / price) : 0m;
            decimal longPrice = buyPrice > 0m ? buyPrice : markPrice;
            decimal shortPrice = sellPrice > 0m ? sellPrice : markPrice;
            return Results.Json(new
            {
                ok = true,
                account = ToPublicAccount(account),
                symbol,
                availableBalance = available,
                availableSource,
                leverage,
                buy = new { qty = LongQty(longPrice), price = longPrice },
                sell = new { qty = LongQty(shortPrice), price = shortPrice },
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapPost("/api/admin/binance/futures/orders/place", async (HttpContext context, BinanceRepository repo, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeFuturesTradingSymbol(ReadString(payload, "symbol"));
            string sideRaw = ReadString(payload, "tradeSide");
            if (string.IsNullOrWhiteSpace(sideRaw)) sideRaw = ReadString(payload, "side");
            string typeRaw = ReadString(payload, "tradeType");
            if (string.IsNullOrWhiteSpace(typeRaw)) typeRaw = ReadString(payload, "type");
            string side = NormalizeFuturesOrderSide(sideRaw);
            string orderType = NormalizeFuturesOrderType(typeRaw);
            decimal quantity = ReadDecimal(payload, "qty");
            if (quantity <= 0m) quantity = ReadDecimal(payload, "quantity");
            decimal price = ReadDecimal(payload, "price");
            if (price <= 0m) price = ReadDecimal(payload, "px");
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            if (string.IsNullOrWhiteSpace(side)) return Results.BadRequest(new { message = "side must be BUY or SELL" });
            if (string.IsNullOrWhiteSpace(orderType)) return Results.BadRequest(new { message = "type must be LIMIT, MARKET or POST_ONLY" });
            if (quantity <= 0m) return Results.BadRequest(new { message = "qty must be greater than 0" });
            if (orderType != "MARKET" && price <= 0m) return Results.BadRequest(new { message = "price is required for limit orders" });

            string binanceType = orderType == "POST_ONLY" ? "LIMIT" : orderType;
            var parts = new List<string>
            {
                "symbol=" + Uri.EscapeDataString(symbol),
                "side=" + Uri.EscapeDataString(side),
                "type=" + Uri.EscapeDataString(binanceType),
                "quantity=" + Uri.EscapeDataString(quantity.ToString(CultureInfo.InvariantCulture))
            };
            if (binanceType == "LIMIT")
            {
                parts.Add("timeInForce=" + Uri.EscapeDataString(orderType == "POST_ONLY" ? "GTX" : "GTC"));
                parts.Add("price=" + Uri.EscapeDataString(price.ToString(CultureInfo.InvariantCulture)));
            }
            string clientOrderId = ReadString(payload, "clientOrderId");
            if (!string.IsNullOrWhiteSpace(clientOrderId)) parts.Add("newClientOrderId=" + Uri.EscapeDataString(clientOrderId));

            string signedQuery = string.Join("&", parts);
            var result = await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v1/order", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Post, signedQuery);
            string source = "fapi:/fapi/v1/order";
            if (!result.ok && result.code == -2015)
            {
                result = await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/um/order", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Post, signedQuery);
                source = "papi:/papi/v1/um/order";
            }
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeFuturesOrders(JsonDocument.Parse("[" + doc.RootElement.GetRawText() + "]").RootElement, includeTerminal: true);
            return Results.Json(new
            {
                ok = true,
                account = ToPublicAccount(account),
                symbol,
                side,
                orderType,
                qty = quantity,
                price,
                data = doc.RootElement.Clone(),
                order = items.FirstOrDefault(),
                source,
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapPost("/api/admin/binance/spot/orders/place", async (HttpContext context, BinanceRepository repo, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeSpotTradingSymbol(ReadString(payload, "symbol"));
            string sideRaw = ReadString(payload, "tradeSide");
            if (string.IsNullOrWhiteSpace(sideRaw)) sideRaw = ReadString(payload, "side");
            string typeRaw = ReadString(payload, "tradeType");
            if (string.IsNullOrWhiteSpace(typeRaw)) typeRaw = ReadString(payload, "type");
            string side = NormalizeSpotOrderSide(sideRaw);
            string orderType = NormalizeSpotOrderType(typeRaw);
            decimal quantity = ReadDecimal(payload, "qty");
            if (quantity <= 0m) quantity = ReadDecimal(payload, "quantity");
            decimal price = ReadDecimal(payload, "price");
            if (price <= 0m) price = ReadDecimal(payload, "px");
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            if (string.IsNullOrWhiteSpace(side)) return Results.BadRequest(new { message = "side must be BUY or SELL" });
            if (string.IsNullOrWhiteSpace(orderType)) return Results.BadRequest(new { message = "type must be LIMIT, MARKET or POST_ONLY" });
            if (quantity <= 0m) return Results.BadRequest(new { message = "qty must be greater than 0" });
            if (orderType != "MARKET" && price <= 0m) return Results.BadRequest(new { message = "price is required for limit orders" });

            string binanceType = orderType == "POST_ONLY" ? "LIMIT_MAKER" : orderType;
            var parts = new List<string>
            {
                "symbol=" + Uri.EscapeDataString(symbol),
                "side=" + Uri.EscapeDataString(side),
                "type=" + Uri.EscapeDataString(binanceType),
                "quantity=" + Uri.EscapeDataString(quantity.ToString(CultureInfo.InvariantCulture))
            };
            if (binanceType == "LIMIT")
            {
                parts.Add("timeInForce=GTC");
                parts.Add("price=" + Uri.EscapeDataString(price.ToString(CultureInfo.InvariantCulture)));
            }
            else if (binanceType == "LIMIT_MAKER")
            {
                parts.Add("price=" + Uri.EscapeDataString(price.ToString(CultureInfo.InvariantCulture)));
            }
            string clientOrderId = ReadString(payload, "clientOrderId");
            if (!string.IsNullOrWhiteSpace(clientOrderId)) parts.Add("newClientOrderId=" + Uri.EscapeDataString(clientOrderId));

            var result = await FetchSignedDocumentAsync(BinanceBaseUrl, "/api/v3/order", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Post, string.Join("&", parts));
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeSpotOrders(doc.RootElement, includeTerminal: true);
            return Results.Json(new
            {
                ok = true,
                account = ToPublicAccount(account),
                symbol,
                side,
                orderType,
                qty = quantity,
                price,
                data = doc.RootElement.Clone(),
                order = items.FirstOrDefault(),
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapGet("/api/admin/binance/spot/orders/open", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeSpotTradingSymbol(context.Request.Query["symbol"].ToString());
            if (string.IsNullOrWhiteSpace(symbol)) symbol = NormalizeSpotTradingSymbol(context.Request.Query["restSymbol"].ToString());
            string? query = string.IsNullOrWhiteSpace(symbol) ? null : "symbol=" + Uri.EscapeDataString(symbol);
            var result = await FetchSignedDocumentAsync(BinanceBaseUrl, "/api/v3/openOrders", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, query);
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeSpotOrders(doc.RootElement, includeTerminal: false);
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items, data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapPost("/api/admin/binance/spot/orders/cancel", async (HttpContext context, BinanceRepository repo, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeSpotTradingSymbol(ReadString(payload, "symbol"));
            string orderId = ReadString(payload, "orderId");
            string clientOrderId = ReadString(payload, "clientOrderId");
            if (string.IsNullOrWhiteSpace(clientOrderId)) clientOrderId = ReadString(payload, "origClientOrderId");
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            if (string.IsNullOrWhiteSpace(orderId) && string.IsNullOrWhiteSpace(clientOrderId)) return Results.BadRequest(new { message = "orderId or clientOrderId is required" });

            var parts = new List<string> { "symbol=" + Uri.EscapeDataString(symbol) };
            if (!string.IsNullOrWhiteSpace(orderId)) parts.Add("orderId=" + Uri.EscapeDataString(orderId));
            else parts.Add("origClientOrderId=" + Uri.EscapeDataString(clientOrderId));
            var result = await FetchSignedDocumentAsync(BinanceBaseUrl, "/api/v3/order", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Delete, string.Join("&", parts));
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeSpotOrders(doc.RootElement, includeTerminal: true);
            return Results.Json(new
            {
                ok = true,
                account = ToPublicAccount(account),
                symbol,
                orderId,
                clientOrderId,
                data = doc.RootElement.Clone(),
                canceledOrder = items.FirstOrDefault(),
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapGet("/api/admin/binance/spot/orders/history", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            var symbols = ResolveSpotHistorySymbols(context);
            int limit = ReadQueryInt(context, "limit", 100, 1, 1000);
            var items = new List<object>();
            foreach (string symbol in symbols)
            {
                var result = await FetchSignedDocumentAsync(BinanceBaseUrl, "/api/v3/allOrders", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, "symbol=" + Uri.EscapeDataString(symbol) + "&limit=" + limit.ToString(CultureInfo.InvariantCulture));
                if (!result.ok) continue;
                using var doc = result.document!;
                items.AddRange(NormalizeSpotOrders(doc.RootElement, includeTerminal: true));
            }
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items, data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/spot/trades/history", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            var symbols = ResolveSpotHistorySymbols(context);
            int limit = ReadQueryInt(context, "limit", 100, 1, 1000);
            var items = new List<object>();
            foreach (string symbol in symbols)
            {
                var result = await FetchSignedDocumentAsync(BinanceBaseUrl, "/api/v3/myTrades", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, "symbol=" + Uri.EscapeDataString(symbol) + "&limit=" + limit.ToString(CultureInfo.InvariantCulture));
                if (!result.ok) continue;
                using var doc = result.document!;
                items.AddRange(NormalizeSpotTrades(doc.RootElement));
            }
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items, data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/orders/open", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeFuturesTradingSymbol(context.Request.Query["symbol"].ToString());
            string? query = string.IsNullOrWhiteSpace(symbol) ? null : "symbol=" + Uri.EscapeDataString(symbol);
            var result = await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v1/openOrders", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, query);
            string source = "fapi:/fapi/v1/openOrders";
            if (!result.ok && result.code == -2015)
            {
                result = await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/um/openOrders", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, query);
                source = "papi:/papi/v1/um/openOrders";
            }
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeFuturesOrders(doc.RootElement, includeTerminal: false);
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items, data = items, list = items, source, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/orders/history", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            var symbols = ResolveFuturesHistorySymbols(context);
            int limit = ReadQueryInt(context, "limit", ReadQueryInt(context, "pageSize", 100, 1, 1000), 1, 1000);
            var items = new List<object>();
            foreach (string symbol in symbols)
            {
                var result = await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v1/allOrders", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, "symbol=" + Uri.EscapeDataString(symbol) + "&limit=" + limit.ToString(CultureInfo.InvariantCulture));
                if (!result.ok) continue;
                using var doc = result.document!;
                items.AddRange(NormalizeFuturesOrders(doc.RootElement, includeTerminal: true));
            }
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items = items.OrderByDescending(row => GetObjectLong(row, "updateTime")).ToList(), data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/trades/history", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            var symbols = ResolveFuturesHistorySymbols(context);
            int limit = ReadQueryInt(context, "limit", ReadQueryInt(context, "pageSize", 100, 1, 1000), 1, 1000);
            var items = new List<object>();
            foreach (string symbol in symbols)
            {
                var result = await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v1/userTrades", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, "symbol=" + Uri.EscapeDataString(symbol) + "&limit=" + limit.ToString(CultureInfo.InvariantCulture));
                if (!result.ok) continue;
                using var doc = result.document!;
                items.AddRange(NormalizeFuturesTrades(doc.RootElement));
            }
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items = items.OrderByDescending(row => GetObjectLong(row, "time")).ToList(), data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/funding/history/db", (HttpContext context) =>
        {
            return Results.Json(new { ok = true, source = "binance_futures_income_db_not_collected", items = Array.Empty<object>(), data = new { list = Array.Empty<object>() }, rows = Array.Empty<object>(), fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/funding/history", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeFuturesTradingSymbol(context.Request.Query["symbol"].ToString());
            int limit = ReadQueryInt(context, "limit", ReadQueryInt(context, "pageSize", 100, 1, 1000), 1, 1000);
            string query = "incomeType=FUNDING_FEE&limit=" + limit.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(symbol)) query += "&symbol=" + Uri.EscapeDataString(symbol);
            var result = await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v1/income", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, query);
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeFuturesIncome(doc.RootElement, "FUNDING_FEE");
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items, data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/positions/history", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeFuturesTradingSymbol(context.Request.Query["symbol"].ToString());
            int limit = ReadQueryInt(context, "limit", ReadQueryInt(context, "pageSize", 100, 1, 1000), 1, 1000);
            string query = "incomeType=REALIZED_PNL&limit=" + limit.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(symbol)) query += "&symbol=" + Uri.EscapeDataString(symbol);
            var result = await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v1/income", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, query);
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeFuturesIncome(doc.RootElement, "REALIZED_PNL");
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items, data = items, list = items, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/exercise", (HttpContext context) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            return Results.Json(new { ok = true, market = "binance_usdm_futures", items = Array.Empty<object>(), data = Array.Empty<object>(), list = Array.Empty<object>(), message = "USD-M futures do not have option exercise records.", fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/binance/futures/assets", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            var result = await FetchFuturesAccountDocumentAsync(account.apiKey, account.apiSecret, context.RequestAborted);
            string source = "fapi:/fapi/v3/account";
            if (!result.ok && result.code == -2015)
            {
                result = await FetchPortfolioMarginBalanceDocumentAsync(account.apiKey, account.apiSecret, context.RequestAborted);
                source = "papi:/papi/v1/balance";
            }
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code, source }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeFuturesAssets(doc.RootElement);
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items, data = items, list = items, source, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapPost("/api/admin/binance/futures/orders/cancel", async (HttpContext context, BinanceRepository repo, JsonElement payload) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeFuturesTradingSymbol(ReadString(payload, "symbol"));
            string orderId = ReadString(payload, "orderId");
            string clientOrderId = ReadString(payload, "clientOrderId");
            if (string.IsNullOrWhiteSpace(symbol)) return Results.BadRequest(new { message = "symbol is required" });
            if (string.IsNullOrWhiteSpace(orderId) && string.IsNullOrWhiteSpace(clientOrderId)) return Results.BadRequest(new { message = "orderId or clientOrderId is required" });

            var parts = new List<string> { "symbol=" + Uri.EscapeDataString(symbol) };
            if (!string.IsNullOrWhiteSpace(orderId)) parts.Add("orderId=" + Uri.EscapeDataString(orderId));
            else parts.Add("origClientOrderId=" + Uri.EscapeDataString(clientOrderId));
            var result = await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v1/order", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Delete, string.Join("&", parts));
            string source = "fapi:/fapi/v1/order";
            if (!result.ok && result.code == -2015)
            {
                result = await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/um/order", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Delete, string.Join("&", parts));
                source = "papi:/papi/v1/um/order";
            }
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code, source }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeFuturesOrders(JsonDocument.Parse("[" + doc.RootElement.GetRawText() + "]").RootElement, includeTerminal: true);
            return Results.Json(new
            {
                ok = true,
                account = ToPublicAccount(account),
                symbol,
                orderId,
                clientOrderId,
                data = doc.RootElement.Clone(),
                source,
                canceledOrder = items.FirstOrDefault(),
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapGet("/api/admin/binance/futures/positions", async (HttpContext context, BinanceRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "Binance account not found" });
            string symbol = NormalizeFuturesTradingSymbol(context.Request.Query["symbol"].ToString());
            string? query = string.IsNullOrWhiteSpace(symbol) ? null : "symbol=" + Uri.EscapeDataString(symbol);
            var result = await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v2/positionRisk", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, query);
            string source = "fapi:/fapi/v2/positionRisk";
            if (!result.ok && result.code == -2015)
            {
                result = await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/um/positionRisk", account.apiKey, account.apiSecret, context.RequestAborted, HttpMethod.Get, query);
                source = "papi:/papi/v1/um/positionRisk";
            }
            if (!result.ok) return Results.Json(new { ok = false, message = result.message, code = result.code }, statusCode: 400);
            using var doc = result.document!;
            var items = NormalizeFuturesPositions(doc.RootElement);
            return Results.Json(new { ok = true, account = ToPublicAccount(account), items, data = items, positions = items, source, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapPut("/api/admin/binance/accounts/{id:int}/settings", async (HttpContext context, BinanceRepository repo, int id, BinanceAccountRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.UpdateAccountSettingsAsync(id, dto.storeMinuteEquity, dto.equityCurrency ?? "USD", context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "Binance account not found" });
        });

        app.MapPut("/api/admin/binance/accounts/{id:int}/activate", async (HttpContext context, BinanceRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.SetActiveAccountAsync(id, context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "Binance account not found" });
        });

        app.MapDelete("/api/admin/binance/accounts/{id:int}", async (HttpContext context, BinanceRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            bool ok = await repo.DeleteAccountAsync(id, context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "Binance account not found" });
        });

        app.MapGet("/api/admin/binance/futures/funding/minute", async (HttpContext context, BinanceRepository repo) =>
        {
            string symbol = BinanceRepository.NormalizeFundingSymbol(context.Request.Query["symbol"].ToString());
            int limit = int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 5000;
            var rows = await repo.GetFundingMinuteAsync(symbol, Math.Clamp(limit, 1, 129600), context.RequestAborted);
            var items = rows.Select(ToFundingMinutePayloadRow).ToList();
            return Results.Json(new { source = "van_binance_futures_funding_minute", symbol, order = "oldest-to-newest", rows = items, data = new { list = items }, fetchedAtUtc = DateTime.UtcNow });
        });
    }

    private static object ToFundingMinutePayloadRow(BinanceFundingRow row)
    {
        long ctime = new DateTimeOffset(row.minuteUtc).ToUnixTimeMilliseconds();
        long? fundingTime = row.fundingTimeUtc.HasValue ? new DateTimeOffset(row.fundingTimeUtc.Value).ToUnixTimeMilliseconds() : null;
        long? nextFundingTime = row.nextFundingTimeUtc.HasValue ? new DateTimeOffset(row.nextFundingTimeUtc.Value).ToUnixTimeMilliseconds() : null;
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
            displayName = string.IsNullOrWhiteSpace(row.displayName) ? BinanceRepository.DisplayName(row.symbol) : row.displayName,
            tsUtc = row.minuteUtc,
            snapshotMinuteUtc = row.minuteUtc,
            snapshot_minute_utc = row.minuteUtc,
            minuteUtc = row.minuteUtc,
            observedMinuteUtc = row.minuteUtc,
            ctime,
            fundingTimeUtc = row.fundingTimeUtc,
            nextFundingTimeUtc = row.nextFundingTimeUtc,
            fundingTime,
            nextFundingTime,
            currentFunding = row.currentFunding,
            current_funding = PercentRate(row.currentFunding),
            interest8h = row.interest8h,
            interest_8h = PercentRate(row.interest8h),
            markPrice = row.markPrice,
            indexPrice = row.indexPrice,
            source = row.source,
            raw
        };
    }

    private static decimal? PercentRate(decimal? rate)
    {
        if (!rate.HasValue) return null;
        var v = rate.Value;
        return Math.Abs(v) <= 1m ? v * 100m : v;
    }

    private static (bool ok, string? message, BinanceAccountRequest? value) NormalizeCredentials(BinanceAccountRequest dto)
    {
        dto.apiKey = (dto.apiKey ?? string.Empty).Trim();
        dto.apiSecret = (dto.apiSecret ?? string.Empty).Trim();
        dto.name = string.IsNullOrWhiteSpace(dto.name) ? "Selected Binance account" : dto.name.Trim();
        dto.equityCurrency = BinanceRepository.NormalizeEquityCurrency(dto.equityCurrency);
        if (string.IsNullOrWhiteSpace(dto.apiKey) || string.IsNullOrWhiteSpace(dto.apiSecret))
        {
            return (false, "apiKey and apiSecret are required", null);
        }
        return (true, null, dto);
    }

    private static async Task<(bool ok, string message, int code)> FetchSpotAccountAsync(BinanceAccountRequest credentials, CancellationToken ct)
    {
        var result = await FetchSpotAccountDocumentAsync(credentials.apiKey!, credentials.apiSecret!, ct);
        result.document?.Dispose();
        return (result.ok, result.message, result.code);
    }

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchSpotAccountDocumentAsync(string apiKey, string apiSecret, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinanceBaseUrl, "/api/v3/account", apiKey, apiSecret, ct);

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchFuturesAccountDocumentAsync(string apiKey, string apiSecret, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v3/account", apiKey, apiSecret, ct);

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchPortfolioMarginAccountDocumentAsync(string apiKey, string apiSecret, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/account", apiKey, apiSecret, ct);

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchPortfolioMarginBalanceDocumentAsync(string apiKey, string apiSecret, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/balance", apiKey, apiSecret, ct);

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchFuturesSymbolConfigDocumentAsync(string apiKey, string apiSecret, string symbol, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v1/symbolConfig", apiKey, apiSecret, ct, HttpMethod.Get, "symbol=" + Uri.EscapeDataString(symbol));

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchPortfolioMarginSymbolConfigDocumentAsync(string apiKey, string apiSecret, string symbol, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/um/symbolConfig", apiKey, apiSecret, ct, HttpMethod.Get, "symbol=" + Uri.EscapeDataString(symbol));

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchFuturesPositionRiskDocumentAsync(string apiKey, string apiSecret, string symbol, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v2/positionRisk", apiKey, apiSecret, ct, HttpMethod.Get, "symbol=" + Uri.EscapeDataString(symbol));

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchFuturesPositionRiskV3DocumentAsync(string apiKey, string apiSecret, string symbol, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v3/positionRisk", apiKey, apiSecret, ct, HttpMethod.Get, "symbol=" + Uri.EscapeDataString(symbol));

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchPortfolioMarginPositionRiskDocumentAsync(string apiKey, string apiSecret, string symbol, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/um/positionRisk", apiKey, apiSecret, ct, HttpMethod.Get, "symbol=" + Uri.EscapeDataString(symbol));

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> SetFuturesLeverageDocumentAsync(string apiKey, string apiSecret, string symbol, int leverage, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinanceFuturesBaseUrl, "/fapi/v1/leverage", apiKey, apiSecret, ct, HttpMethod.Post, "symbol=" + Uri.EscapeDataString(symbol) + "&leverage=" + leverage.ToString(CultureInfo.InvariantCulture));

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> SetPortfolioMarginFuturesLeverageDocumentAsync(string apiKey, string apiSecret, string symbol, int leverage, CancellationToken ct)
        => await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/um/leverage", apiKey, apiSecret, ct, HttpMethod.Post, "symbol=" + Uri.EscapeDataString(symbol) + "&leverage=" + leverage.ToString(CultureInfo.InvariantCulture));

    private static async Task<(decimal leverage, string source)> TryDerivePortfolioMarginLeverageFromOpenOrdersAsync(BinanceAccountRecord account, string symbol, CancellationToken ct)
    {
        string normalizedSymbol = NormalizeFuturesTradingSymbol(symbol);
        if (string.IsNullOrWhiteSpace(normalizedSymbol)) return (0m, string.Empty);

        var ordersResult = await FetchSignedDocumentAsync(BinancePortfolioMarginBaseUrl, "/papi/v1/um/openOrders", account.apiKey, account.apiSecret, ct, HttpMethod.Get);
        if (!ordersResult.ok || ordersResult.document == null) return (0m, string.Empty);

        decimal selectedNotional = 0m;
        decimal totalNotional = 0m;
        using (var ordersDoc = ordersResult.document)
        {
            if (ordersDoc.RootElement.ValueKind != JsonValueKind.Array) return (0m, string.Empty);
            foreach (var order in ordersDoc.RootElement.EnumerateArray())
            {
                string orderSymbol = ReadString(order, "symbol").ToUpperInvariant();
                decimal price = ReadDecimal(order, "price");
                decimal qty = ReadDecimal(order, "origQty") - ReadDecimal(order, "executedQty");
                if (qty <= 0m) qty = ReadDecimal(order, "origQty");
                decimal notional = Math.Abs(price * qty);
                if (notional <= 0m) continue;
                totalNotional += notional;
                if (orderSymbol == normalizedSymbol) selectedNotional += notional;
            }
        }
        if (selectedNotional <= 0m || totalNotional <= 0m) return (0m, string.Empty);

        var accountResult = await FetchPortfolioMarginAccountDocumentAsync(account.apiKey, account.apiSecret, ct);
        if (!accountResult.ok || accountResult.document == null) return (0m, string.Empty);

        decimal initialMargin;
        using (var accountDoc = accountResult.document)
        {
            initialMargin = ReadDecimal(accountDoc.RootElement, "accountInitialMargin");
        }
        if (initialMargin <= 0m) return (0m, string.Empty);

        // accountInitialMargin is account-level, so derive only when selected orders are effectively all open notional.
        if (Math.Abs(totalNotional - selectedNotional) > Math.Max(0.01m, totalNotional * 0.0001m)) return (0m, string.Empty);

        decimal rawLeverage = selectedNotional / initialMargin;
        int rounded = (int)Math.Round(rawLeverage, MidpointRounding.AwayFromZero);
        if (rounded < 1 || rounded > 125) return (0m, string.Empty);
        return (rounded, "papi:/papi/v1/um/openOrders+papi:/papi/v1/account:derived");
    }

    private static async Task<(bool ok, string message, int code, JsonDocument? document)> FetchSignedDocumentAsync(string baseUrl, string path, string apiKey, string apiSecret, CancellationToken ct, HttpMethod? method = null, string? extraQuery = null)
    {
        const string recvWindow = "5000";
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string prefix = string.IsNullOrWhiteSpace(extraQuery) ? string.Empty : extraQuery.TrimStart('?', '&') + "&";
        string query = prefix + "timestamp=" + timestamp + "&recvWindow=" + recvWindow;
        string signature;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret)))
        {
            signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(query))).ToLowerInvariant();
        }

        using var req = new HttpRequestMessage(method ?? HttpMethod.Get, baseUrl + path + "?" + query + "&signature=" + signature);
        req.Headers.TryAddWithoutValidation("X-MBX-APIKEY", apiKey);

        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        try
        {
            var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            int code = 0;
            string message = string.Empty;
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                code = doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var parsedCode) ? parsedCode : 0;
                message = doc.RootElement.TryGetProperty("msg", out var msgEl) ? (msgEl.GetString() ?? string.Empty) : string.Empty;
            }
            if (res.IsSuccessStatusCode)
            {
                return (true, "OK", 0, doc);
            }
            doc.Dispose();
            return (false, string.IsNullOrWhiteSpace(message) ? $"Binance returned HTTP {(int)res.StatusCode}" : message, code, null);
        }
        catch (JsonException)
        {
            return (false, $"Binance returned HTTP {(int)res.StatusCode}", 0, null);
        }
    }

    private static async Task<(bool ok, string message, int code, string listenKey)> FetchListenKeyAsync(string baseUrl, string path, string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + path);
        req.Headers.TryAddWithoutValidation("X-MBX-APIKEY", apiKey);
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            int code = 0;
            string message = string.Empty;
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                code = doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var parsedCode) ? parsedCode : 0;
                message = doc.RootElement.TryGetProperty("msg", out var msgEl) ? (msgEl.GetString() ?? string.Empty) : string.Empty;
                string listenKey = doc.RootElement.TryGetProperty("listenKey", out var keyEl) ? (keyEl.GetString() ?? string.Empty) : string.Empty;
                if (res.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(listenKey)) return (true, "OK", 0, listenKey);
            }
            return (false, string.IsNullOrWhiteSpace(message) ? $"Binance returned HTTP {(int)res.StatusCode}" : message, code, string.Empty);
        }
        catch (JsonException)
        {
            return (false, $"Binance returned HTTP {(int)res.StatusCode}", 0, string.Empty);
        }
    }

    private static List<object> BuildFuturesExchangeInfoItems(JsonElement root)
    {
        var list = new List<object>();
        if (!root.TryGetProperty("symbols", out var symbols) || symbols.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in symbols.EnumerateArray())
        {
            string symbol = ReadString(item, "symbol").ToUpperInvariant();
            string status = ReadString(item, "status").ToUpperInvariant();
            string contractType = ReadString(item, "contractType").ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol) || (status != "TRADING" && status != string.Empty)) continue;
            decimal minQty = 0m;
            decimal stepSize = 0m;
            decimal minNotional = 0m;
            if (item.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Array)
            {
                foreach (var filter in filters.EnumerateArray())
                {
                    string type = ReadString(filter, "filterType").ToUpperInvariant();
                    if (type == "LOT_SIZE" || type == "MARKET_LOT_SIZE")
                    {
                        decimal nextMinQty = ReadDecimal(filter, "minQty");
                        decimal nextStepSize = ReadDecimal(filter, "stepSize");
                        if (nextMinQty > 0m && (minQty == 0m || nextMinQty > minQty)) minQty = nextMinQty;
                        if (nextStepSize > 0m && (stepSize == 0m || nextStepSize > stepSize)) stepSize = nextStepSize;
                    }
                    else if (type == "MIN_NOTIONAL")
                    {
                        minNotional = ReadDecimal(filter, "notional");
                        if (minNotional == 0m) minNotional = ReadDecimal(filter, "minNotional");
                    }
                }
            }
            list.Add(new
            {
                symbol,
                displayName = DisplayFuturesSymbol(symbol),
                baseToken = ReadString(item, "baseAsset"),
                quoteToken = ReadString(item, "quoteAsset"),
                contractType,
                minQty,
                stepSize,
                minNotional
            });
        }
        return list;
    }

    private static async Task<List<object>> FetchFuturesExchangeInfoItemsAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, BinanceFuturesBaseUrl + "/fapi/v1/exchangeInfo");
        using var res = await Http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return new List<object>();
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        return BuildFuturesExchangeInfoItems(doc.RootElement);
    }

    private static string NormalizeFuturesTradingSymbol(string? symbol)
    {
        string value = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (value.EndsWith("-PERP", StringComparison.OrdinalIgnoreCase)) value = value[..^5];
        if (value.EndsWith("_PERP", StringComparison.OrdinalIgnoreCase)) value = value[..^5];
        if (value.EndsWith("PERP", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        value = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (value.EndsWith("USD", StringComparison.OrdinalIgnoreCase) && !value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)) value = value[..^3] + "USDT";
        return value;
    }

    private static string DisplayFuturesSymbol(string symbol)
    {
        string value = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)) return value[..^4] + "USDT-Perp";
        return value;
    }

    private static string NormalizeFuturesOrderSide(string? side)
    {
        string value = (side ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            "1" or "BUY" or "LONG" => "BUY",
            "2" or "SELL" or "SHORT" => "SELL",
            _ => string.Empty
        };
    }

    private static string NormalizeFuturesOrderType(string? type)
    {
        string value = (type ?? string.Empty).Trim().ToUpperInvariant().Replace("-", "_");
        return value switch
        {
            "1" or "LIMIT" => "LIMIT",
            "2" or "MARKET" => "MARKET",
            "3" or "POST_ONLY" or "POSTONLY" => "POST_ONLY",
            _ => string.Empty
        };
    }

    private static string NormalizeSpotTradingSymbol(string? symbol)
    {
        string value = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        value = value.Replace("/", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string DisplaySpotSymbol(string symbol)
    {
        string value = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (value.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) && value.Length > 4) return value[..^4] + "/USDT";
        if (value.EndsWith("USDC", StringComparison.OrdinalIgnoreCase) && value.Length > 4) return value[..^4] + "/USDC";
        if (value.EndsWith("BTC", StringComparison.OrdinalIgnoreCase) && value.Length > 3) return value[..^3] + "/BTC";
        if (value.EndsWith("ETH", StringComparison.OrdinalIgnoreCase) && value.Length > 3) return value[..^3] + "/ETH";
        return value;
    }

    private static string NormalizeSpotOrderSide(string? side)
    {
        string value = (side ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            "1" or "BUY" or "LONG" => "BUY",
            "2" or "SELL" or "SHORT" => "SELL",
            _ => string.Empty
        };
    }

    private static string NormalizeSpotOrderType(string? type)
    {
        string value = (type ?? string.Empty).Trim().ToUpperInvariant().Replace("-", "_");
        return value switch
        {
            "1" or "LIMIT" => "LIMIT",
            "2" or "MARKET" => "MARKET",
            "3" or "POST_ONLY" or "POSTONLY" or "LIMIT_MAKER" => "POST_ONLY",
            _ => string.Empty
        };
    }

    private static string[] ResolveSpotHistorySymbols(HttpContext context)
    {
        string symbol = NormalizeSpotTradingSymbol(context.Request.Query["symbol"].ToString());
        if (!string.IsNullOrWhiteSpace(symbol)) return new[] { symbol };

        string restSymbol = NormalizeSpotTradingSymbol(context.Request.Query["restSymbol"].ToString());
        if (!string.IsNullOrWhiteSpace(restSymbol)) return new[] { restSymbol };

        return new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT" };
    }

    private static string[] ResolveFuturesHistorySymbols(HttpContext context)
    {
        string symbol = NormalizeFuturesTradingSymbol(context.Request.Query["symbol"].ToString());
        if (!string.IsNullOrWhiteSpace(symbol)) return new[] { symbol };

        string restSymbol = NormalizeFuturesTradingSymbol(context.Request.Query["restSymbol"].ToString());
        if (!string.IsNullOrWhiteSpace(restSymbol)) return new[] { restSymbol };

        return new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT" };
    }

    private static int ReadQueryInt(HttpContext context, string name, int fallback, int min, int max)
    {
        if (!int.TryParse(context.Request.Query[name].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) value = fallback;
        return Math.Clamp(value, min, max);
    }

    private static List<object> NormalizeSpotOrders(JsonElement root, bool includeTerminal)
    {
        var rows = new List<object>();
        IEnumerable<JsonElement> elements = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToArray()
            : new[] { root };
        foreach (var item in elements)
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            string status = ReadString(item, "status").ToUpperInvariant();
            if (!includeTerminal && (status is "FILLED" or "CANCELED" or "CANCELLED" or "EXPIRED" or "REJECTED")) continue;
            string symbol = ReadString(item, "symbol").ToUpperInvariant();
            decimal price = ReadDecimal(item, "price");
            decimal qty = ReadDecimal(item, "origQty");
            if (qty <= 0m) qty = ReadDecimal(item, "executedQty");
            decimal executedQty = ReadDecimal(item, "executedQty");
            decimal quoteQty = ReadDecimal(item, "cummulativeQuoteQty");
            if (quoteQty <= 0m) quoteQty = ReadDecimal(item, "cumulativeQuoteQty");
            long time = ReadLong(item, "time");
            if (time <= 0L) time = ReadLong(item, "transactTime");
            long updateTime = ReadLong(item, "updateTime");
            if (updateTime <= 0L) updateTime = time;
            rows.Add(new
            {
                symbol,
                displaySymbol = DisplaySpotSymbol(symbol),
                displayName = DisplaySpotSymbol(symbol),
                orderId = ReadLong(item, "orderId"),
                clientOrderId = ReadString(item, "clientOrderId"),
                tradeSide = ReadString(item, "side"),
                side = ReadString(item, "side"),
                tradeType = ReadString(item, "type"),
                orderType = ReadString(item, "type"),
                type = ReadString(item, "type"),
                price,
                px = price,
                avgPrice = executedQty > 0m && quoteQty > 0m ? quoteQty / executedQty : 0m,
                averagePrice = executedQty > 0m && quoteQty > 0m ? quoteQty / executedQty : 0m,
                qty,
                quantity = qty,
                amount = qty,
                filledQty = executedQty,
                fillQty = executedQty,
                executedQty,
                quoteQty,
                value = quoteQty,
                remainQty = Math.Max(0m, qty - executedQty),
                timeInForce = ReadString(item, "timeInForce"),
                status,
                orderStatus = status,
                createTime = time,
                updateTime,
                time,
                market = "spot",
                raw = item.Clone()
            });
        }
        return rows.OrderByDescending(row => GetObjectLong(row, "updateTime")).ToList();
    }

    private static List<object> NormalizeSpotTrades(JsonElement root)
    {
        var rows = new List<object>();
        if (root.ValueKind != JsonValueKind.Array) return rows;
        foreach (var item in root.EnumerateArray())
        {
            string symbol = ReadString(item, "symbol").ToUpperInvariant();
            decimal price = ReadDecimal(item, "price");
            decimal qty = ReadDecimal(item, "qty");
            decimal quoteQty = ReadDecimal(item, "quoteQty");
            bool isBuyer = ReadBoolFlexible(item, "isBuyer");
            rows.Add(new
            {
                symbol,
                displaySymbol = DisplaySpotSymbol(symbol),
                displayName = DisplaySpotSymbol(symbol),
                tradeId = ReadLong(item, "id"),
                orderId = ReadLong(item, "orderId"),
                tradeSide = isBuyer ? "BUY" : "SELL",
                side = isBuyer ? "BUY" : "SELL",
                price,
                px = price,
                qty,
                quantity = qty,
                amount = qty,
                filledQty = qty,
                fillQty = qty,
                quoteQty,
                value = quoteQty,
                fee = ReadDecimal(item, "commission"),
                feeCcy = ReadString(item, "commissionAsset"),
                time = ReadLong(item, "time"),
                createTime = ReadLong(item, "time"),
                updateTime = ReadLong(item, "time"),
                market = "spot",
                raw = item.Clone()
            });
        }
        return rows.OrderByDescending(row => GetObjectLong(row, "time")).ToList();
    }

    private static List<object> NormalizeFuturesTrades(JsonElement root)
    {
        var rows = new List<object>();
        if (root.ValueKind != JsonValueKind.Array) return rows;
        foreach (var item in root.EnumerateArray())
        {
            string symbol = ReadString(item, "symbol").ToUpperInvariant();
            decimal price = ReadDecimal(item, "price");
            decimal qty = ReadDecimal(item, "qty");
            decimal quoteQty = ReadDecimal(item, "quoteQty");
            bool buyer = ReadBoolFlexible(item, "buyer");
            rows.Add(new
            {
                symbol,
                displaySymbol = DisplayFuturesSymbol(symbol),
                displayName = DisplayFuturesSymbol(symbol),
                tradeId = ReadLong(item, "id"),
                orderId = ReadLong(item, "orderId"),
                tradeSide = buyer ? "BUY" : "SELL",
                side = buyer ? "BUY" : "SELL",
                price,
                px = price,
                qty,
                quantity = qty,
                amount = qty,
                filledQty = qty,
                fillQty = qty,
                quoteQty,
                value = quoteQty,
                fee = ReadDecimal(item, "commission"),
                feeCcy = ReadString(item, "commissionAsset"),
                realizedPnl = ReadDecimal(item, "realizedPnl"),
                role = ReadBoolFlexible(item, "maker") ? "Maker" : "Taker",
                isTaker = ReadBoolFlexible(item, "maker") ? 0 : 1,
                time = ReadLong(item, "time"),
                createTime = ReadLong(item, "time"),
                updateTime = ReadLong(item, "time"),
                market = "futures",
                contractType = "PERPETUAL",
                raw = item.Clone()
            });
        }
        return rows.OrderByDescending(row => GetObjectLong(row, "time")).ToList();
    }

    private static long GetObjectLong(object row, string propertyName)
    {
        var property = row.GetType().GetProperty(propertyName);
        if (property == null) return 0L;
        var value = property.GetValue(row);
        return value is long n ? n : 0L;
    }

    private static List<object> NormalizeFuturesOrders(JsonElement root, bool includeTerminal)
    {
        var rows = new List<object>();
        if (root.ValueKind != JsonValueKind.Array) return rows;
        foreach (var item in root.EnumerateArray())
        {
            string status = ReadString(item, "status").ToUpperInvariant();
            if (!includeTerminal && (status is "FILLED" or "CANCELED" or "CANCELLED" or "EXPIRED" or "REJECTED")) continue;
            string symbol = ReadString(item, "symbol").ToUpperInvariant();
            decimal price = ReadDecimal(item, "price");
            decimal qty = ReadDecimal(item, "origQty");
            decimal executedQty = ReadDecimal(item, "executedQty");
            decimal avgPrice = ReadDecimal(item, "avgPrice");
            if (avgPrice == 0m) avgPrice = ReadDecimal(item, "averagePrice");
            rows.Add(new
            {
                symbol,
                displaySymbol = DisplayFuturesSymbol(symbol),
                displayName = DisplayFuturesSymbol(symbol),
                orderId = ReadLong(item, "orderId"),
                clientOrderId = ReadString(item, "clientOrderId"),
                tradeSide = ReadString(item, "side"),
                side = ReadString(item, "side"),
                tradeType = ReadString(item, "type"),
                orderType = ReadString(item, "type"),
                type = ReadString(item, "type"),
                price,
                px = price,
                avgPrice,
                averagePrice = avgPrice,
                qty,
                quantity = qty,
                amount = qty,
                filledQty = executedQty,
                fillQty = executedQty,
                executedQty,
                remainQty = Math.Max(0m, qty - executedQty),
                reduceOnly = ReadBoolFlexible(item, "reduceOnly"),
                timeInForce = ReadString(item, "timeInForce"),
                status,
                orderStatus = status,
                createTime = ReadLong(item, "time"),
                updateTime = ReadLong(item, "updateTime"),
                time = ReadLong(item, "time"),
                market = "futures",
                contractType = "PERPETUAL",
                raw = item.Clone()
            });
        }
        return rows;
    }

    private static List<object> NormalizeFuturesPositions(JsonElement root)
    {
        var rows = new List<object>();
        if (root.ValueKind != JsonValueKind.Array) return rows;
        foreach (var item in root.EnumerateArray())
        {
            string symbol = ReadString(item, "symbol").ToUpperInvariant();
            decimal qty = ReadDecimal(item, "positionAmt");
            if (qty == 0m) continue;
            decimal entryPrice = ReadDecimal(item, "entryPrice");
            decimal markPrice = ReadDecimal(item, "markPrice");
            decimal upnl = ReadDecimal(item, "unRealizedProfit");
            decimal notional = ReadDecimal(item, "notional");
            rows.Add(new
            {
                symbol,
                displaySymbol = DisplayFuturesSymbol(symbol),
                displayName = DisplayFuturesSymbol(symbol),
                tradeSide = qty < 0m ? "SELL" : "BUY",
                side = qty < 0m ? "SELL" : "BUY",
                positionSide = ReadString(item, "positionSide"),
                qty,
                quantity = qty,
                amount = qty,
                size = qty,
                positionAmt = qty,
                value = notional,
                notional,
                avgPrice = entryPrice,
                entryPrice,
                averagePrice = entryPrice,
                markPrice,
                lastPrice = markPrice,
                upnlByLastPrice = upnl,
                upnl,
                unrealizedPnl = upnl,
                leverage = ReadDecimal(item, "leverage"),
                initMargin = ReadDecimal(item, "initialMargin"),
                maintMargin = ReadDecimal(item, "maintMargin"),
                marginType = ReadString(item, "marginType"),
                updateTime = ReadLong(item, "updateTime"),
                market = "futures",
                contractType = "PERPETUAL",
                raw = item.Clone()
            });
        }
        return rows;
    }

    private static List<object> NormalizeFuturesIncome(JsonElement root, string fallbackIncomeType)
    {
        var rows = new List<object>();
        if (root.ValueKind != JsonValueKind.Array) return rows;
        foreach (var item in root.EnumerateArray())
        {
            string symbol = ReadString(item, "symbol").ToUpperInvariant();
            string incomeType = ReadString(item, "incomeType");
            decimal income = ReadDecimal(item, "income");
            long time = ReadLong(item, "time");
            rows.Add(new
            {
                symbol,
                displaySymbol = string.IsNullOrWhiteSpace(symbol) ? string.Empty : DisplayFuturesSymbol(symbol),
                displayName = string.IsNullOrWhiteSpace(symbol) ? string.Empty : DisplayFuturesSymbol(symbol),
                incomeType = string.IsNullOrWhiteSpace(incomeType) ? fallbackIncomeType : incomeType,
                fundFee = income,
                funding = income,
                realizedPnl = income,
                pnl = income,
                amount = income,
                asset = ReadString(item, "asset"),
                ccy = ReadString(item, "asset"),
                tradeId = ReadLong(item, "tranId"),
                tranId = ReadLong(item, "tranId"),
                info = ReadString(item, "info"),
                time,
                ctime = time,
                createTime = time,
                updateTime = time,
                market = "futures",
                raw = item.Clone()
            });
        }
        return rows.OrderByDescending(row => GetObjectLong(row, "time")).ToList();
    }

    private static List<object> NormalizeFuturesAssets(JsonElement root)
    {
        var rows = new List<object>();
        JsonElement assets;
        if (root.ValueKind == JsonValueKind.Array)
        {
            assets = root;
        }
        else if (!root.TryGetProperty("assets", out assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }
        foreach (var item in assets.EnumerateArray())
        {
            string asset = ReadString(item, "asset").ToUpperInvariant();
            decimal walletBalance = ReadAssetWalletBalance(item);
            decimal unrealized = ReadDecimal(item, "unrealizedProfit");
            if (unrealized == 0m) unrealized = ReadDecimal(item, "umUnrealizedPNL") + ReadDecimal(item, "cmUnrealizedPNL");
            decimal marginBalance = ReadDecimal(item, "marginBalance");
            if (marginBalance == 0m) marginBalance = walletBalance + unrealized;
            decimal available = ReadDecimal(item, "availableBalance");
            if (available == 0m) available = ReadDecimal(item, "crossMarginFree");
            if (walletBalance == 0m && marginBalance == 0m && available == 0m && unrealized == 0m) continue;
            rows.Add(new
            {
                asset,
                ccy = asset,
                coin = asset,
                equity = marginBalance != 0m ? marginBalance : walletBalance + unrealized,
                balance = walletBalance,
                walletBalance,
                marginBalance,
                available,
                availableBalance = available,
                unrealizedPnl = unrealized,
                upl = unrealized,
                borrowed = 0m,
                market = "futures",
                raw = item.Clone()
            });
        }
        return rows;
    }

    private static decimal SumPortfolioMarginWalletEquity(JsonElement root)
    {
        JsonElement assets;
        if (root.ValueKind == JsonValueKind.Array)
        {
            assets = root;
        }
        else if (!root.TryGetProperty("assets", out assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return 0m;
        }
        decimal total = 0m;
        foreach (var item in assets.EnumerateArray())
        {
            total += ReadAssetWalletBalance(item);
        }
        return total;
    }

    private static decimal ReadAssetWalletBalance(JsonElement item)
    {
        decimal wallet = ReadDecimal(item, "totalWalletBalance");
        if (wallet != 0m) return wallet;
        wallet = ReadDecimal(item, "walletBalance");
        if (wallet != 0m) return wallet;
        decimal um = ReadDecimal(item, "umWalletBalance");
        decimal cm = ReadDecimal(item, "cmWalletBalance");
        if (um != 0m || cm != 0m) return um + cm;
        decimal crossFree = ReadDecimal(item, "crossMarginFree");
        decimal crossLocked = ReadDecimal(item, "crossMarginLocked");
        return crossFree + crossLocked;
    }

    private static JsonElement FindSymbolConfigRow(JsonElement root, string symbol)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (ReadString(item, "symbol").Equals(symbol, StringComparison.OrdinalIgnoreCase)) return item;
            }
            return root.GetArrayLength() > 0 ? root[0] : default;
        }
        return root;
    }

    private static long ReadLong(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return 0L;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return 0L;
    }

    private static bool ReadBoolFlexible(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return false;
        if (el.ValueKind == JsonValueKind.True) return true;
        if (el.ValueKind == JsonValueKind.False) return false;
        if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var parsed)) return parsed;
        return false;
    }

    public static async Task<(bool ok, string message, int code, object metrics, List<object> balances, object? futuresAccount, string accountType, bool canTrade, bool canWithdraw, bool canDeposit, int nonZeroBalances)> BuildAccountSummaryAsync(BinanceAccountRecord account, CancellationToken ct)
    {
        var spot = await FetchSpotAccountDocumentAsync(account.apiKey, account.apiSecret, ct);
        using var doc = spot.document;
        var balances = spot.ok && doc != null ? await BuildSpotBalancesAsync(doc.RootElement, ct) : new List<object>();
        var spotMetrics = BuildSpotMetrics(balances);
        var futures = await FetchFuturesAccountDocumentAsync(account.apiKey, account.apiSecret, ct);
        object? futuresAccount = null;
        object metrics = spotMetrics;
        var portfolioMargin = await FetchPortfolioMarginAccountDocumentAsync(account.apiKey, account.apiSecret, ct);
        if (futures.ok && futures.document != null)
        {
            using var futuresDoc = futures.document;
            metrics = BuildFuturesMetrics(futuresDoc.RootElement, spotMetrics);
            futuresAccount = new
            {
                canTrade = ReadBool(futuresDoc.RootElement, "canTrade"),
                totalWalletBalance = ReadDecimal(futuresDoc.RootElement, "totalWalletBalance"),
                totalMarginBalance = ReadDecimal(futuresDoc.RootElement, "totalMarginBalance"),
                availableBalance = ReadDecimal(futuresDoc.RootElement, "availableBalance"),
                totalUnrealizedProfit = ReadDecimal(futuresDoc.RootElement, "totalUnrealizedProfit")
            };
        }
        if (portfolioMargin.ok && portfolioMargin.document != null)
        {
            using var pmDoc = portfolioMargin.document;
            decimal pmEquity = ReadDecimal(pmDoc.RootElement, "accountEquity");
            if (pmEquity == 0m) pmEquity = ReadDecimal(pmDoc.RootElement, "actualEquity");
            decimal pmAvailable = ReadDecimal(pmDoc.RootElement, "totalAvailableBalance");
            if (pmAvailable == 0m) pmAvailable = ReadDecimal(pmDoc.RootElement, "availableBalance");
            if (pmEquity > 0m || pmAvailable > 0m)
            {
                var pmBalance = await FetchPortfolioMarginBalanceDocumentAsync(account.apiKey, account.apiSecret, ct);
                if (pmBalance.ok && pmBalance.document != null)
                {
                    using var pmBalanceDoc = pmBalance.document;
                    metrics = BuildPortfolioMarginMetrics(pmDoc.RootElement, pmBalanceDoc.RootElement);
                }
                else
                {
                    metrics = BuildPortfolioMarginMetrics(pmDoc.RootElement);
                }
                futuresAccount = new
                {
                    accountMode = "Portfolio Margin",
                    accountStatus = ReadString(pmDoc.RootElement, "accountStatus"),
                    accountEquity = pmEquity,
                    actualEquity = ReadDecimal(pmDoc.RootElement, "actualEquity"),
                    totalAvailableBalance = pmAvailable,
                    accountInitialMargin = ReadDecimal(pmDoc.RootElement, "accountInitialMargin"),
                    accountMaintMargin = ReadDecimal(pmDoc.RootElement, "accountMaintMargin"),
                    uniMMR = ReadDecimal(pmDoc.RootElement, "uniMMR")
                };
            }
        }
        int nonZeroBalances = spot.ok && doc != null ? CountNonZeroBalances(doc.RootElement) : 0;
        if (ReadMetricDecimal(metrics, "totalEquity") == 0m
            && ReadMetricDecimal(metrics, "availableEquity") == 0m
            && nonZeroBalances == 0
            && !futures.ok
            && !portfolioMargin.ok)
        {
            string message = "Binance Spot/Portfolio/Futures account is unavailable for this API key or server IP. "
                + "Enable Portfolio Margin/Futures API permissions and whitelist server IP 49.12.121.179. "
                + "Spot: " + spot.message + " (" + spot.code.ToString(CultureInfo.InvariantCulture) + "); "
                + "Futures: " + futures.message + " (" + futures.code.ToString(CultureInfo.InvariantCulture) + "); "
                + "Portfolio: " + portfolioMargin.message + " (" + portfolioMargin.code.ToString(CultureInfo.InvariantCulture) + ").";
            int code = portfolioMargin.code != 0 ? portfolioMargin.code : (futures.code != 0 ? futures.code : spot.code);
            return (false, message, code, new { }, new List<object>(), null, string.Empty, false, false, false, 0);
        }
        string accountType = spot.ok && doc != null ? ReadString(doc.RootElement, "accountType") : string.Empty;
        bool canTrade = (spot.ok && doc != null && ReadBool(doc.RootElement, "canTrade")) || futures.ok || portfolioMargin.ok;
        bool canWithdraw = spot.ok && doc != null && ReadBool(doc.RootElement, "canWithdraw");
        bool canDeposit = spot.ok && doc != null && ReadBool(doc.RootElement, "canDeposit");
        return (true, "OK", 0, metrics, balances, futuresAccount, accountType, canTrade, canWithdraw, canDeposit, nonZeroBalances);
    }

    public static async Task StoreSummarySnapshotAsync(BinanceRepository repo, BinanceAccountRecord account, (bool ok, string message, int code, object metrics, List<object> balances, object? futuresAccount, string accountType, bool canTrade, bool canWithdraw, bool canDeposit, int nonZeroBalances) summary, bool storeMinuteEquity, CancellationToken ct)
    {
        if (!summary.ok) return;
        decimal totalEquity = ReadMetricDecimal(summary.metrics, "totalEquity");
        decimal availableEquity = ReadMetricDecimal(summary.metrics, "availableEquity");
        decimal unrealizedPnl = ReadMetricDecimal(summary.metrics, "unrealizedPnl");
        decimal initialMargin = ReadMetricDecimal(summary.metrics, "initialMargin");
        decimal maintenanceMargin = ReadMetricDecimal(summary.metrics, "maintenanceMargin");
        decimal? marginRatio = (initialMargin == 0m && maintenanceMargin == 0m) ? null : (totalEquity != 0m ? maintenanceMargin / Math.Abs(totalEquity) * 100m : 0m);
        if (totalEquity == 0m && availableEquity == 0m) return;
        await repo.UpsertEquityAsync(account.id, account.name, DateTime.UtcNow, "USD", totalEquity, availableEquity, unrealizedPnl, marginRatio, storeMinuteEquity, ct);
    }

    private static bool IsPortfolioMarginSummary((bool ok, string message, int code, object metrics, List<object> balances, object? futuresAccount, string accountType, bool canTrade, bool canWithdraw, bool canDeposit, int nonZeroBalances) summary)
    {
        if (summary.futuresAccount == null) return false;
        var json = JsonSerializer.SerializeToElement(summary.futuresAccount);
        string accountMode = ReadString(json, "accountMode");
        return accountMode.Equals("Portfolio Margin", StringComparison.OrdinalIgnoreCase);
    }

    private static object ToEquityPointPayload(BinanceEquityPoint p) => new
    {
        tsUtc = p.tsUtc,
        dt = p.tsUtc,
        value = p.totalEquity,
        v = p.totalEquity,
        availableEquity = p.availableEquity,
        unrealizedPnl = p.unrealizedPnl,
        marginRatio = p.marginRatio,
        equityCurrency = p.equityCurrency
    };

    private static List<object> BuildDrawdownPoints(List<BinanceEquityPoint> points)
    {
        var result = new List<object>();
        decimal hwm = 0m;
        foreach (var p in points.OrderBy(x => x.tsUtc))
        {
            if (p.totalEquity > hwm) hwm = p.totalEquity;
            decimal dd = hwm > 0m ? (hwm - p.totalEquity) / hwm * 100m : 0m;
            result.Add(new { tsUtc = p.tsUtc, dt = p.tsUtc, value = dd, v = dd });
        }
        return result;
    }

    private static async Task<List<object>> BuildSpotBalancesAsync(JsonElement root, CancellationToken ct)
    {
        var rows = new List<object>();
        if (!root.TryGetProperty("balances", out var balances) || balances.ValueKind != JsonValueKind.Array) return rows;
        foreach (var item in balances.EnumerateArray())
        {
            string asset = ReadString(item, "asset").ToUpperInvariant();
            decimal free = ReadDecimal(item, "free");
            decimal locked = ReadDecimal(item, "locked");
            decimal balance = free + locked;
            if (string.IsNullOrWhiteSpace(asset) || balance == 0m) continue;
            decimal? usdPrice = await TryGetUsdPriceAsync(asset, ct);
            decimal? equityUsd = usdPrice.HasValue ? balance * usdPrice.Value : null;
            decimal? availableUsd = usdPrice.HasValue ? free * usdPrice.Value : null;
            rows.Add(new
            {
                ccy = asset,
                asset,
                free,
                locked,
                available = free,
                frozen = locked,
                balance,
                equity = balance,
                equityUsd,
                availableUsd,
                source = "binance:/api/v3/account"
            });
        }
        return rows;
    }

    private static async Task<decimal?> TryGetUsdPriceAsync(string asset, CancellationToken ct)
    {
        asset = (asset ?? string.Empty).Trim().ToUpperInvariant();
        if (asset is "USDT" or "USDC" or "BUSD" or "FDUSD" or "USD") return 1m;
        if (string.IsNullOrWhiteSpace(asset)) return null;
        foreach (string quote in new[] { "USDT", "USDC" })
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, BinanceBaseUrl + "/api/v3/ticker/price?symbol=" + Uri.EscapeDataString(asset + quote));
                using var res = await Http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) continue;
                string body = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                decimal price = ReadDecimal(doc.RootElement, "price");
                if (price > 0m) return price;
            }
            catch
            {
            }
        }
        return null;
    }

    private static object BuildSpotMetrics(List<object> balances)
    {
        decimal totalEquity = 0m;
        decimal availableEquity = 0m;
        foreach (var row in balances)
        {
            var json = JsonSerializer.SerializeToElement(row);
            totalEquity += ReadDecimal(json, "equityUsd");
            availableEquity += ReadDecimal(json, "availableUsd");
        }
        return new
        {
            totalEquity,
            availableEquity,
            initialMargin = 0m,
            maintenanceMargin = 0m,
            equityCurrency = "USD",
            source = "binance:/api/v3/account"
        };
    }

    private static object BuildFuturesMetrics(JsonElement root, object spotMetricsObj)
    {
        var spotJson = JsonSerializer.SerializeToElement(spotMetricsObj);
        decimal spotTotal = ReadDecimal(spotJson, "totalEquity");
        decimal spotAvailable = ReadDecimal(spotJson, "availableEquity");
        decimal futuresTotal = ReadDecimal(root, "totalMarginBalance");
        if (futuresTotal == 0m) futuresTotal = ReadDecimal(root, "totalWalletBalance") + ReadDecimal(root, "totalUnrealizedProfit");
        decimal futuresAvailable = ReadDecimal(root, "availableBalance");
        decimal initialMargin = ReadDecimal(root, "totalInitialMargin");
        decimal maintenanceMargin = ReadDecimal(root, "totalMaintMargin");
        decimal totalEquity = spotTotal + futuresTotal;
        decimal availableEquity = spotAvailable + futuresAvailable;
        return new
        {
            totalEquity,
            availableEquity,
            initialMargin,
            maintenanceMargin,
            equityCurrency = "USD",
            futuresTotalEquity = futuresTotal,
            futuresAvailableEquity = futuresAvailable,
            spotTotalEquity = spotTotal,
            spotAvailableEquity = spotAvailable,
            source = "binance:/api/v3/account+fapi:/fapi/v3/account"
        };
    }

    private static object BuildPortfolioMarginMetrics(JsonElement root)
        => BuildPortfolioMarginMetrics(root, default, false);

    private static object BuildPortfolioMarginMetrics(JsonElement root, JsonElement balanceRoot)
        => BuildPortfolioMarginMetrics(root, balanceRoot, true);

    private static object BuildPortfolioMarginMetrics(JsonElement root, JsonElement balanceRoot, bool hasBalanceRoot)
    {
        decimal walletEquity = hasBalanceRoot ? SumPortfolioMarginWalletEquity(balanceRoot) : SumPortfolioMarginWalletEquity(root);
        decimal actualEquity = ReadDecimal(root, "actualEquity");
        decimal totalEquity = walletEquity != 0m ? walletEquity : actualEquity;
        if (totalEquity == 0m) totalEquity = ReadDecimal(root, "accountEquity");
        decimal availableEquity = ReadDecimal(root, "totalAvailableBalance");
        if (availableEquity == 0m) availableEquity = ReadDecimal(root, "availableBalance");
        decimal initialMargin = ReadDecimal(root, "accountInitialMargin");
        decimal maintenanceMargin = ReadDecimal(root, "accountMaintMargin");
        decimal unrealizedPnl = ReadDecimal(root, "totalUnrealizedProfit");
        if (unrealizedPnl == 0m) unrealizedPnl = ReadDecimal(root, "unrealizedProfit");
        return new
        {
            totalEquity,
            availableEquity,
            initialMargin,
            maintenanceMargin,
            unrealizedPnl,
            equityCurrency = "USD",
            futuresTotalEquity = totalEquity,
            futuresAvailableEquity = availableEquity,
            spotTotalEquity = 0m,
            spotAvailableEquity = 0m,
            source = "papi:/papi/v1/account"
        };
    }

    private static async Task<BinanceAccountRecord?> ResolveAccountAsync(HttpContext context, BinanceRepository repo)
    {
        if (context.Request.Query.TryGetValue("accountId", out var raw) && int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
        {
            return await repo.GetAccountByIdAsync(id, includeSecrets: true, context.RequestAborted);
        }
        return (await repo.GetAccountsAsync(includeSecrets: true, context.RequestAborted)).FirstOrDefault(a => a.isActive);
    }

    private static string ReadString(JsonElement root, string name) => root.TryGetProperty(name, out var el) ? (el.GetString() ?? string.Empty) : string.Empty;

    private static bool ReadBool(JsonElement root, string name) => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    private static int CountNonZeroBalances(JsonElement root)
    {
        if (!root.TryGetProperty("balances", out var balances) || balances.ValueKind != JsonValueKind.Array) return 0;
        int count = 0;
        foreach (var item in balances.EnumerateArray())
        {
            decimal free = ReadDecimal(item, "free");
            decimal locked = ReadDecimal(item, "locked");
            if (free != 0m || locked != 0m) count++;
        }
        return count;
    }

    private static decimal ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return 0m;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var n)) return n;
        if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return 0m;
    }

    public static decimal ReadMetricDecimal(object metrics, string name)
    {
        var json = JsonSerializer.SerializeToElement(metrics);
        return ReadDecimal(json, name);
    }

    private static string FuturesLeverageCacheKey(int accountId, string symbol) => accountId.ToString(CultureInfo.InvariantCulture) + ":" + NormalizeFuturesTradingSymbol(symbol);

    private static object ToPublicAccount(BinanceAccountRecord account) => new
    {
        id = account.id,
        name = account.name,
        apiKeyMasked = BinanceRepository.MaskApiKey(account.apiKey),
        isActive = account.isActive,
        isDemo = false,
        storeMinuteEquity = account.storeMinuteEquity,
        equityCurrency = BinanceRepository.NormalizeEquityCurrency(account.equityCurrency),
        createdAtUtc = account.createdAtUtc,
        updatedAtUtc = account.updatedAtUtc
    };

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

public sealed class BinanceAccountRequest
{
    public string? name { get; set; }
    public string? apiKey { get; set; }
    public string? apiSecret { get; set; }
    public bool isActive { get; set; }
    public bool setActive { get; set; }
    public bool storeMinuteEquity { get; set; }
    public string? equityCurrency { get; set; } = "USD";
}
