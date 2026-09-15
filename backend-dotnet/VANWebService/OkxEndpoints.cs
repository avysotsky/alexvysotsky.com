using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using VANWebService.Auth;
using VANWebService.Models;
using VANWebService.Services;

namespace VANWebService;

public static class OkxEndpoints
{
    public static void MapOkxEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/okx/accounts", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var accounts = await repo.GetAccountsAsync(includeSecrets: false, context.RequestAborted);
            return Results.Json(new
            {
                items = accounts.Select(a => new
                {
                    id = a.id,
                    name = a.name,
                    apiKeyMasked = OkxRepository.MaskApiKey(a.apiKey),
                    isActive = a.isActive,
                    isDemo = a.isDemo,
                    storeMinuteEquity = a.storeMinuteEquity,
                    equityCurrency = OkxRepository.NormalizeEquityCurrency(a.equityCurrency),
                    createdAtUtc = a.createdAtUtc,
                    updatedAtUtc = a.updatedAtUtc
                }).ToList()
            });
        });

        app.MapPost("/api/admin/okx/accounts", async (HttpContext context, OkxRepository repo, OkxCreateAccountRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            if (string.IsNullOrWhiteSpace(dto.name) || string.IsNullOrWhiteSpace(dto.apiKey) || string.IsNullOrWhiteSpace(dto.apiSecret) || string.IsNullOrWhiteSpace(dto.passphrase))
            {
                return Results.BadRequest(new { message = "name, apiKey, apiSecret, passphrase are required" });
            }
            int id = await repo.CreateAccountAsync(
                dto.name,
                dto.apiKey,
                dto.apiSecret,
                dto.passphrase,
                dto.isDemo,
                dto.setActive,
                dto.storeMinuteEquity,
                dto.equityCurrency ?? "USD",
                context.RequestAborted);
            return Results.Json(new { ok = true, id });
        });

        app.MapPut("/api/admin/okx/accounts/{id:int}", async (HttpContext context, OkxRepository repo, int id, OkxUpdateAccountSettingsRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            bool ok = await repo.UpdateAccountSettingsAsync(id, dto.storeMinuteEquity, dto.equityCurrency ?? "USD", context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "Account not found" });
        });

        app.MapPut("/api/admin/okx/accounts/{id:int}/activate", async (HttpContext context, OkxRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            bool ok = await repo.SetActiveAccountAsync(id, context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "Account not found" });
        });

        app.MapDelete("/api/admin/okx/accounts/{id:int}", async (HttpContext context, OkxRepository repo, int id) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            bool ok = await repo.DeleteAccountAsync(id, context.RequestAborted);
            return ok ? Results.Json(new { ok = true }) : Results.NotFound(new { message = "Account not found" });
        });

        app.MapGet("/api/admin/okx/summary", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            var client = new OkxApiClient(account);
            var snapshot = await client.GetLiveSnapshotAsync(context.RequestAborted);
            return Results.Json(new
            {
                account = ToPublicAccount(account),
                totalEquityUsdt = snapshot.totalEquityUsdt,
                availableEquityUsdt = snapshot.availableEquityUsdt,
                unrealizedPnlUsdt = snapshot.unrealizedPnlUsdt,
                marginRatio = snapshot.marginRatio,
                balanceDetails = snapshot.balanceDetails,
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapGet("/api/admin/okx/funding", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            var client = new OkxApiClient(account);
            using var doc = await client.GetFundingBalancesAsync(context.RequestAborted);
            var items = ParseFundingRows(doc);
            return Results.Json(new
            {
                account = ToPublicAccount(account),
                items,
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapGet("/api/admin/okx/funding-history", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            var client = new OkxApiClient(account);
            using var doc = await client.GetFundingBillsAsync(context.RequestAborted);
            return Results.Json(new
            {
                account = ToPublicAccount(account),
                items = ParseFundingHistoryRows(doc),
                fetchedAtUtc = DateTime.UtcNow
            });
        });

        app.MapGet("/api/admin/okx/futures/funding/minute", async (HttpContext context, OkxRepository repo) =>
        {
            string instId = OkxRepository.NormalizeFundingInstId(context.Request.Query["instId"].ToString());
            int limit = int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 5000;
            var rows = await repo.GetFundingMinuteAsync(instId, Math.Clamp(limit, 1, 10000), context.RequestAborted);
            var items = rows.Select(ToOkxFundingMinutePayloadRow).ToList();
            return Results.Json(new { source = "van_okx_futures_funding_minute", instId, order = "oldest-to-newest", rows = items, data = new { list = items }, fetchedAtUtc = DateTime.UtcNow });
        });

        app.MapGet("/api/admin/okx/futures/funding/history", async (HttpContext context) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            string instId = OkxRepository.NormalizeFundingInstId(context.Request.Query["instId"].ToString());
            int limit = int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit) ? parsedLimit : 200;
            string query = "/api/v5/public/funding-rate-history?instId=" + Uri.EscapeDataString(instId)
                + "&limit=" + Math.Clamp(limit, 1, 400).ToString(CultureInfo.InvariantCulture);
            try
            {
                using var doc = await OkxApiClient.GetPublicAsync(query, context.RequestAborted);
                var items = ParseOkxFundingHistoryRows(doc.RootElement, instId).ToList();
                return Results.Json(new { source = "okx:/api/v5/public/funding-rate-history", instId, order = "oldest-to-newest", rows = items, data = new { list = items }, fetchedAtUtc = DateTime.UtcNow });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { ok = false, message = ex.Message });
            }
        });

        app.MapGet("/api/admin/okx/positions", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            var client = new OkxApiClient(account);
            using var doc = await client.GetPositionsAsync(context.RequestAborted);
            var items = new List<object>();
            foreach (var row in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                items.Add(new
                {
                    instId = OkxApiClient.ReadString(row, "instId"),
                    instType = OkxApiClient.ReadString(row, "instType"),
                    posSide = OkxApiClient.ReadString(row, "posSide"),
                    side = OkxApiClient.ReadString(row, "pos"),
                    leverage = OkxApiClient.ReadString(row, "lever"),
                    size = OkxApiClient.ReadString(row, "pos"),
                    avgPrice = OkxApiClient.ReadString(row, "avgPx"),
                    markPrice = OkxApiClient.ReadString(row, "markPx"),
                    unrealizedPnl = OkxApiClient.ReadString(row, "upl"),
                    margin = OkxApiClient.ReadString(row, "margin"),
                    marginRatio = OkxApiClient.ReadString(row, "mgnRatio")
                });
            }
            return Results.Json(new { account = ToPublicAccount(account), items });
        });

        app.MapGet("/api/admin/okx/orders", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            var client = new OkxApiClient(account);
            using var doc = await client.GetOpenOrdersAsync(context.RequestAborted);
            var items = new List<object>();
            foreach (var row in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                items.Add(new
                {
                    ordId = OkxApiClient.ReadString(row, "ordId"),
                    clOrdId = OkxApiClient.ReadString(row, "clOrdId"),
                    instId = OkxApiClient.ReadString(row, "instId"),
                    side = OkxApiClient.ReadString(row, "side"),
                    posSide = OkxApiClient.ReadString(row, "posSide"),
                    ordType = OkxApiClient.ReadString(row, "ordType"),
                    state = OkxApiClient.ReadString(row, "state"),
                    price = OkxApiClient.ReadString(row, "px"),
                    size = OkxApiClient.ReadString(row, "sz"),
                    filled = OkxApiClient.ReadString(row, "accFillSz"),
                    createdAt = OkxApiClient.ReadString(row, "cTime")
                });
            }
            return Results.Json(new { account = ToPublicAccount(account), items });
        });

        app.MapGet("/api/admin/okx/statistics", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            int days = 7;
            if (context.Request.Query.TryGetValue("days", out var daysRaw) && int.TryParse(daysRaw.FirstOrDefault(), out var parsedDays))
            {
                days = Math.Clamp(parsedDays, 1, 90);
            }
            var points = await repo.GetSnapshotsAsync(account.id, days, context.RequestAborted);
            var latest = points.LastOrDefault();
            var first24h = points.LastOrDefault(p => p.tsUtc <= DateTime.UtcNow.AddHours(-24)) ?? points.FirstOrDefault();
            var first7d = points.FirstOrDefault();
            return Results.Json(new
            {
                account = ToPublicAccount(account),
                latest,
                delta24h = BuildUsdtDelta(first24h, latest),
                deltaPeriod = BuildUsdtDelta(first7d, latest),
                points
            });
        });

        app.MapGet("/api/admin/okx/equity", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            var daily = await repo.GetDailyEquityAsync(account.id, 3650, context.RequestAborted);
            var minute = account.storeMinuteEquity
                ? await repo.GetMinuteEquityAsync(account.id, 24, context.RequestAborted)
                : new List<OkxEquityPoint>();
            var dailyDrawdownMinute = account.storeMinuteEquity
                ? await repo.GetMinuteEquityForDailyDrawdownAsync(account.id, 3650, context.RequestAborted)
                : new List<OkxEquityPoint>();
            return Results.Json(BuildEquityPayload(account, daily, minute, dailyDrawdownMinute));
        });

        app.MapGet("/api/admin/okx/pnl", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            var daily = await repo.GetDailyEquityAsync(account.id, 3650, context.RequestAborted);
            var minute = account.storeMinuteEquity
                ? await repo.GetMinuteEquityAsync(account.id, 24, context.RequestAborted)
                : new List<OkxEquityPoint>();
            var dailyDrawdownMinute = account.storeMinuteEquity
                ? await repo.GetMinuteEquityForDailyDrawdownAsync(account.id, 3650, context.RequestAborted)
                : new List<OkxEquityPoint>();
            return Results.Json(BuildEquityPayload(account, daily, minute, dailyDrawdownMinute));
        });

        app.MapGet("/api/admin/okx/state", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            var client = new OkxApiClient(account);
            var snapshotTask = client.GetLiveSnapshotAsync(context.RequestAborted);
            var fundingTask = client.GetFundingBalancesAsync(context.RequestAborted);
            var fundingHistoryTask = client.GetFundingBillsAsync(context.RequestAborted);
            var positionsTask = client.GetPositionsAsync(context.RequestAborted);
            var ordersTask = client.GetOpenOrdersAsync(context.RequestAborted);
            var spotOrderHistoryTask = client.GetOrderHistoryAsync("SPOT", context.RequestAborted);
            var spotFillsHistoryTask = client.GetFillsHistoryAsync("SPOT", context.RequestAborted);
            var accountConfigTask = client.GetAccountConfigAsync(context.RequestAborted);
            var statsTask = repo.GetSnapshotsAsync(account.id, 7, context.RequestAborted);
            await Task.WhenAll(snapshotTask, fundingTask, fundingHistoryTask, positionsTask, ordersTask, spotOrderHistoryTask, spotFillsHistoryTask, accountConfigTask, statsTask);
            var snapshot = snapshotTask.Result;
            using var fundingDoc = fundingTask.Result;
            using var fundingHistoryDoc = fundingHistoryTask.Result;
            using var positionsDoc = positionsTask.Result;
            using var ordersDoc = ordersTask.Result;
            using var spotOrderHistoryDoc = spotOrderHistoryTask.Result;
            using var spotFillsHistoryDoc = spotFillsHistoryTask.Result;
            using var accountConfigDoc = accountConfigTask.Result;
            var stats = statsTask.Result;
            var fundingItems = ParseFundingRows(fundingDoc);
            var fundingHistoryItems = ParseFundingHistoryRows(fundingHistoryDoc);
            var positions = positionsDoc.RootElement.GetProperty("data").EnumerateArray().Select(row => new
            {
                instId = OkxApiClient.ReadString(row, "instId"),
                instType = OkxApiClient.ReadString(row, "instType"),
                posSide = OkxApiClient.ReadString(row, "posSide"),
                size = OkxApiClient.ReadString(row, "pos"),
                avgPrice = OkxApiClient.ReadString(row, "avgPx"),
                markPrice = OkxApiClient.ReadString(row, "markPx"),
                unrealizedPnl = OkxApiClient.ReadString(row, "upl"),
                marginRatio = OkxApiClient.ReadString(row, "mgnRatio"),
                mgnMode = OkxApiClient.ReadString(row, "mgnMode")
            }).ToList();
            var orders = ParseOrderRows(ordersDoc);
            var spotOrderHistory = ParseOrderRows(spotOrderHistoryDoc);
            var spotFillsHistory = ParseFillRows(spotFillsHistoryDoc);
            string acctLv = "";
            string posMode = "";
            string feeType = "";
            if (accountConfigDoc.RootElement.TryGetProperty("data", out var accountConfigData) && accountConfigData.ValueKind == JsonValueKind.Array && accountConfigData.GetArrayLength() > 0)
            {
                var accountConfigRow = accountConfigData[0];
                acctLv = OkxApiClient.ReadString(accountConfigRow, "acctLv");
                posMode = OkxApiClient.ReadString(accountConfigRow, "posMode");
                feeType = OkxApiClient.ReadString(accountConfigRow, "feeType");
            }
            var latest = stats.LastOrDefault();
            var first24h = stats.LastOrDefault(p => p.tsUtc <= DateTime.UtcNow.AddHours(-24)) ?? stats.FirstOrDefault();
            return Results.Json(new
            {
                account = ToPublicAccount(account),
                summary = new
                {
                    totalEquityUsdt = snapshot.totalEquityUsdt,
                    availableEquityUsdt = snapshot.availableEquityUsdt,
                    unrealizedPnlUsdt = snapshot.unrealizedPnlUsdt,
                    marginRatio = snapshot.marginRatio,
                    balanceDetails = snapshot.balanceDetails
                },
                funding = new
                {
                    items = fundingItems,
                    totalAssets = fundingItems.Count,
                    history = fundingHistoryItems
                },
                positions,
                orders,
                orderHistory = spotOrderHistory,
                fillsHistory = spotFillsHistory,
                accountConfig = new
                {
                    acctLv,
                    posMode,
                    feeType
                },
                statistics = new
                {
                    latest,
                    delta24h = BuildUsdtDelta(first24h, latest),
                    points = stats
                },
                fetchedAtUtc = DateTime.UtcNow
            });
        });


        app.MapPost("/api/admin/okx/account-mode", async (HttpContext context, OkxRepository repo, OkxSetAccountModeRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            try
            {
                await repo.EnsureSchemaAsync(context.RequestAborted);
                var account = await ResolveAccountAsync(context, repo);
                if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
                string mode = (dto.mode ?? "").Trim().ToLowerInvariant();
                string advancedMode = (dto.advancedMode ?? "").Trim().ToLowerInvariant();
                string acctLv = mode switch
                {
                    "spot" => "1",
                    "futures" => "2",
                    "advanced" when advancedMode == "portfolio" => "4",
                    "advanced" => "3",
                    _ => ""
                };
                if (string.IsNullOrWhiteSpace(acctLv)) return Results.BadRequest(new { message = "mode must be spot, futures, or advanced" });
                var client = new OkxApiClient(account);
                using var setDoc = await client.SetAccountLevelAsync(new { acctLv }, context.RequestAborted);
                using var configDoc = await client.GetAccountConfigAsync(context.RequestAborted);
                string confirmedAcctLv = "";
                string confirmedPosMode = "";
                string confirmedFeeType = "";
                if (configDoc.RootElement.TryGetProperty("data", out var configData) && configData.ValueKind == JsonValueKind.Array && configData.GetArrayLength() > 0)
                {
                    var configRow = configData[0];
                    confirmedAcctLv = OkxApiClient.ReadString(configRow, "acctLv");
                    confirmedPosMode = OkxApiClient.ReadString(configRow, "posMode");
                    confirmedFeeType = OkxApiClient.ReadString(configRow, "feeType");
                }
                if (!string.Equals(confirmedAcctLv, acctLv, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(new
                    {
                        message = $"OKX accepted account mode switch, but reread returned acctLv={confirmedAcctLv} instead of {acctLv}",
                        code = "ACCOUNT_MODE_REREAD_MISMATCH",
                        requestedAcctLv = acctLv,
                        accountConfig = new { acctLv = confirmedAcctLv, posMode = confirmedPosMode, feeType = confirmedFeeType },
                        result = JsonSerializer.Deserialize<object>(setDoc.RootElement.GetRawText())
                    }, statusCode: StatusCodes.Status502BadGateway);
                }
                return Results.Json(new
                {
                    ok = true,
                    requestedAcctLv = acctLv,
                    accountConfig = new { acctLv = confirmedAcctLv, posMode = confirmedPosMode, feeType = confirmedFeeType },
                    result = JsonSerializer.Deserialize<object>(setDoc.RootElement.GetRawText())
                });
            }
            catch (Exception ex)
            {
                return OkxErrorResult("Failed to switch OKX account mode", ex);
            }
        });

        app.MapPost("/api/admin/okx/fee-type", async (HttpContext context, OkxRepository repo, OkxSetFeeTypeRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            try
            {
                await repo.EnsureSchemaAsync(context.RequestAborted);
                var account = await ResolveAccountAsync(context, repo);
                if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
                string feeType = (dto.feeType ?? "").Trim();
                if (feeType != "0" && feeType != "1") return Results.BadRequest(new { message = "feeType must be 0 or 1" });
                var client = new OkxApiClient(account);
                using var setDoc = await client.SetFeeTypeAsync(new { feeType }, context.RequestAborted);
                using var configDoc = await client.GetAccountConfigAsync(context.RequestAborted);
                string confirmedAcctLv = "";
                string confirmedPosMode = "";
                string confirmedFeeType = "";
                if (configDoc.RootElement.TryGetProperty("data", out var configData) && configData.ValueKind == JsonValueKind.Array && configData.GetArrayLength() > 0)
                {
                    var configRow = configData[0];
                    confirmedAcctLv = OkxApiClient.ReadString(configRow, "acctLv");
                    confirmedPosMode = OkxApiClient.ReadString(configRow, "posMode");
                    confirmedFeeType = OkxApiClient.ReadString(configRow, "feeType");
                }
                if (!string.Equals(confirmedFeeType, feeType, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(new
                    {
                        message = $"OKX accepted fee type switch, but reread returned feeType={confirmedFeeType} instead of {feeType}",
                        code = "FEE_TYPE_REREAD_MISMATCH",
                        requestedFeeType = feeType,
                        accountConfig = new { acctLv = confirmedAcctLv, posMode = confirmedPosMode, feeType = confirmedFeeType },
                        result = JsonSerializer.Deserialize<object>(setDoc.RootElement.GetRawText())
                    }, statusCode: StatusCodes.Status502BadGateway);
                }
                return Results.Json(new
                {
                    ok = true,
                    requestedFeeType = feeType,
                    accountConfig = new { acctLv = confirmedAcctLv, posMode = confirmedPosMode, feeType = confirmedFeeType },
                    result = JsonSerializer.Deserialize<object>(setDoc.RootElement.GetRawText())
                });
            }
            catch (Exception ex)
            {
                return OkxErrorResult("Failed to switch OKX fee type", ex);
            }
        });

        app.MapGet("/api/admin/okx/leverage", async (HttpContext context, OkxRepository repo) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            try
            {
                await repo.EnsureSchemaAsync(context.RequestAborted);
                var account = await ResolveAccountAsync(context, repo);
                if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
                var client = new OkxApiClient(account);
                var instId = context.Request.Query["instId"].FirstOrDefault();
                var mgnMode = context.Request.Query["mgnMode"].FirstOrDefault();
                return Results.Json(await BuildLeveragePayloadAsync(client, context.RequestAborted, instId, mgnMode));
            }
            catch (Exception ex)
            {
                return OkxErrorResult("Failed to read leverage from OKX", ex);
            }
        });

        app.MapPost("/api/admin/okx/leverage", async (HttpContext context, OkxRepository repo, OkxSetLeverageRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            try
            {
                await repo.EnsureSchemaAsync(context.RequestAborted);
                var account = await ResolveAccountAsync(context, repo);
                if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
                string kind = (dto.kind ?? "").Trim().ToLowerInvariant();
                if (kind != "perpetual" && kind != "expiry") return Results.BadRequest(new { message = "kind must be perpetual or expiry" });
                decimal lever = ParseLeverage(dto.lever);
                decimal maxLever = kind == "expiry" ? 20m : 100m;
                if (lever < 1m || lever > maxLever) return Results.BadRequest(new { message = $"Leverage must be between 1 and {maxLever.ToString(CultureInfo.InvariantCulture)}" });
                string mgnMode = string.IsNullOrWhiteSpace(dto.mgnMode) ? "cross" : dto.mgnMode.Trim().ToLowerInvariant();
                if (mgnMode != "cross" && mgnMode != "isolated") return Results.BadRequest(new { message = "mgnMode must be cross or isolated" });
                var client = new OkxApiClient(account);
                string instId = string.IsNullOrWhiteSpace(dto.instId) ? (kind == "perpetual" ? "BTC-USD-SWAP" : await client.GetNearestBtcUsdFutureInstrumentAsync(context.RequestAborted)) : dto.instId.Trim().ToUpperInvariant();
                var setResults = new List<object?>();
                async Task SetOneAsync(string? posSide)
                {
                    var payload = new Dictionary<string, object?> { ["instId"] = instId, ["lever"] = FormatLever(lever), ["mgnMode"] = mgnMode };
                    AddIfSet(payload, "posSide", posSide);
                    using var setResult = await client.SetLeverageAsync(payload, context.RequestAborted);
                    setResults.Add(JsonSerializer.Deserialize<object>(setResult.RootElement.GetRawText()));
                }
                string requestedPosSide = (dto.posSide ?? "").Trim().ToLowerInvariant();
                string posMode = await ReadOkxPositionModeAsync(client, context.RequestAborted);
                if (!string.IsNullOrWhiteSpace(requestedPosSide))
                {
                    await SetOneAsync(requestedPosSide);
                }
                else if (posMode == "long_short_mode")
                {
                    await SetOneAsync("long");
                    await SetOneAsync("short");
                }
                else
                {
                    await SetOneAsync(null);
                }
                var leverage = await BuildLeveragePayloadAsync(client, context.RequestAborted, instId, mgnMode);
                decimal confirmed = requestedPosSide == "long" ? ParseLeverage(leverage.selected?.longDisplay)
                    : requestedPosSide == "short" ? ParseLeverage(leverage.selected?.shortDisplay)
                    : ParseLeverage(leverage.selected?.lever);
                if (confirmed <= 0m || Math.Abs(confirmed - lever) > 0.0001m)
                {
                    return Results.Json(new
                    {
                        message = $"OKX set-leverage returned success, but reread did not confirm {FormatLever(lever)}x for {kind}",
                        code = "LEVERAGE_REREAD_MISMATCH",
                        instId,
                        requestedPosSide,
                        requestedLever = FormatLever(lever),
                        confirmedLever = confirmed > 0m ? FormatLever(confirmed) : "",
                        leverage
                    }, statusCode: StatusCodes.Status502BadGateway);
                }
                return Results.Json(new { ok = true, result = setResults, leverage });
            }
            catch (Exception ex)
            {
                return OkxErrorResult("Failed to set leverage in OKX", ex);
            }
        });

        app.MapPost("/api/admin/okx/transfer", async (HttpContext context, OkxRepository repo, OkxTransferRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            if (string.IsNullOrWhiteSpace(dto.ccy) || string.IsNullOrWhiteSpace(dto.amt) || string.IsNullOrWhiteSpace(dto.from) || string.IsNullOrWhiteSpace(dto.to))
            {
                return Results.BadRequest(new { message = "ccy, amt, from, to are required" });
            }
            string from = dto.from.Trim();
            string to = dto.to.Trim();
            if ((from != "6" && from != "18") || (to != "6" && to != "18"))
            {
                return Results.BadRequest(new { message = "Only Funding (6) and Trading (18) transfers are supported" });
            }
            if (from == to)
            {
                return Results.BadRequest(new { message = "Source and destination accounts must differ" });
            }
            var payload = new Dictionary<string, object?>
            {
                ["ccy"] = dto.ccy.Trim().ToUpperInvariant(),
                ["amt"] = dto.amt.Trim(),
                ["from"] = from,
                ["to"] = to
            };
            AddIfSet(payload, "clientId", dto.clientId);
            var client = new OkxApiClient(account);
            using var result = await client.TransferAsync(payload, context.RequestAborted);
            return Results.Json(JsonSerializer.Deserialize<object>(result.RootElement.GetRawText()));
        });

        app.MapPost("/api/admin/okx/orders/place", async (HttpContext context, OkxRepository repo, OkxPlaceOrderRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            try
            {
                await repo.EnsureSchemaAsync(context.RequestAborted);
                var account = await ResolveAccountAsync(context, repo);
                if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
                if (string.IsNullOrWhiteSpace(dto.instId) || string.IsNullOrWhiteSpace(dto.tdMode) || string.IsNullOrWhiteSpace(dto.side) || string.IsNullOrWhiteSpace(dto.ordType) || string.IsNullOrWhiteSpace(dto.sz))
                {
                    return Results.BadRequest(new { message = "instId, tdMode, side, ordType, sz are required" });
                }
                var payload = new Dictionary<string, object?>
                {
                    ["instId"] = dto.instId,
                    ["tdMode"] = dto.tdMode,
                    ["side"] = dto.side,
                    ["ordType"] = dto.ordType,
                    ["sz"] = dto.sz
                };
                AddIfSet(payload, "px", dto.px);
                AddIfSet(payload, "posSide", dto.posSide);
                AddIfSet(payload, "ccy", dto.ccy);
                AddIfSet(payload, "clOrdId", dto.clOrdId);
                if (dto.reduceOnly.HasValue) payload["reduceOnly"] = dto.reduceOnly.Value;
                var client = new OkxApiClient(account);
                using var result = await client.PlaceOrderAsync(payload, context.RequestAborted);
                return Results.Json(JsonSerializer.Deserialize<object>(result.RootElement.GetRawText()));
            }
            catch (Exception ex)
            {
                return OkxErrorResult("Failed to place OKX order", ex);
            }
        });

        app.MapPost("/api/admin/okx/nitro/orders/place", async (HttpContext context, OkxRepository repo, OkxNitroPlaceOrderRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            if (!IsNitroLiveSubmitEnabled())
            {
                return Results.Json(new
                {
                    message = "OKX Nitro live order submission is disabled. Set VAN_OKX_NITRO_LIVE_SUBMIT_ENABLED=true only after explicit Alex confirmation.",
                    code = "OKX_NITRO_LIVE_SUBMIT_DISABLED"
                }, statusCode: StatusCodes.Status403Forbidden);
            }
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            string sprdId = (dto.sprdId ?? "").Trim();
            string side = (dto.side ?? "").Trim().ToLowerInvariant();
            string ordType = (dto.ordType ?? "").Trim().ToLowerInvariant();
            string sz = (dto.sz ?? "").Trim();
            string px = (dto.px ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sprdId) || string.IsNullOrWhiteSpace(side) || string.IsNullOrWhiteSpace(ordType) || string.IsNullOrWhiteSpace(sz))
            {
                return Results.BadRequest(new { message = "sprdId, side, ordType, sz are required" });
            }
            if (side != "buy" && side != "sell") return Results.BadRequest(new { message = "side must be buy or sell" });
            if (ordType != "market" && ordType != "limit" && ordType != "post_only" && ordType != "ioc") return Results.BadRequest(new { message = "ordType must be market, limit, post_only, or ioc" });
            if (ordType != "market" && string.IsNullOrWhiteSpace(px)) return Results.BadRequest(new { message = "px is required for limit, post_only, and ioc Nitro spread orders" });
            if (!decimal.TryParse(sz, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedSz) || parsedSz <= 0m) return Results.BadRequest(new { message = "sz must be a positive number" });
            if (ordType != "market" && !decimal.TryParse(px, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) return Results.BadRequest(new { message = "px must be a valid number" });
            var payload = new Dictionary<string, object?>
            {
                ["sprdId"] = sprdId,
                ["side"] = side,
                ["ordType"] = ordType,
                ["sz"] = sz
            };
            if (ordType != "market") payload["px"] = px;
            AddIfSet(payload, "clOrdId", dto.clOrdId);
            AddIfSet(payload, "tag", dto.tag);
            var client = new OkxApiClient(account);
            using var result = await client.PlaceSpreadOrderAsync(payload, context.RequestAborted);
            return Results.Json(JsonSerializer.Deserialize<object>(result.RootElement.GetRawText()));
        });

        app.MapPost("/api/admin/okx/orders/cancel", async (HttpContext context, OkxRepository repo, OkxCancelOrderRequest dto) =>
        {
            if (!TryRequireAdmin(context, out var fail)) return fail!;
            await repo.EnsureSchemaAsync(context.RequestAborted);
            var account = await ResolveAccountAsync(context, repo);
            if (account == null) return Results.NotFound(new { message = "No OKX account selected" });
            if (string.IsNullOrWhiteSpace(dto.instId) || (string.IsNullOrWhiteSpace(dto.ordId) && string.IsNullOrWhiteSpace(dto.clOrdId)))
            {
                return Results.BadRequest(new { message = "instId and ordId or clOrdId are required" });
            }
            var payload = new Dictionary<string, object?> { ["instId"] = dto.instId };
            AddIfSet(payload, "ordId", dto.ordId);
            AddIfSet(payload, "clOrdId", dto.clOrdId);
            var client = new OkxApiClient(account);
            using var result = await client.CancelOrderAsync(payload, context.RequestAborted);
            return Results.Json(JsonSerializer.Deserialize<object>(result.RootElement.GetRawText()));
        });
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

    private static async Task<OkxAccountRecord?> ResolveAccountAsync(HttpContext context, OkxRepository repo)
    {
        if (context.Request.Query.TryGetValue("accountId", out var raw) && int.TryParse(raw.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return await repo.GetAccountByIdAsync(id, context.RequestAborted);
        }
        return await repo.GetActiveAccountAsync(context.RequestAborted);
    }

    private static object ToPublicAccount(OkxAccountRecord account)
    {
        return new
        {
            id = account.id,
            name = account.name,
            apiKeyMasked = OkxRepository.MaskApiKey(account.apiKey),
            isActive = account.isActive,
            isDemo = account.isDemo,
            storeMinuteEquity = account.storeMinuteEquity,
            equityCurrency = OkxRepository.NormalizeEquityCurrency(account.equityCurrency),
            createdAtUtc = account.createdAtUtc,
            updatedAtUtc = account.updatedAtUtc
        };
    }

    private static object BuildUsdtDelta(OkxSnapshotPoint? first, OkxSnapshotPoint? latest)
    {
        if (first == null || latest == null)
        {
            return new { absolute = 0m, percent = 0m };
        }
        decimal absolute = latest.totalEquityUsdt - first.totalEquityUsdt;
        decimal percent = first.totalEquityUsdt == 0m ? 0m : absolute / first.totalEquityUsdt * 100m;
        return new { absolute, percent };
    }

    private static OkxEquityPayload BuildEquityPayload(OkxAccountRecord account, List<OkxEquityPoint> dailyStored, List<OkxEquityPoint> minuteStored, List<OkxEquityPoint> dailyDrawdownMinuteStored)
    {
        string equityCurrency = OkxRepository.NormalizeEquityCurrency(account.equityCurrency);
        var dailyOrdered = dailyStored.OrderBy(p => p.tsUtc).ToList();
        var minuteOrdered = minuteStored.OrderBy(p => p.tsUtc).ToList();
        if (dailyOrdered.Count == 0)
        {
            return new OkxEquityPayload(
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
                new { points = new List<OkxChartPoint>(), drawdownPoints = new List<OkxChartPoint>() },
                new { points = new List<OkxChartPoint>(), drawdownPoints = new List<OkxChartPoint>() }
            );
        }

        DateTime nowUtc = DateTime.UtcNow;
        OkxEquityPoint latest = minuteOrdered.LastOrDefault() ?? dailyOrdered[^1];
        OkxEquityPoint first = dailyOrdered[0];
        OkxEquityPoint? ref24h = minuteOrdered.LastOrDefault(p => p.tsUtc <= nowUtc.AddHours(-24)) ?? dailyOrdered.LastOrDefault(p => p.tsUtc <= nowUtc.AddHours(-1)) ?? first;
        OkxEquityPoint? ref7d = dailyOrdered.LastOrDefault(p => p.tsUtc <= nowUtc.AddDays(-7)) ?? first;

        var dailyPoints = dailyOrdered.Select(p => new OkxChartPoint(p.tsUtc, p.totalEquity)).ToList();
        var minuteSeries = minuteOrdered.Select(p => new OkxChartPoint(p.tsUtc, p.totalEquity)).ToList();
        var dailyDrawdownMinuteSeries = dailyDrawdownMinuteStored.OrderBy(p => p.tsUtc).Select(p => new OkxChartPoint(p.tsUtc, p.totalEquity)).ToList();
        var dailyDrawdownPoints = BuildDailyDrawdownSeriesWithMinuteOverlay(dailyPoints, dailyDrawdownMinuteSeries);
        var minuteDrawdownPoints = BuildDrawdownSeriesWith30MinuteConfirmedHwm(minuteSeries, collapseToDate: false);
        decimal maxDrawdown24h = minuteDrawdownPoints.Count == 0 ? 0m : minuteDrawdownPoints.Max(p => p.value);
        decimal maxDrawdownAll = dailyDrawdownPoints.Count == 0 ? 0m : dailyDrawdownPoints.Max(p => p.value);

        return new OkxEquityPayload(
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

    private static object BuildEquityDelta(OkxEquityPoint? first, OkxEquityPoint? latest)
    {
        if (first == null || latest == null)
        {
            return new { absolute = 0m, percent = 0m };
        }
        decimal absolute = latest.totalEquity - first.totalEquity;
        decimal percent = first.totalEquity == 0m ? 0m : absolute / first.totalEquity * 100m;
        return new { absolute, percent };
    }

    private static List<OkxChartPoint> BuildDailyDrawdownSeriesWithMinuteOverlay(List<OkxChartPoint> dailyPoints, List<OkxChartPoint> minutePoints)
    {
        var dailyDrawdown = BuildDrawdownSeries(dailyPoints, collapseToDate: true);
        if (minutePoints.Count == 0) return dailyDrawdown;

        var byDate = dailyDrawdown.ToDictionary(p => DateOnly.FromDateTime(p.tsUtc), p => p.value);
        foreach (var point in BuildDrawdownSeriesWith30MinuteConfirmedHwm(minutePoints, collapseToDate: true))
        {
            byDate[DateOnly.FromDateTime(point.tsUtc)] = point.value;
        }

        return byDate
            .OrderBy(kv => kv.Key)
            .Select(kv => new OkxChartPoint(kv.Key.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), Math.Round(kv.Value, 4)))
            .ToList();
    }

    private static List<OkxChartPoint> BuildDrawdownSeriesWith30MinuteConfirmedHwm(List<OkxChartPoint> points, bool collapseToDate)
    {
        var list = new List<OkxChartPoint>();
        if (points.Count == 0) return list;

        decimal acceptedHwm = points[0].value;
        decimal? candidatePeak = null;
        int candidateCount = 0;

        foreach (var point in points)
        {
            if (point.value > acceptedHwm)
            {
                candidatePeak = candidatePeak.HasValue ? Math.Max(candidatePeak.Value, point.value) : point.value;
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

            decimal dd = acceptedHwm > 0m ? (acceptedHwm - point.value) / acceptedHwm * 100m : 0m;
            if (dd < 0m) dd = 0m;
            DateTime ts = collapseToDate
                ? DateTime.SpecifyKind(point.tsUtc.Date, DateTimeKind.Utc)
                : point.tsUtc;
            if (collapseToDate && list.Count > 0 && list[^1].tsUtc == ts)
            {
                if (dd > list[^1].value) list[^1] = new OkxChartPoint(ts, Math.Round(dd, 4));
            }
            else
            {
                list.Add(new OkxChartPoint(ts, Math.Round(dd, 4)));
            }
        }
        return list;
    }

    private static List<OkxChartPoint> BuildDrawdownSeries(List<OkxChartPoint> points, bool collapseToDate)
    {
        var list = new List<OkxChartPoint>();
        if (points.Count == 0) return list;
        decimal peak = points[0].value;
        foreach (var point in points)
        {
            if (point.value > peak) peak = point.value;
            decimal dd = peak > 0m ? (peak - point.value) / peak * 100m : 0m;
            DateTime ts = collapseToDate
                ? DateTime.SpecifyKind(point.tsUtc.Date, DateTimeKind.Utc)
                : point.tsUtc;
            if (collapseToDate && list.Count > 0 && list[^1].tsUtc == ts)
            {
                if (dd > list[^1].value) list[^1] = new OkxChartPoint(ts, Math.Round(dd, 4));
            }
            else
            {
                list.Add(new OkxChartPoint(ts, Math.Round(dd, 4)));
            }
        }
        return list;
    }

    private sealed record OkxEquityPayload(object metrics, object daily, object minute);
    private sealed record OkxChartPoint(DateTime tsUtc, decimal value);
    private sealed record OkxLeveragePayload(string mgnMode, string posMode, OkxLeverageRow perpetual, OkxLeverageRow expiry, OkxLeverageRow? selected, DateTime fetchedAtUtc);
    private sealed record OkxLeverageRow(string kind, string label, string instId, string lever, string display, string mgnMode = "", string longDisplay = "", string shortDisplay = "");
    private sealed record OkxParsedError(string code, string msg, string sCode, string sMsg, string subCode);

    private static List<object> ParseOrderRows(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return new List<object>();
        return data.EnumerateArray().Select(row => new
        {
            ordId = OkxApiClient.ReadString(row, "ordId"),
            clOrdId = OkxApiClient.ReadString(row, "clOrdId"),
            instId = OkxApiClient.ReadString(row, "instId"),
            instType = OkxApiClient.ReadString(row, "instType"),
            side = OkxApiClient.ReadString(row, "side"),
            ordType = OkxApiClient.ReadString(row, "ordType"),
            state = OkxApiClient.ReadString(row, "state"),
            price = OkxApiClient.ReadString(row, "px"),
            avgPrice = OkxApiClient.ReadString(row, "avgPx"),
            fillPx = OkxApiClient.ReadString(row, "fillPx"),
            size = OkxApiClient.ReadString(row, "sz"),
            filled = OkxApiClient.ReadString(row, "accFillSz"),
            fillSz = OkxApiClient.ReadString(row, "fillSz"),
            fee = OkxApiClient.ReadString(row, "fee"),
            feeCcy = OkxApiClient.ReadString(row, "feeCcy"),
            pnl = OkxApiClient.ReadString(row, "pnl"),
            pnlRatio = OkxApiClient.ReadString(row, "pnlRatio"),
            tradeCcy = OkxApiClient.ReadString(row, "tradeCcy"),
            tdMode = OkxApiClient.ReadString(row, "tdMode"),
            reduceOnly = OkxApiClient.ReadString(row, "reduceOnly"),
            createdAt = OkxApiClient.ReadString(row, "cTime"),
            updatedAt = OkxApiClient.ReadString(row, "uTime")
        }).ToList<object>();
    }

    private static List<object> ParseFillRows(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return new List<object>();
        return data.EnumerateArray().Select(row => new
        {
            ordId = OkxApiClient.ReadString(row, "ordId"),
            instId = OkxApiClient.ReadString(row, "instId"),
            side = OkxApiClient.ReadString(row, "side"),
            fillPx = OkxApiClient.ReadString(row, "fillPx"),
            fillSz = OkxApiClient.ReadString(row, "fillSz"),
            fee = OkxApiClient.ReadString(row, "fee"),
            feeCcy = OkxApiClient.ReadString(row, "feeCcy"),
            execType = OkxApiClient.ReadString(row, "execType"),
            fillTime = OkxApiClient.ReadString(row, "fillTime")
        }).ToList<object>();
    }

    private static List<object> ParseFundingRows(JsonDocument doc)
    {
        var items = new List<object>();
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return items;
        }
        foreach (var row in data.EnumerateArray())
        {
            items.Add(new
            {
                ccy = OkxApiClient.ReadString(row, "ccy"),
                bal = OkxApiClient.ReadString(row, "bal"),
                availBal = OkxApiClient.ReadString(row, "availBal"),
                frozenBal = OkxApiClient.ReadString(row, "frozenBal")
            });
        }
        return items;
    }

    private static List<object> ParseFundingHistoryRows(JsonDocument doc)
    {
        var items = new List<object>();
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return items;
        }
        foreach (var row in data.EnumerateArray())
        {
            var type = OkxApiClient.ReadString(row, "type");
            var ccy = OkxApiClient.ReadString(row, "ccy");
            var amount = OkxApiClient.ReadString(row, "balChg");
            items.Add(new
            {
                billId = OkxApiClient.ReadString(row, "billId"),
                ts = OkxApiClient.ReadString(row, "ts"),
                ccy,
                type,
                subType = OkxApiClient.ReadString(row, "subType"),
                amount,
                balance = OkxApiClient.ReadString(row, "bal"),
                note = BuildFundingHistoryNote(type, amount, ccy)
            });
        }
        return items;
    }

    private static string BuildFundingHistoryNote(string type, string amount, string ccy)
    {
        decimal.TryParse(amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amt);
        var direction = amt < 0 ? "Transferred to trading account" : "Received from main account";
        return string.IsNullOrWhiteSpace(ccy) ? direction : $"{direction} {ccy}";
    }

    private static object ToOkxFundingMinutePayloadRow(OkxFundingRow row) => new
    {
        instId = row.instId,
        symbol = row.instId,
        displayName = row.displayName,
        tsUtc = row.minuteUtc,
        snapshotMinuteUtc = row.minuteUtc,
        snapshot_minute_utc = row.minuteUtc,
        fundingTimeUtc = row.fundingTimeUtc,
        nextFundingTimeUtc = row.nextFundingTimeUtc,
        currentFunding = row.currentFunding,
        current_funding = PercentRate(row.currentFunding),
        interest8h = row.interest8h,
        interest_8h = PercentRate(row.interest8h),
        settledFunding = row.settledFunding,
        settState = row.settState,
        source = row.source
    };

    private static IEnumerable<object> ParseOkxFundingHistoryRows(JsonElement root, string instId)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) yield break;
        var rows = new List<object>();
        foreach (var item in data.EnumerateArray())
        {
            string rowInstId = OkxRepository.NormalizeFundingInstId(OkxApiClient.ReadString(item, "instId"));
            if (!string.Equals(rowInstId, instId, StringComparison.OrdinalIgnoreCase)) continue;
            long? fundingMs = ReadNullableLong(item, "fundingTime");
            DateTime? fundingTimeUtc = fundingMs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(fundingMs.Value).UtcDateTime : null;
            decimal? rate = ReadNullableDecimal(item, "realizedRate") ?? ReadNullableDecimal(item, "fundingRate");
            rows.Add(new
            {
                instId = rowInstId,
                symbol = rowInstId,
                displayName = OkxFundingDisplayName(rowInstId),
                tsUtc = fundingTimeUtc,
                recordTimeUtc = fundingTimeUtc,
                fundingTimeUtc,
                time = fundingMs,
                fundingRate = rate,
                fundRate = rate,
                currentFunding = null as decimal?,
                current_funding = null as decimal?,
                interest8h = null as decimal?,
                interest_8h = null as decimal?,
                source = "okx:/api/v5/public/funding-rate-history"
            });
        }
        foreach (var row in rows.AsEnumerable().Reverse()) yield return row;
    }

    private static decimal? PercentRate(decimal? value) => value.HasValue ? value.Value * 100m : null;

    private static string OkxFundingDisplayName(string instId) => instId switch
    {
        "BTC-USDT-SWAP" => "BTCUSDT Perp",
        "ETH-USDT-SWAP" => "ETHUSDT Perp",
        _ => instId
    };

    private static decimal? ReadNullableDecimal(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static long? ReadNullableLong(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }


    private static async Task<OkxLeveragePayload> BuildLeveragePayloadAsync(OkxApiClient client, CancellationToken ct, string? selectedInstId = null, string? selectedMgnMode = null)
    {
        string expiryInstId = await client.GetNearestBtcUsdFutureInstrumentAsync(ct);
        string posMode = await ReadOkxPositionModeAsync(client, ct);
        var perpetual = await ReadLeverageRowAsync(client, "perpetual", "BTCUSD Perpetual", "BTC-USD-SWAP", "cross", ct);
        var expiry = await ReadLeverageRowAsync(client, "expiry", "BTCUSD Expiry", expiryInstId, "cross", ct);
        OkxLeverageRow? selected = null;
        selectedInstId = (selectedInstId ?? "").Trim().ToUpperInvariant();
        selectedMgnMode = (selectedMgnMode ?? "cross").Trim().ToLowerInvariant();
        if (selectedMgnMode != "cross" && selectedMgnMode != "isolated") selectedMgnMode = "cross";
        if (!string.IsNullOrWhiteSpace(selectedInstId))
        {
            selectedMgnMode = await ReadOkxInstrumentMarginModeAsync(client, selectedInstId, selectedMgnMode, ct);
            string label = selectedInstId.Replace("-USDT-SWAP", "USDT Perp", StringComparison.OrdinalIgnoreCase).Replace("-USD-SWAP", "USD Perp", StringComparison.OrdinalIgnoreCase).Replace("-", " ", StringComparison.OrdinalIgnoreCase);
            selected = await ReadLeverageRowAsync(client, "selected", label, selectedInstId, selectedMgnMode, ct);
        }
        return new OkxLeveragePayload(selectedMgnMode, posMode, perpetual, expiry, selected, DateTime.UtcNow);
    }

    private static async Task<string> ReadOkxInstrumentMarginModeAsync(OkxApiClient client, string instId, string preferredMgnMode, CancellationToken ct)
    {
        instId = (instId ?? "").Trim().ToUpperInvariant();
        static string NormalizeMode(string raw)
        {
            raw = (raw ?? "").Trim().ToLowerInvariant();
            return raw == "cross" || raw == "isolated" ? raw : "";
        }
        try
        {
            using var positionsDoc = await client.GetPositionsAsync(ct);
            if (positionsDoc.RootElement.TryGetProperty("data", out var positions) && positions.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in positions.EnumerateArray())
                {
                    if (!string.Equals(OkxApiClient.ReadString(row, "instId"), instId, StringComparison.OrdinalIgnoreCase)) continue;
                    var mode = NormalizeMode(OkxApiClient.ReadString(row, "mgnMode"));
                    if (!string.IsNullOrWhiteSpace(mode)) return mode;
                }
            }
        }
        catch { }
        try
        {
            using var ordersDoc = await client.GetOpenOrdersAsync(ct);
            if (ordersDoc.RootElement.TryGetProperty("data", out var orders) && orders.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in orders.EnumerateArray())
                {
                    if (!string.Equals(OkxApiClient.ReadString(row, "instId"), instId, StringComparison.OrdinalIgnoreCase)) continue;
                    var mode = NormalizeMode(OkxApiClient.ReadString(row, "tdMode"));
                    if (!string.IsNullOrWhiteSpace(mode)) return mode;
                }
            }
        }
        catch { }
        var preferred = NormalizeMode(preferredMgnMode);
        return string.IsNullOrWhiteSpace(preferred) ? "cross" : preferred;
    }

    private static async Task<string> ReadOkxPositionModeAsync(OkxApiClient client, CancellationToken ct)
    {
        using var doc = await client.GetAccountConfigAsync(ct);
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            return OkxApiClient.ReadString(data[0], "posMode");
        }
        return "";
    }

    private static async Task<OkxLeverageRow> ReadLeverageRowAsync(OkxApiClient client, string kind, string label, string instId, string mgnMode, CancellationToken ct)
    {
        using var doc = await client.GetLeverageInfoAsync(instId, mgnMode, ct);
        string lever = "";
        string actualMgnMode = mgnMode;
        string longLever = "";
        string shortLever = "";
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
        {
            foreach (var row in data.EnumerateArray())
            {
                var rowLever = OkxApiClient.ReadString(row, "lever");
                var posSide = OkxApiClient.ReadString(row, "posSide").Trim().ToLowerInvariant();
                var rowMgnMode = OkxApiClient.ReadString(row, "mgnMode").Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(rowMgnMode)) actualMgnMode = rowMgnMode;
                if (string.IsNullOrWhiteSpace(lever)) lever = rowLever;
                if (posSide == "long") longLever = rowLever;
                if (posSide == "short") shortLever = rowLever;
            }
        }
        decimal parsed = ParseLeverage(lever);
        if (parsed <= 0m)
        {
            throw new InvalidOperationException($"OKX leverage-info returned no leverage for {instId}");
        }
        string formatted = FormatLever(parsed);
        string longDisplay = ParseLeverage(longLever) > 0m ? FormatLever(ParseLeverage(longLever)) + "x" : formatted + "x";
        string shortDisplay = ParseLeverage(shortLever) > 0m ? FormatLever(ParseLeverage(shortLever)) + "x" : formatted + "x";
        return new OkxLeverageRow(kind, label, instId, formatted, formatted + "x", actualMgnMode, longDisplay, shortDisplay);
    }

    private static bool TryReadPayloadLever(OkxLeveragePayload payload, string kind, out decimal lever)
    {
        lever = kind switch
        {
            "selected" => ParseLeverage(payload.selected?.lever),
            "expiry" => ParseLeverage(payload.expiry.lever),
            _ => ParseLeverage(payload.perpetual.lever)
        };
        return lever > 0m;
    }

    private static bool IsNitroLiveSubmitEnabled()
    {
        return string.Equals(Environment.GetEnvironmentVariable("VAN_OKX_NITRO_LIVE_SUBMIT_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static IResult OkxErrorResult(string message, Exception ex)
    {
        var parsed = TryParseOkxError(ex.Message);
        if (parsed != null)
        {
            return Results.Json(new
            {
                message = $"{message}: {parsed.sMsg}",
                code = "OKX_API_ERROR",
                okxCode = parsed.code,
                okxMessage = parsed.msg,
                sCode = parsed.sCode,
                sMsg = parsed.sMsg,
                subCode = parsed.subCode
            }, statusCode: StatusCodes.Status502BadGateway);
        }
        return Results.Json(new { message = $"{message}: {ex.Message}", code = "OKX_API_ERROR" }, statusCode: StatusCodes.Status502BadGateway);
    }

    private static OkxParsedError? TryParseOkxError(string rawMessage)
    {
        const string marker = "raw=";
        int idx = rawMessage.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        string raw = rawMessage[(idx + marker.Length)..].Trim();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            string code = OkxApiClient.ReadString(root, "code");
            string msg = OkxApiClient.ReadString(root, "msg");
            string sCode = "";
            string sMsg = "";
            string subCode = "";
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
            {
                var row = data[0];
                sCode = OkxApiClient.ReadString(row, "sCode");
                sMsg = OkxApiClient.ReadString(row, "sMsg");
                subCode = OkxApiClient.ReadString(row, "subCode");
            }
            if (string.IsNullOrWhiteSpace(sMsg)) sMsg = string.IsNullOrWhiteSpace(msg) ? rawMessage : msg;
            return new OkxParsedError(code, msg, sCode, sMsg.Trim(), subCode);
        }
        catch
        {
            return null;
        }
    }

    private static decimal ParseLeverage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        string clean = raw.Trim().TrimEnd('x', 'X');
        return decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    private static string FormatLever(decimal value)
    {
        if (value <= 0m) return "";
        return value % 1m == 0m ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static void AddIfSet(Dictionary<string, object?> payload, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) payload[key] = value.Trim();
    }
}

public sealed class OkxCreateAccountRequest
{
    public string? name { get; set; }
    public string? apiKey { get; set; }
    public string? apiSecret { get; set; }
    public string? passphrase { get; set; }
    public bool isDemo { get; set; }
    public bool setActive { get; set; } = true;
    public bool storeMinuteEquity { get; set; }
    public string? equityCurrency { get; set; } = "USD";
}

public sealed class OkxUpdateAccountSettingsRequest
{
    public bool storeMinuteEquity { get; set; }
    public string? equityCurrency { get; set; } = "USD";
}

public sealed class OkxSetAccountModeRequest
{
    public string? mode { get; set; }
    public string? advancedMode { get; set; }
}

public sealed class OkxSetLeverageRequest
{
    public string? kind { get; set; }
    public string? instId { get; set; }
    public string? lever { get; set; }
    public string? mgnMode { get; set; } = "cross";
    public string? posSide { get; set; }
}

public sealed class OkxSetFeeTypeRequest
{
    public string? feeType { get; set; }
}

public sealed class OkxTransferRequest
{
    public string? ccy { get; set; }
    public string? amt { get; set; }
    public string? from { get; set; }
    public string? to { get; set; }
    public string? clientId { get; set; }
}

public sealed class OkxPlaceOrderRequest
{
    public string? instId { get; set; }
    public string? tdMode { get; set; }
    public string? side { get; set; }
    public string? ordType { get; set; }
    public string? sz { get; set; }
    public string? px { get; set; }
    public string? posSide { get; set; }
    public string? ccy { get; set; }
    public string? clOrdId { get; set; }
    public bool? reduceOnly { get; set; }
}

public sealed class OkxNitroPlaceOrderRequest
{
    public string? sprdId { get; set; }
    public string? side { get; set; }
    public string? ordType { get; set; }
    public string? sz { get; set; }
    public string? px { get; set; }
    public string? clOrdId { get; set; }
    public string? tag { get; set; }
}

public sealed class OkxCancelOrderRequest
{
    public string? instId { get; set; }
    public string? ordId { get; set; }
    public string? clOrdId { get; set; }
}
