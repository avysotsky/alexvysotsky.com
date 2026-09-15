using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;
using VANWebService.Services;

namespace VANWebService.Models;

public sealed class CoincallSnapshotHostedService : BackgroundService
{
    private readonly Enums.LogAction _log;
    private readonly CoincallRepository _repo;

    private sealed record BalanceRow(string Ccy, decimal? Balance, decimal? Available, decimal? Frozen, decimal? Upl);
    private sealed record FieldDecimal(decimal? Value, string? Source);
    private sealed record RawAssetRow(string Asset, FieldDecimal Equity, FieldDecimal Available, FieldDecimal Frozen, FieldDecimal Upl, FieldDecimal UsdValue, string RawJson);
    private sealed record EquitySnapshot(decimal TotalEquity, decimal AvailableEquity, decimal UnrealizedPnl, decimal? MarginRatio, string EquityCurrency);

    public CoincallSnapshotHostedService(Enums.LogAction log, CoincallRepository repo)
    {
        _log = log;
        _repo = repo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _repo.EnsureSchemaAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _log("[COINCALL][SNAPSHOT][SCHEMA][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }

        try
        {
            await Task.Delay(DelayUntilNextMinuteSecond(3), stoppingToken);
        }
        catch
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCycle(stoppingToken);
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
            }
            catch
            {
                break;
            }
        }
    }

    private async Task RunCycle(CancellationToken ct)
    {
        try
        {
            DateTime nowUtc = DateTime.UtcNow;
            DateTime minuteUtc = CoincallRepository.TruncateToMinuteUtc(nowUtc);
            DateOnly dayUtc = DailyClearingDayUtc(nowUtc);
            var accounts = await _repo.GetAccountsAsync(includeSecrets: true, ct);
            foreach (var account in accounts)
            {
                try
                {
                    var client = new CoincallApiClient(account);
                    using var doc = await client.GetAccountSummaryAsync(ct);
                    decimal? btcUsd = null;
                    if (CoincallRepository.NormalizeEquityCurrency(account.equityCurrency) == "BTC" || HasBtcBalance(doc.RootElement))
                    {
                        try { btcUsd = await client.GetBtcUsdtLastPriceAsync(ct); } catch { btcUsd = null; }
                    }
                    var assetPrices = await BuildAssetPriceMapAsync(client, doc.RootElement, ct);
                    var assetPoints = BuildAssetEquitySnapshots(doc.RootElement, assetPrices);
                    foreach (var assetPoint in assetPoints)
                    {
                        await _repo.UpsertAssetDailyEquityAsync(account.id, dayUtc, minuteUtc, assetPoint, ct);
                        await _repo.UpsertAssetMinuteEquityAsync(account.id, minuteUtc, assetPoint, ct);
                    }
                    var snap = BuildEquitySnapshot(doc.RootElement, account.equityCurrency, btcUsd);
                    if (IsEquitySnapshotSuspect(snap, assetPoints, out var assetTotalUsd, out var diffUsd, out var diffPct))
                    {
                        _log($"[COINCALL][SNAPSHOT][EQUITY_GUARD] account={account.id} minute={minuteUtc:O} skipped account equity total={snap.TotalEquity.ToString(CultureInfo.InvariantCulture)} asset_total={assetTotalUsd.ToString(CultureInfo.InvariantCulture)} diff={diffUsd.ToString(CultureInfo.InvariantCulture)} diff_pct={diffPct.ToString(CultureInfo.InvariantCulture)}", Enums.LogLevel.llBaselogic);
                        continue;
                    }
                    await _repo.UpsertDailyEquityAsync(account.id, dayUtc, minuteUtc, snap.EquityCurrency, snap.TotalEquity, snap.AvailableEquity, snap.UnrealizedPnl, snap.MarginRatio, ct);
                    if (account.storeMinuteEquity)
                    {
                        await _repo.UpsertMinuteEquityAsync(account.id, minuteUtc, snap.EquityCurrency, snap.TotalEquity, snap.AvailableEquity, snap.UnrealizedPnl, snap.MarginRatio, ct);
                    }
                }
                catch (Exception ex)
                {
                    _log($"[COINCALL][SNAPSHOT][ACCOUNT {account.id}][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
                }
            }
        }
        catch (Exception ex)
        {
            _log("[COINCALL][SNAPSHOT][CYCLE][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }
    }

    private static TimeSpan DelayUntilNextMinuteSecond(int targetSecond)
    {
        DateTime now = DateTime.UtcNow;
        targetSecond = Math.Clamp(targetSecond, 0, 59);
        DateTime next = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, targetSecond, DateTimeKind.Utc);
        if (next <= now) next = next.AddMinutes(1);
        return next - now;
    }

    private static DateOnly DailyClearingDayUtc(DateTime utcNow)
    {
        var utc = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        return DateOnly.FromDateTime(utc.AddHours(-8));
    }

    private static EquitySnapshot BuildEquitySnapshot(JsonElement root, string configuredCurrency, decimal? btcUsd)
    {
        string currency = CoincallRepository.NormalizeEquityCurrency(configuredCurrency);
        var rows = CollectBalanceRows(root);
        decimal totalUsd = 0m, availableUsd = 0m, uplUsd = 0m;
        bool hasUsd = false;
        foreach (var row in rows)
        {
            string c = row.Ccy.ToUpperInvariant();
            if (c is "USD" or "USDT" or "USDC")
            {
                totalUsd += row.Balance ?? ((row.Available ?? 0m) + (row.Frozen ?? 0m));
                availableUsd += row.Available ?? row.Balance ?? 0m;
                uplUsd += row.Upl ?? 0m;
                hasUsd = true;
            }
            else if (c == "BTC" && btcUsd.HasValue && btcUsd.Value > 0m)
            {
                totalUsd += (row.Balance ?? ((row.Available ?? 0m) + (row.Frozen ?? 0m))) * btcUsd.Value;
                availableUsd += (row.Available ?? row.Balance ?? 0m) * btcUsd.Value;
                uplUsd += (row.Upl ?? 0m) * btcUsd.Value;
                hasUsd = true;
            }
            else if (TryAssetUsdFallback(row, root, out var total, out var available, out var upl))
            {
                totalUsd += total;
                availableUsd += available;
                uplUsd += upl;
                hasUsd = true;
            }
        }

        if (!hasUsd)
        {
            var any = rows.FirstOrDefault(r => r.Balance.HasValue || r.Available.HasValue || r.Upl.HasValue);
            totalUsd = any?.Balance ?? ((any?.Available ?? 0m) + (any?.Frozen ?? 0m));
            availableUsd = any?.Available ?? any?.Balance ?? 0m;
            uplUsd = any?.Upl ?? 0m;
        }

        if (currency == "BTC")
        {
            if (btcUsd.HasValue && btcUsd.Value > 0m)
            {
                return new EquitySnapshot(totalUsd / btcUsd.Value, availableUsd / btcUsd.Value, uplUsd / btcUsd.Value, null, currency);
            }
            return new EquitySnapshot(0m, 0m, 0m, null, currency);
        }
        return new EquitySnapshot(totalUsd, availableUsd, uplUsd, null, currency);
    }

    private static bool IsEquitySnapshotSuspect(EquitySnapshot snap, IReadOnlyCollection<CoincallAssetEquityPoint> assetPoints, out decimal assetTotalUsd, out decimal diffUsd, out decimal diffPct)
    {
        assetTotalUsd = assetPoints.Select(p => p.equityUsd).Where(v => v.HasValue).Sum(v => v!.Value);
        diffUsd = Math.Abs(snap.TotalEquity - assetTotalUsd);
        diffPct = assetTotalUsd > 0m ? diffUsd / assetTotalUsd * 100m : 0m;
        return string.Equals(snap.EquityCurrency, "USD", StringComparison.OrdinalIgnoreCase)
            && assetTotalUsd > 0m
            && diffUsd > 1000m
            && diffPct > 5m;
    }

    private static bool TryAssetUsdFallback(BalanceRow row, JsonElement root, out decimal totalUsd, out decimal availableUsd, out decimal uplUsd)
    {
        totalUsd = 0m;
        availableUsd = 0m;
        uplUsd = 0m;
        string asset = row.Ccy.Trim().ToUpperInvariant();
        var assetRows = CollectRawAssetRows(root)
            .Where(r => string.Equals(r.Asset, asset, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (assetRows.Count == 0) return false;

        decimal sourceUsd = 0m;
        bool hasSourceUsd = false;
        decimal nativeEquity = 0m;
        foreach (var assetRow in assetRows)
        {
            if (assetRow.UsdValue.Value.HasValue)
            {
                sourceUsd += assetRow.UsdValue.Value.Value;
                hasSourceUsd = true;
            }
            if (assetRow.Equity.Value.HasValue) nativeEquity += assetRow.Equity.Value.Value;
        }
        if (!hasSourceUsd) return false;

        totalUsd = sourceUsd;
        decimal? unitUsd = nativeEquity != 0m ? sourceUsd / nativeEquity : null;
        if (unitUsd.HasValue)
        {
            availableUsd = (row.Available ?? row.Balance ?? 0m) * unitUsd.Value;
            uplUsd = (row.Upl ?? 0m) * unitUsd.Value;
        }
        else
        {
            availableUsd = sourceUsd;
        }
        return true;
    }

    private static async Task<Dictionary<string, decimal>> BuildAssetPriceMapAsync(CoincallApiClient client, JsonElement root, CancellationToken ct)
    {
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USDT"] = 1m };
        foreach (var asset in CollectRawAssetRows(root).Select(r => r.Asset).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (asset is not ("BTC" or "ETH")) continue;
            try
            {
                var price = await client.GetSpotUsdtLastPriceAsync(asset, ct);
                if (price > 0m) prices[asset] = price;
            }
            catch
            {
                // Leave USD valuation null when the confirmed price source is unavailable.
            }
        }
        return prices;
    }

    private static List<CoincallAssetEquityPoint> BuildAssetEquitySnapshots(JsonElement root, IReadOnlyDictionary<string, decimal> usdtPrices)
    {
        var result = new List<CoincallAssetEquityPoint>();
        foreach (var group in CollectRawAssetRows(root).GroupBy(r => r.Asset, StringComparer.OrdinalIgnoreCase))
        {
            var rows = group.ToList();
            decimal? Sum(Func<RawAssetRow, decimal?> pick)
            {
                bool any = false;
                decimal sum = 0m;
                foreach (var row in rows)
                {
                    var value = pick(row);
                    if (!value.HasValue) continue;
                    any = true;
                    sum += value.Value;
                }
                return any ? sum : null;
            }

            string? Sources(Func<RawAssetRow, FieldDecimal> pick)
            {
                var sources = rows.Select(r => pick(r).Source).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return sources.Length == 0 ? null : string.Join(",", sources);
            }

            var sourceUsd = Sum(r => r.UsdValue.Value);
            decimal? equityUsdt = sourceUsd;
            string valuationSource = sourceUsd.HasValue ? "source" : "none";
            string? equityUsdSource = Sources(r => r.UsdValue);
            var equityNative = Sum(r => r.Equity.Value);

            if (!equityUsdt.HasValue && equityNative.HasValue && usdtPrices.TryGetValue(group.Key, out var price) && price > 0m)
            {
                equityUsdt = equityNative.Value * price;
                valuationSource = "derived";
                equityUsdSource = group.Key == "USDT" ? "fixed:USDT=1" : group.Key + "USDT:spot_1min_close";
            }

            result.Add(new CoincallAssetEquityPoint
            {
                tsUtc = DateTime.MinValue,
                asset = group.Key.ToUpperInvariant(),
                equityNative = equityNative,
                availableNative = Sum(r => r.Available.Value),
                frozenNative = Sum(r => r.Frozen.Value),
                unrealizedPnlNative = Sum(r => r.Upl.Value),
                equityUsdt = equityUsdt,
                equityUsd = equityUsdt,
                valuationSource = valuationSource,
                equityNativeSource = Sources(r => r.Equity),
                availableNativeSource = Sources(r => r.Available),
                frozenNativeSource = Sources(r => r.Frozen),
                unrealizedPnlNativeSource = Sources(r => r.Upl),
                equityUsdSource = equityUsdSource,
                rawJson = "[" + string.Join(",", rows.Select(r => r.RawJson)) + "]"
            });
        }
        return result;
    }

    private static bool HasBtcBalance(JsonElement root) => CollectBalanceRows(root).Any(r => string.Equals(r.Ccy, "BTC", StringComparison.OrdinalIgnoreCase));

    private static List<RawAssetRow> CollectRawAssetRows(JsonElement root)
    {
        var rows = new List<RawAssetRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var obj in EnumerateObjects(root))
        {
            string? asset = FindString(obj, "ccy", "currency", "coin", "asset");
            if (string.IsNullOrWhiteSpace(asset)) continue;
            asset = asset.Trim().ToUpperInvariant();
            if (asset is not ("BTC" or "ETH" or "USDT")) continue;

            var equity = FindDecimalField(obj, "equityamount", "marginbalance", "accountequity", "equity", "eq", "totalbalance", "walletbalance", "cashbalanceamount", "cashbalance", "cashbal", "total", "balance", "bal");
            var available = FindDecimalField(obj, "available", "avail", "availablebalance", "availablebal", "availbalance", "availeq", "availableequity", "free");
            var frozen = FindDecimalField(obj, "frozen", "locked", "hold", "freeze", "usedmargin");
            var upl = FindDecimalField(obj, "upl", "unrealizedpnl", "unrealisedpnl", "unrealisedprofit", "unrealizedprofit");
            var usd = FindDecimalField(obj, "usdtvalue", "dollarvalue");
            if (!equity.Value.HasValue && !available.Value.HasValue && !frozen.Value.HasValue && !upl.Value.HasValue && !usd.Value.HasValue) continue;

            string raw = obj.GetRawText();
            string key = asset + "|" + raw;
            if (seen.Add(key)) rows.Add(new RawAssetRow(asset, equity, available, frozen, upl, usd, raw));
        }
        return rows;
    }

    private static List<BalanceRow> CollectBalanceRows(JsonElement root)
    {
        var rows = new List<BalanceRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in EnumerateObjects(root))
        {
            string? ccy = FindString(obj, "ccy", "currency", "coin", "asset", "basecurrency", "symbol", "margincoin");
            decimal? balance = FindDecimal(obj, "equityamount", "marginbalance", "accountequity", "equity", "eq", "totalbalance", "walletbalance", "cashbalanceamount", "cashbalance", "cashbal", "total", "balance", "bal");
            decimal? available = FindDecimal(obj, "available", "avail", "availablebalance", "availablebal", "availbalance", "availeq", "availableequity", "free");
            decimal? frozen = FindDecimal(obj, "frozen", "locked", "hold", "freeze", "usedmargin");
            decimal? upl = FindDecimal(obj, "upl", "unrealizedpnl", "unrealisedpnl", "unrealisedprofit", "unrealizedprofit");
            if (string.IsNullOrWhiteSpace(ccy) || (!balance.HasValue && !available.HasValue && !frozen.HasValue && !upl.HasValue)) continue;
            string key = ccy.Trim().ToUpperInvariant() + "|" + (balance?.ToString(CultureInfo.InvariantCulture) ?? "") + "|" + (available?.ToString(CultureInfo.InvariantCulture) ?? "") + "|" + (frozen?.ToString(CultureInfo.InvariantCulture) ?? "") + "|" + (upl?.ToString(CultureInfo.InvariantCulture) ?? "");
            if (seen.Add(key)) rows.Add(new BalanceRow(ccy.Trim().ToUpperInvariant(), balance, available, frozen, upl));
        }
        return rows;
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
            foreach (var prop in node.EnumerateObject())
            foreach (var child in EnumerateObjects(prop.Value))
                yield return child;
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            foreach (var child in EnumerateObjects(item))
                yield return child;
        }
    }

    private static string? FindString(JsonElement obj, params string[] names)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!names.Contains(prop.Name.ToLowerInvariant())) continue;
            return prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetRawText(),
                _ => prop.Value.ToString()
            };
        }
        return null;
    }

    private static decimal? FindDecimal(JsonElement obj, params string[] names)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!names.Contains(prop.Name.ToLowerInvariant())) continue;
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var n)) return n;
            if (prop.Value.ValueKind == JsonValueKind.String && decimal.TryParse(prop.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        }
        return null;
    }

    private static FieldDecimal FindDecimalField(JsonElement obj, params string[] names)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!names.Contains(prop.Name.ToLowerInvariant())) continue;
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var n)) return new FieldDecimal(n, prop.Name);
            if (prop.Value.ValueKind == JsonValueKind.String && decimal.TryParse(prop.Value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return new FieldDecimal(s, prop.Name);
        }
        return new FieldDecimal(null, null);
    }
}
