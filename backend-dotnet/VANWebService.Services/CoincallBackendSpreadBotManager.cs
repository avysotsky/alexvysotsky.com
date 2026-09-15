using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;
using Npgsql;
using VANWebService.Models;

namespace VANWebService.Services;

public sealed class CoincallBackendSpreadBotManager
{
    private readonly CoincallSpreadBotRepository _repo;
    private readonly CoincallRepository _accounts;
    private readonly CoincallCandlesHostedService _candles;
    private readonly Enums.LogAction _log;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public CoincallBackendSpreadBotManager(CoincallSpreadBotRepository repo, CoincallRepository accounts, CoincallCandlesHostedService candles, Enums.LogAction log)
    {
        _repo = repo;
        _accounts = accounts;
        _candles = candles;
        _log = log;
    }

    public async Task<CoincallBackendSpreadBotInstance> StartAsync(CoincallBackendSpreadBotStartRequest request, CancellationToken ct)
    {
        string pair = CleanPair(request.pair);
        if (string.IsNullOrWhiteSpace(pair)) throw new InvalidOperationException("pair is required");
        string strategy = CleanName(request.strategy, "orders-net");
        string mode = CleanName(request.mode, "LLF-BS");
        string configJson = request.config.HasValue ? request.config.Value.GetRawText() : "{}";
        var account = request.accountId.HasValue
            ? await _accounts.GetAccountByIdAsync(request.accountId.Value, ct)
            : await _accounts.GetActiveAccountAsync(ct);
        if (account == null) throw new InvalidOperationException("No CoinCall account selected");

        var settings = await _repo.UpsertSettingsAsync(account.id, pair, strategy, mode, configJson, ct);
        pair = settings.pair;
        strategy = settings.strategy;
        mode = settings.mode;
        configJson = settings.configJson;
        var existing = await _repo.GetRunningInstanceAsync(account.id, pair, strategy, ct);
        if (existing != null)
        {
            if (SameJson(existing.configJson, configJson))
            {
                StartRunner(existing.id);
                await _repo.AppendLogAsync(existing.id, "BackendBotStartIgnoredAlreadyRunning", "start", "Backend start requested while instance is already running with the same config; returning existing instance", configJson, ct);
                return (await _repo.GetInstanceAsync(existing.id, ct)) ?? existing;
            }

            await _repo.AppendLogAsync(existing.id, "BackendBotRestartingWithUpdatedConfig", "start", "Backend start requested with updated config; stopping existing instance before starting a new one", configJson, ct);
            await StopAsync(existing.id, true, ct);
        }

        try
        {
            var instance = await _repo.CreateInstanceAsync(account.id, pair, strategy, mode, configJson, ct);
            await _repo.AppendLogAsync(instance.id, "BackendBotStarting", "start", "Backend CoinCall spreadbot instance started", configJson, ct);
            StartRunner(instance.id);
            return (await _repo.GetInstanceAsync(instance.id, ct)) ?? instance;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            existing = await _repo.GetRunningInstanceAsync(account.id, pair, strategy, ct);
            if (existing == null) throw;
            if (!SameJson(existing.configJson, configJson))
            {
                await _repo.AppendLogAsync(existing.id, "BackendBotRestartingWithUpdatedConfig", "start", "Backend start raced with an existing instance using different config; stopping existing instance before retry", configJson, ct);
                await StopAsync(existing.id, true, ct);
                var instance = await _repo.CreateInstanceAsync(account.id, pair, strategy, mode, configJson, ct);
                await _repo.AppendLogAsync(instance.id, "BackendBotStarting", "start", "Backend CoinCall spreadbot instance started", configJson, ct);
                StartRunner(instance.id);
                return (await _repo.GetInstanceAsync(instance.id, ct)) ?? instance;
            }
            StartRunner(existing.id);
            await _repo.AppendLogAsync(existing.id, "BackendBotStartIgnoredAlreadyRunning", "start", "Backend start raced with an already running instance using the same config; returning existing instance", configJson, ct);
            return (await _repo.GetInstanceAsync(existing.id, ct)) ?? existing;
        }
    }

    public Task<List<CoincallBackendSpreadBotInstance>> ListAsync(CancellationToken ct) => _repo.ListInstancesAsync(ct);
    public Task<CoincallBackendSpreadBotInstance?> GetAsync(Guid id, CancellationToken ct) => _repo.GetInstanceAsync(id, ct);
    public Task<List<CoincallBackendSpreadBotLogRow>> LogsAsync(Guid id, int limit, CancellationToken ct) => _repo.GetLogsAsync(id, limit, ct);

    public async Task<CoincallBackendSpreadBotSettings> SaveSettingsAsync(CoincallBackendSpreadBotSettingsRequest request, CancellationToken ct)
    {
        string pair = CleanPair(request.pair);
        if (string.IsNullOrWhiteSpace(pair)) throw new InvalidOperationException("pair is required");
        string strategy = CleanName(request.strategy, "orders-net");
        string mode = CleanName(request.mode, "LLF-BS");
        string configJson = request.config.HasValue ? request.config.Value.GetRawText() : "{}";
        var account = request.accountId.HasValue
            ? await _accounts.GetAccountByIdAsync(request.accountId.Value, ct)
            : await _accounts.GetActiveAccountAsync(ct);
        if (account == null) throw new InvalidOperationException("No CoinCall account selected");
        return await _repo.UpsertSettingsAsync(account.id, pair, strategy, mode, configJson, ct);
    }

    public async Task<CoincallBackendSpreadBotSettings?> GetSettingsAsync(int? accountId, string? pair, string? strategy, CancellationToken ct)
    {
        string cleanPair = CleanPair(pair);
        if (string.IsNullOrWhiteSpace(cleanPair)) throw new InvalidOperationException("pair is required");
        string cleanStrategy = CleanName(strategy, "orders-net");
        var account = accountId.HasValue
            ? await _accounts.GetAccountByIdAsync(accountId.Value, ct)
            : await _accounts.GetActiveAccountAsync(ct);
        if (account == null) throw new InvalidOperationException("No CoinCall account selected");
        return await _repo.GetSettingsAsync(account.id, cleanPair, cleanStrategy, ct);
    }

    public async Task<CoincallBackendSpreadBotInstance?> StopAsync(Guid id, bool cancelOwnedOrders, CancellationToken ct)
    {
        var instance = await _repo.GetInstanceAsync(id, ct);
        if (_running.TryRemove(id, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        if (cancelOwnedOrders && instance != null)
        {
            try
            {
                await CancelOwnedOpenOrdersAsync(instance, ct);
            }
            catch (Exception ex)
            {
                await _repo.AppendLogAsync(id, "BackendBotStopCancelFailed", "error", "Owned-order cancellation failed before stop: " + ex.Message, "{}", ct);
            }
        }
        string stoppedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await _repo.AppendLogAsync(id, "BackendBotStopping", "stop", cancelOwnedOrders ? "Stop requested; owned-order cancellation will be handled by execution loop" : "Stop requested", "{}", ct);
        await _repo.SetStatusAsync(id, "stopped", "{\"stoppedAtUtc\":\"" + stoppedAtUtc + "\"}", string.Empty, ct);
        if (instance != null && string.Equals(instance.strategy, "ddh", StringComparison.OrdinalIgnoreCase))
        {
            await _repo.AppendLogAsync(id, "SpreadBot_DDH_Stopped", "stop", "DDH backend stopped", "{\"stoppedAtUtc\":\"" + stoppedAtUtc + "\"}", ct);
        }
        return await _repo.GetInstanceAsync(id, ct);
    }

    private async Task CancelOwnedOpenOrdersAsync(CoincallBackendSpreadBotInstance instance, CancellationToken ct)
    {
        var account = await _accounts.GetAccountByIdAsync(instance.accountId, ct);
        if (account == null) throw new InvalidOperationException("CoinCall account #" + instance.accountId + " not found");
        var client = new CoincallApiClient(account);
        if (string.Equals(instance.strategy, "orders-net", StringComparison.OrdinalIgnoreCase))
        {
            var cfg = BackendOrdersNetConfig.Parse(instance.configJson, instance.pair, instance.mode);
            await CancelOwnedOrderIfOpenAsync(instance, client, "firstOrder", "firstOrderFilled", cfg.FirstLeg.Symbol, cfg.FirstSideName, cfg.FirstLeg.Market, ct);
            await CancelOwnedOrderIfOpenAsync(instance, client, "hedgeOrder", "hedgeOrderFilled", cfg.HedgeLeg.Symbol, cfg.HedgeSideName, cfg.HedgeLeg.Market, ct);
            await CancelOwnedOrderIfOpenAsync(instance, client, "previousHedgeOrder", "previousHedgeOrderFilled", cfg.HedgeLeg.Symbol, cfg.HedgeSideName, cfg.HedgeLeg.Market, ct);
            return;
        }
        await CancelOwnedOrderIfOpenAsync(instance, client, "firstOrder", "firstOrderFilled", JsonText(instance.stateJson, "firstLeg", ""), JsonText(instance.stateJson, "firstSide", ""), JsonText(instance.stateJson, "firstMarket", "futures"), ct);
        await CancelOwnedOrderIfOpenAsync(instance, client, "hedgeOrder", "hedgeOrderFilled", JsonText(instance.stateJson, "hedgeLeg", ""), JsonText(instance.stateJson, "hedgeSide", ""), JsonText(instance.stateJson, "hedgeMarket", "futures"), ct);
        await CancelOwnedOrderIfOpenAsync(instance, client, "activeOrder", "activeOrderFilled", JsonText(instance.stateJson, "activeLeg", ""), JsonText(instance.stateJson, "activeSide", ""), JsonText(instance.stateJson, "activeMarket", "futures"), ct);
    }

    private async Task CancelOwnedOrderIfOpenAsync(CoincallBackendSpreadBotInstance instance, CoincallApiClient client, string orderProperty, string filledProperty, string symbol, string side, string market, CancellationToken ct)
    {
        if (JsonBool(instance.stateJson, filledProperty)) return;
        JsonElement order = JsonObject(instance.stateJson, orderProperty);
        if (order.ValueKind != JsonValueKind.Object) return;
        try
        {
            using var res = string.Equals(market, "spot", StringComparison.OrdinalIgnoreCase)
                ? await client.CancelSpotOrderAsync(order.Clone(), ct)
                : await client.CancelFuturesOrderAsync(order.Clone(), ct);
            await _repo.AppendLogAsync(instance.id, "BackendBotOwnedOrderCancelled", "cancel", "Cancelled backend-owned " + symbol + " " + side + " order from " + orderProperty, res.RootElement.GetRawText(), ct);
        }
        catch (InvalidOperationException ex) when (IsCoincallCancelFailed(ex))
        {
            var leg = new BackendGenericLeg(symbol, string.IsNullOrWhiteSpace(market) ? "futures" : market);
            bool stillOpen = await IsGenericOrderStillOpenAsync(instance.accountId, instance.stateJson, orderProperty, leg, side, ct);
            string state = stillOpen ? "BackendBotOwnedOrderCancelStillOpen" : "BackendBotOwnedOrderCancelStale";
            string action = stillOpen ? "wait" : "cancel";
            string message = stillOpen
                ? "Cancel returned CoinCall 10537 for backend-owned " + symbol + " " + side + " order from " + orderProperty + ", and the order is still open; backend will not assume it was cancelled"
                : "Cancel returned CoinCall 10537 for backend-owned " + symbol + " " + side + " order from " + orderProperty + ", but the order is no longer in open orders";
            await _repo.AppendLogAsync(instance.id, state, action, message, "{}", ct);
            if (stillOpen) throw;
        }
    }

    public async Task ResumeRunningAsync(CancellationToken ct)
    {
        var items = await _repo.ListInstancesAsync(ct);
        foreach (var item in items)
        {
            if (string.Equals(item.status, "running", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.status, "starting", StringComparison.OrdinalIgnoreCase))
            {
                await _repo.AppendLogAsync(item.id, "BackendBotResume", "resume", "Backend service resumed persisted spreadbot instance", "{}", ct);
                StartRunner(item.id);
            }
        }
    }

    public async Task StopAllAsync(CancellationToken ct)
    {
        foreach (var id in _running.Keys)
        {
            if (_running.TryRemove(id, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                try { await _repo.AppendLogAsync(id, "BackendBotShutdown", "shutdown", "Backend service shutdown; runner cancelled", "{}", ct); }
                catch (Exception ex) { _log("CoinCall spreadbot shutdown log failed: " + ex.Message, Enums.LogLevel.llExceptions); }
            }
        }
    }

    private void StartRunner(Guid id)
    {
        if (_running.ContainsKey(id)) return;
        var cts = new CancellationTokenSource();
        if (!_running.TryAdd(id, cts))
        {
            cts.Dispose();
            return;
        }
        _ = Task.Run(() => RunAsync(id, cts.Token));
    }

    private static bool SameJson(string? left, string? right)
    {
        try
        {
            using var leftDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(left) ? "{}" : left);
            using var rightDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(right) ? "{}" : right);
            return JsonElementDeepEquals(leftDoc.RootElement, rightDoc.RootElement);
        }
        catch (JsonException)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
        }
    }

    private static bool JsonElementDeepEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var rightProps = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var prop in right.EnumerateObject()) rightProps[prop.Name] = prop.Value;
                foreach (var prop in left.EnumerateObject())
                {
                    if (!rightProps.TryGetValue(prop.Name, out var rightValue)) return false;
                    if (!JsonElementDeepEquals(prop.Value, rightValue)) return false;
                    rightProps.Remove(prop.Name);
                }
                return rightProps.Count == 0;
            }
            case JsonValueKind.Array:
            {
                var leftItems = left.EnumerateArray();
                var rightItems = right.EnumerateArray();
                while (leftItems.MoveNext())
                {
                    if (!rightItems.MoveNext()) return false;
                    if (!JsonElementDeepEquals(leftItems.Current, rightItems.Current)) return false;
                }
                return !rightItems.MoveNext();
            }
            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
            case JsonValueKind.True:
            case JsonValueKind.False:
                return left.GetBoolean() == right.GetBoolean();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;
            default:
                return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
        }
    }

    private async Task RunAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var instance = await _repo.GetInstanceAsync(id, ct);
            if (instance == null) throw new InvalidOperationException("Spreadbot instance not found");
            string strategy = StrategyName(instance);
            bool executionArmed = JsonBool(instance.configJson, "executionArmed");
            if (!executionArmed)
            {
                await _repo.SetStatusAsync(id, "running", StateJson("idle", "not-armed", null, null), string.Empty, ct);
                await _repo.AppendLogAsync(id, "BackendBotRunning", "runner", "Backend runner is alive; execution is not armed because config.executionArmed is not true", "{}", ct);
            }
            else
            {
                await RunBackendCycleAsync(instance, strategy, ct);
            }
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(executionArmed ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(15), ct);
                var latest = await _repo.GetInstanceAsync(id, ct);
                string state = latest?.stateJson ?? "{}";
                if (latest != null && executionArmed) await RunBackendCycleAsync(latest, strategy, ct);
                else
                {
                    string phase = JsonText(state, "phase", "idle");
                    string execution = JsonText(state, "execution", "not-armed");
                    await _repo.SetStatusAsync(id, "running", StateJson(phase, execution, BoolJson(state, "l1Placed"), JsonText(state, "l1State", "")), string.Empty, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal stop/shutdown path.
        }
        catch (Exception ex)
        {
            _running.TryRemove(id, out _);
            try
            {
                await _repo.SetStatusAsync(id, "failed", "{}", ex.Message, CancellationToken.None);
                await _repo.AppendLogAsync(id, "BackendBotFailed", "error", ex.Message, "{}", CancellationToken.None);
            }
            catch (Exception logEx)
            {
                _log("CoinCall spreadbot failure log failed: " + logEx.Message, Enums.LogLevel.llExceptions);
            }
        }
    }

    private async Task RunBackendCycleAsync(CoincallBackendSpreadBotInstance instance, string strategy, CancellationToken ct)
    {
        if (string.Equals(strategy, "orders-net", StringComparison.OrdinalIgnoreCase))
        {
            var cfg = BackendOrdersNetConfig.Parse(instance.configJson, instance.pair, instance.mode);
            await RunOrdersNetCycleAsync(instance, cfg, ct);
            return;
        }
        if (string.Equals(strategy, "ddh", StringComparison.OrdinalIgnoreCase))
        {
            await RunDdhCycleAsync(instance, BackendGenericBotConfig.Parse(instance.configJson, instance.pair, instance.mode, "ddh"), ct);
            return;
        }
        if (string.Equals(strategy, "limit-limit", StringComparison.OrdinalIgnoreCase))
        {
            await RunLimitLimitCycleAsync(instance, BackendGenericBotConfig.Parse(instance.configJson, instance.pair, instance.mode, "limit-limit"), ct);
            return;
        }
        throw new InvalidOperationException("Unsupported spreadbot backend strategy: " + strategy);
    }

    private async Task RunOrdersNetCycleAsync(CoincallBackendSpreadBotInstance instance, BackendOrdersNetConfig cfg, CancellationToken ct)
    {
        if (!string.Equals(cfg.Strategy, "orders-net", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only orders-net backend strategy is supported");
        if (!string.Equals(cfg.L1Mode, "price", StringComparison.OrdinalIgnoreCase) && !string.Equals(cfg.L1Mode, "trail", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backend Orders Net armed execution supports L1 Price or L1 Trail only");
        if (cfg.Amount <= 0) throw new InvalidOperationException("Amount must be positive");
        if (cfg.L1Price <= 0) throw new InvalidOperationException("L1 Price must be positive");

        int currentLevel = Math.Clamp(JsonInt(instance.stateJson, "currentLevel", 1), 1, cfg.Levels);
        instance = await TryMarkOrdersNetPreviousHedgeFillAsync(instance, cfg, ct);
        instance = await ReconcileOrdersNetPreviousHedgeCompletionAsync(instance, cfg, currentLevel, ct);
        currentLevel = Math.Clamp(JsonInt(instance.stateJson, "currentLevel", 1), 1, cfg.Levels);
        if (JsonBool(instance.stateJson, "gridCompleted") || currentLevel > cfg.Levels)
        {
            await _repo.SetStatusAsync(instance.id, "completed", StateWithHeartbeat(instance.stateJson, "completed"), string.Empty, ct);
            await _repo.AppendLogAsync(instance.id, Branch(cfg) + "_Completed", "state", "Orders Net grid completed by backend", "{}", ct);
            return;
        }

        if (!JsonBool(instance.stateJson, "firstOrderPlaced"))
        {
            await PlaceOrdersNetFirstOrderAsync(instance, cfg, currentLevel, ct);
            return;
        }
        if (!JsonBool(instance.stateJson, "firstOrderFilled"))
        {
            await TryMarkOrdersNetFirstFillAsync(instance, cfg, currentLevel, ct);
            return;
        }
        if (!JsonBool(instance.stateJson, "hedgeOrderPlaced"))
        {
            await PlaceOrdersNetHedgeOrderAsync(instance, cfg, currentLevel, ct);
            return;
        }
        if (!JsonBool(instance.stateJson, "hedgeOrderFilled"))
        {
            await TryMarkOrdersNetHedgeFillAsync(instance, cfg, currentLevel, ct);
            var latest = await _repo.GetInstanceAsync(instance.id, ct);
            string latestStateJson = latest?.stateJson ?? instance.stateJson;
            if (JsonBool(latestStateJson, "hedgeOrderFilled")) return;
            if (currentLevel < cfg.Levels)
            {
                string nextLevelState = BuildNextLevelStateAfterHedgePlacement(latestStateJson, currentLevel + 1, cfg);
                await _repo.SetStatusAsync(instance.id, "running", nextLevelState, string.Empty, ct);
                await _repo.AppendLogAsync(instance.id, Branch(cfg) + "_L" + (currentLevel + 1) + "_Ready", "state", "Backend transition to L" + (currentLevel + 1) + " first-leg " + SideWord(cfg.FirstSide) + " after L" + currentLevel + " hedge placement", "{}", ct);
            }
            return;
        }

        if (currentLevel >= cfg.Levels)
        {
            await _repo.SetStatusAsync(instance.id, "completed", StateWithHeartbeat(StateMerge(instance.stateJson, "completed", "armed", currentLevel, null, null), "completed"), string.Empty, ct);
            await _repo.AppendLogAsync(instance.id, Branch(cfg) + "_Completed", "state", "Orders Net grid completed by backend", "{}", ct);
            return;
        }

        string nextState = BuildLevelState("ready-next-level", "armed", currentLevel + 1, cfg, null, null, null, null);
        await _repo.SetStatusAsync(instance.id, "running", nextState, string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, Branch(cfg) + "_L" + (currentLevel + 1) + "_Ready", "state", "Backend transition to L" + (currentLevel + 1) + " first-leg " + SideWord(cfg.FirstSide), "{}", ct);
    }

    private async Task<CoincallBackendSpreadBotInstance> TryMarkOrdersNetPreviousHedgeFillAsync(CoincallBackendSpreadBotInstance instance, BackendOrdersNetConfig cfg, CancellationToken ct)
    {
        var pending = ReadPendingPreviousHedges(instance.stateJson);
        if (pending.Count <= 0) return instance;

        var account = await _accounts.GetAccountByIdAsync(instance.accountId, ct);
        if (account == null) throw new InvalidOperationException("CoinCall account #" + instance.accountId + " not found");
        var client = new CoincallApiClient(account);
        using var history = cfg.HedgeLeg.IsSpot ? await client.GetSpotFillsAsync(cfg.HedgeLeg.Symbol, ct) : await client.GetFuturesTradeHistoryAsync(100, 0, 0, 0, ct);

        var filled = new List<PendingOrdersNetHedgeFill>();
        foreach (var item in pending)
        {
            var fill = FindFillForOrder(item.Order, cfg.HedgeLeg.Symbol, cfg.HedgeSideName, item.Price, cfg.Amount, item.PlacedAtUtc, history.RootElement);
            if (fill != null) filled.Add(new PendingOrdersNetHedgeFill(item, fill));
        }
        if (filled.Count <= 0) return instance;

        var rearm = filled[0];
        foreach (var item in filled)
        {
            if (item.Pending.Level < rearm.Pending.Level) rearm = item;
        }
        string nextState = StateWithPreviousHedgeFills(instance.stateJson, pending, filled, rearm);
        await _repo.SetStatusAsync(instance.id, "running", nextState, string.Empty, ct);
        foreach (var item in filled)
        {
            int previousLevel = Math.Max(1, item.Pending.Level);
            string filledState = Branch(cfg) + "_L" + previousLevel + "_" + SideWord(cfg.HedgeSide) + "LimitOrder_Filled";
            await _repo.AppendLogAsync(instance.id, filledState, "fill", "L" + previousLevel + " " + cfg.HedgeLeg.Market + " " + cfg.HedgeSideName + " previous hedge filled qty=" + item.Fill.Qty.ToString(CultureInfo.InvariantCulture) + " price=" + item.Fill.Price.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(item.Fill.ToPayload()), ct);
        }
        return await _repo.GetInstanceAsync(instance.id, ct) ?? instance;
    }

    private async Task<CoincallBackendSpreadBotInstance> ReconcileOrdersNetPreviousHedgeCompletionAsync(CoincallBackendSpreadBotInstance instance, BackendOrdersNetConfig cfg, int currentLevel, CancellationToken ct)
    {
        if (!JsonBool(instance.stateJson, "previousHedgeOrderFilled")) return instance;

        int previousLevel = Math.Max(1, JsonInt(instance.stateJson, "previousHedgeLevel", currentLevel - 1));
        if (previousLevel <= 0 || currentLevel <= previousLevel) return instance;
        if (!JsonBool(instance.stateJson, "firstOrderPlaced")) return instance;

        var latest = instance;
        if (!JsonBool(latest.stateJson, "firstOrderFilled"))
        {
            await TryMarkOrdersNetFirstFillAsync(latest, cfg, currentLevel, ct);
            latest = await _repo.GetInstanceAsync(instance.id, ct) ?? latest;
            if (JsonBool(latest.stateJson, "firstOrderFilled"))
            {
                await _repo.AppendLogAsync(instance.id, Branch(cfg) + "_L" + currentLevel + "_BuyLimitOrder_FilledBeforeStaleCancel", "fill", "L" + currentLevel + " first-leg " + SideWord(cfg.FirstSide) + " filled before stale-level cancel after L" + previousLevel + " hedge fill; continuing current level", "{}", ct);
                return latest;
            }
        }

        var account = await _accounts.GetAccountByIdAsync(instance.accountId, ct);
        if (account == null) throw new InvalidOperationException("CoinCall account #" + instance.accountId + " not found");
        var client = new CoincallApiClient(account);
        await CancelOwnedOrderIfOpenAsync(latest, client, "firstOrder", "firstOrderFilled", cfg.FirstLeg.Symbol, cfg.FirstSideName, cfg.FirstLeg.Market, ct);

        string rearmState = PreservePreviousHedgeFields(
            latest.stateJson,
            BuildLevelState("ready-rearm-level", "armed", previousLevel, cfg, null, null, null, null));
        await _repo.SetStatusAsync(instance.id, "running", rearmState, string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, Branch(cfg) + "_L" + previousLevel + "_RearmAfterPreviousHedgeFill", "state", "Previous L" + previousLevel + " hedge filled; cancelled stale L" + currentLevel + " first-leg " + SideWord(cfg.FirstSide) + " order and rearmed L" + previousLevel + " first-leg placement", "{}", ct);
        return await _repo.GetInstanceAsync(instance.id, ct) ?? latest;
    }

    private async Task PlaceOrdersNetFirstOrderAsync(CoincallBackendSpreadBotInstance instance, BackendOrdersNetConfig cfg, int level, CancellationToken ct)
    {
        string branch = Branch(cfg);
        string label = "L" + level;
        decimal price = LevelPrice(cfg, level);
        string sideWord = SideWord(cfg.FirstSide);
        string state = branch + "_" + label + "_" + sideWord + "LimitOrder_Sent";
        decimal[] levels = BuildLevels(cfg);
        await _repo.SetStatusAsync(instance.id, "running", PreservePreviousHedgeFields(instance.stateJson, BuildLevelState("placing-first", "armed", level, cfg, state, null, null, null)), string.Empty, ct);
        var levelRows = new List<string>();
        for (int i = 0; i < levels.Length; i++) levelRows.Add("L" + (i + 1) + ":level=" + levels[i].ToString(CultureInfo.InvariantCulture));
        if (level == 1)
        {
            await _repo.AppendLogAsync(instance.id, state, "params",
            "Backend Orders Net armed start pair=" + cfg.Pair + " mode=" + cfg.Mode + " first=" + cfg.FirstLeg.Symbol + "/" + sideWord + " hedge=" + cfg.HedgeLeg.Symbol + "/" + SideWord(cfg.HedgeSide) + " sell=" + cfg.SellLeg.Symbol + " buy=" + cfg.BuyLeg.Symbol +
            " amount=" + cfg.Amount.ToString(CultureInfo.InvariantCulture) + " spreadSizeUsd=" + cfg.SpreadSizeUsd.ToString(CultureInfo.InvariantCulture) +
            " l1Price=" + cfg.L1Price.ToString(CultureInfo.InvariantCulture) + " stepUsd=" + cfg.StepUsd.ToString(CultureInfo.InvariantCulture) +
            " levelTable=[" + string.Join(";", levelRows) + "]",
            JsonSerializer.Serialize(cfg.ToLogPayload()), ct);
        }

        var account = await _accounts.GetAccountByIdAsync(instance.accountId, ct);
        if (account == null) throw new InvalidOperationException("CoinCall account #" + instance.accountId + " not found");
        var client = new CoincallApiClient(account);
        var payload = OrdersNetOrderPayload(cfg.FirstLeg, cfg.FirstSideName, cfg.Amount, price);
        await _repo.AppendLogAsync(instance.id, state, "place", label + " send " + cfg.FirstLeg.Symbol + " " + sideWord + " Limit Post Only @ " + price.ToString(CultureInfo.InvariantCulture) + " amount " + cfg.Amount.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(payload), ct);
        JsonDocument res;
        try
        {
            res = cfg.FirstLeg.IsSpot ? await client.PlaceSpotOrderAsync(payload, ct) : await client.PlaceFuturesOrderAsync(payload, ct);
        }
        catch (InvalidOperationException ex) when (IsCoincallOrderExpired(ex))
        {
            string retryJson = PreservePreviousHedgeFields(instance.stateJson, BuildLevelState("ready-next-level", "armed", level, cfg, null, null, null, null));
            await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(retryJson, "ready-next-level"), string.Empty, ct);
            await _repo.AppendLogAsync(instance.id, state, "wait", label + " " + cfg.FirstLeg.Market + " " + sideWord + " Limit placement rejected by CoinCall 10540 Order has expired; backend will retry on next tick", JsonSerializer.Serialize(payload), ct);
            return;
        }
        string placedState = branch + "_" + label + "_" + sideWord + "LimitOrder_Placed_Successfully";
        string placedJson = PreservePreviousHedgeFields(instance.stateJson, BuildLevelState("first-placed", "armed", level, cfg, placedState, res.RootElement.Clone(), null, null));
        await _repo.SetStatusAsync(instance.id, "running", placedJson, string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, placedState, "place", label + " " + cfg.FirstLeg.Market + " " + sideWord + " Limit order placed successfully by backend", res.RootElement.GetRawText(), ct);
        res.Dispose();
    }

    private async Task TryMarkOrdersNetFirstFillAsync(CoincallBackendSpreadBotInstance instance, BackendOrdersNetConfig cfg, int level, CancellationToken ct)
    {
        var account = await _accounts.GetAccountByIdAsync(instance.accountId, ct);
        if (account == null) throw new InvalidOperationException("CoinCall account #" + instance.accountId + " not found");
        var client = new CoincallApiClient(account);
        using var history = cfg.FirstLeg.IsSpot ? await client.GetSpotFillsAsync(cfg.FirstLeg.Symbol, ct) : await client.GetFuturesTradeHistoryAsync(100, 0, 0, 0, ct);
        decimal expectedPrice = JsonDecimal(instance.stateJson, "firstPrice", LevelPrice(cfg, level));
        DateTime placedAt = JsonDate(instance.stateJson, "firstPlacedAtUtc") ?? DateTime.UtcNow.AddMinutes(-10);
        var fill = FindFill(instance.stateJson, "firstOrder", cfg.FirstLeg.Symbol, cfg.FirstSideName, expectedPrice, cfg.Amount, placedAt, history.RootElement);
        string branch = Branch(cfg);
        string label = "L" + level;
        string sideWord = SideWord(cfg.FirstSide);
        if (fill == null)
        {
            string waitState = branch + "_" + label + "_" + sideWord + "LimitOrder_Placed_Successfully";
            await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "first-placed"), string.Empty, ct);
            if (ShouldLogWait(instance.stateJson))
                await _repo.AppendLogAsync(instance.id, waitState, "wait", label + " " + cfg.FirstLeg.Market + " " + sideWord + " Limit not filled yet; backend waiting for trade history/private fill", "{}", ct);
            if (string.Equals(cfg.L1Mode, "trail", StringComparison.OrdinalIgnoreCase))
                await TryTrailFirstOrderAsync(instance, cfg, level, ct);
            return;
        }

        string filledState = branch + "_" + label + "_" + sideWord + "LimitOrder_Filled";
        await _repo.SetStatusAsync(instance.id, "running", StateMerge(instance.stateJson, "first-filled", "armed", level, fill, null), string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, filledState, "fill", label + " " + cfg.FirstLeg.Market + " " + cfg.FirstSideName + " filled qty=" + fill.Qty.ToString(CultureInfo.InvariantCulture) + " price=" + fill.Price.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(fill.ToPayload()), ct);
    }

    private async Task TryTrailFirstOrderAsync(CoincallBackendSpreadBotInstance instance, BackendOrdersNetConfig cfg, int level, CancellationToken ct)
    {
        decimal current = JsonDecimal(instance.stateJson, "firstPrice", LevelPrice(cfg, level));
        if (current <= 0) return;
        var account = await _accounts.GetAccountByIdAsync(instance.accountId, ct);
        if (account == null) throw new InvalidOperationException("CoinCall account #" + instance.accountId + " not found");
        var client = new CoincallApiClient(account);
        var top = await GetOrdersNetTrailBidAskAsync(client, cfg.FirstLeg, ct);
        if (top == null)
        {
            if (ShouldLogWait(instance.stateJson))
            {
                await _repo.AppendLogAsync(instance.id, Branch(cfg) + "_L" + level + "_" + SideWord(cfg.FirstSide) + "LimitOrder_L1_Trail_Wait", "wait", "L" + level + " first " + SideWord(cfg.FirstSide) + " Limit trail BidAskMedian source unavailable or invalid; backend keeps active order and will retry", "{}", ct);
            }
            return;
        }
        decimal median = (top.Value.Bid + top.Value.Ask) / 2m;
        decimal target = RoundTrailPrice(median, cfg.FirstSide);
        bool move = cfg.FirstSide == "1" ? target >= current + Math.Max(cfg.L1TrailSizeUsd, 0m) : target <= current - Math.Max(cfg.L1TrailSizeUsd, 0m);
        if (!move || target <= 0 || target == current) return;

        string branch = Branch(cfg);
        string label = "L" + level;
        string sideWord = SideWord(cfg.FirstSide);
        string state = branch + "_" + label + "_" + sideWord + "LimitOrder_L1_Trail_Update";
        await _repo.AppendLogAsync(instance.id, state, "trail", label + " first " + sideWord + " Limit trail candidate current=" + current.ToString(CultureInfo.InvariantCulture) + " liveBid=" + top.Value.Bid.ToString(CultureInfo.InvariantCulture) + " liveAsk=" + top.Value.Ask.ToString(CultureInfo.InvariantCulture) + " target=" + target.ToString(CultureInfo.InvariantCulture), "{}", ct);

        bool postOnlySafe = cfg.FirstSide == "1" ? target < top.Value.Ask : target > top.Value.Bid;
        if (!postOnlySafe)
        {
            await _repo.AppendLogAsync(instance.id, state, "wait", label + " first " + sideWord + " Limit trail target " + target.ToString(CultureInfo.InvariantCulture) + " is not post-only safe against live bid=" + top.Value.Bid.ToString(CultureInfo.InvariantCulture) + " ask=" + top.Value.Ask.ToString(CultureInfo.InvariantCulture) + "; backend keeps existing order and will retry", "{}", ct);
            return;
        }

        JsonElement firstOrder = JsonObject(instance.stateJson, "firstOrder");
        if (firstOrder.ValueKind == JsonValueKind.Object)
        {
            using var cancelRes = cfg.FirstLeg.IsSpot ? await client.CancelSpotOrderAsync(firstOrder.Clone(), ct) : await client.CancelFuturesOrderAsync(firstOrder.Clone(), ct);
            await _repo.AppendLogAsync(instance.id, state, "cancel", "Cancel " + cfg.FirstLeg.Symbol + " " + sideWord + " Limit for backend L1 trail update", cancelRes.RootElement.GetRawText(), ct);
        }

        var payload = OrdersNetOrderPayload(cfg.FirstLeg, cfg.FirstSideName, cfg.Amount, target);
        await _repo.AppendLogAsync(instance.id, state, "place", label + " move " + cfg.FirstLeg.Symbol + " " + sideWord + " Limit Post Only from " + current.ToString(CultureInfo.InvariantCulture) + " to " + target.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(payload), ct);
        JsonDocument res;
        try
        {
            res = cfg.FirstLeg.IsSpot ? await client.PlaceSpotOrderAsync(payload, ct) : await client.PlaceFuturesOrderAsync(payload, ct);
        }
        catch (InvalidOperationException ex) when (IsCoincallOrderExpired(ex))
        {
            await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "first-placed"), string.Empty, ct);
            await _repo.AppendLogAsync(instance.id, state, "wait", label + " first " + sideWord + " Limit trail replacement rejected by CoinCall 10540 Order has expired; backend will retry trail/placement checks on next tick", JsonSerializer.Serialize(payload), ct);
            return;
        }
        string placedState = branch + "_" + label + "_" + sideWord + "LimitOrder_Placed_Successfully";
        string placedJson = BuildLevelState("first-placed", "armed", level, cfg, placedState, res.RootElement.Clone(), null, null, target);
        await _repo.SetStatusAsync(instance.id, "running", placedJson, string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, placedState, "trail", label + " " + cfg.FirstLeg.Market + " " + sideWord + " Limit moved from " + current.ToString(CultureInfo.InvariantCulture) + " to " + target.ToString(CultureInfo.InvariantCulture), res.RootElement.GetRawText(), ct);
        res.Dispose();
    }

    private async Task PlaceOrdersNetHedgeOrderAsync(CoincallBackendSpreadBotInstance instance, BackendOrdersNetConfig cfg, int level, CancellationToken ct)
    {
        var fill = ReadFill(instance.stateJson, "firstFill");
        if (fill == null) throw new InvalidOperationException("First-leg fill is required before hedge placement");
        decimal hedgePrice = HedgePriceFromFirstFill(cfg, fill.Price);
        if (hedgePrice <= 0) throw new InvalidOperationException("Computed hedge price is invalid");
        var account = await _accounts.GetAccountByIdAsync(instance.accountId, ct);
        if (account == null) throw new InvalidOperationException("CoinCall account #" + instance.accountId + " not found");
        var client = new CoincallApiClient(account);
        var payload = OrdersNetOrderPayload(cfg.HedgeLeg, cfg.HedgeSideName, cfg.Amount, hedgePrice);
        string branch = Branch(cfg);
        string label = "L" + level;
        string sideWord = SideWord(cfg.HedgeSide);
        string sentState = branch + "_" + label + "_" + sideWord + "LimitOrder_Sent";
        await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "placing-hedge"), string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, sentState, "price", label + " hedge price = " + HedgeFormula(cfg) + " = " + hedgePrice.ToString(CultureInfo.InvariantCulture), "{}", ct);
        await _repo.AppendLogAsync(instance.id, sentState, "place", label + " send " + cfg.HedgeLeg.Symbol + " " + sideWord + " Limit Post Only @ " + payload["price"] + " amount " + payload["qty"], JsonSerializer.Serialize(payload), ct);
        JsonDocument res;
        try
        {
            res = cfg.HedgeLeg.IsSpot ? await client.PlaceSpotOrderAsync(payload, ct) : await client.PlaceFuturesOrderAsync(payload, ct);
        }
        catch (InvalidOperationException ex) when (IsCoincallOrderExpired(ex))
        {
            await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "first-filled"), string.Empty, ct);
            await _repo.AppendLogAsync(instance.id, sentState, "wait", label + " " + cfg.HedgeLeg.Market + " " + sideWord + " Limit hedge placement rejected by CoinCall 10540 Order has expired; backend will retry on next tick", JsonSerializer.Serialize(payload), ct);
            return;
        }
        string placedState = branch + "_" + label + "_" + sideWord + "LimitOrder_Placed_Successfully";
        string placedJson = StateMerge(instance.stateJson, "hedge-placed", "armed", level, null, res.RootElement.Clone(), hedgePrice, cfg);
        await _repo.SetStatusAsync(instance.id, "running", placedJson, string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, placedState, "place", label + " " + cfg.HedgeLeg.Market + " " + sideWord + " Limit hedge order placed successfully by backend", res.RootElement.GetRawText(), ct);
        res.Dispose();
    }

    private async Task TryMarkOrdersNetHedgeFillAsync(CoincallBackendSpreadBotInstance instance, BackendOrdersNetConfig cfg, int level, CancellationToken ct)
    {
        var account = await _accounts.GetAccountByIdAsync(instance.accountId, ct);
        if (account == null) throw new InvalidOperationException("CoinCall account #" + instance.accountId + " not found");
        var client = new CoincallApiClient(account);
        using var history = cfg.HedgeLeg.IsSpot ? await client.GetSpotFillsAsync(cfg.HedgeLeg.Symbol, ct) : await client.GetFuturesTradeHistoryAsync(100, 0, 0, 0, ct);
        decimal expectedPrice = JsonDecimal(instance.stateJson, "hedgePrice", 0m);
        DateTime placedAt = JsonDate(instance.stateJson, "hedgePlacedAtUtc") ?? DateTime.UtcNow.AddMinutes(-10);
        var fill = FindFill(instance.stateJson, "hedgeOrder", cfg.HedgeLeg.Symbol, cfg.HedgeSideName, expectedPrice, cfg.Amount, placedAt, history.RootElement);
        string branch = Branch(cfg);
        string label = "L" + level;
        string sideWord = SideWord(cfg.HedgeSide);
        if (fill == null)
        {
            string waitState = branch + "_" + label + "_" + sideWord + "LimitOrder_Placed_Successfully";
            await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "hedge-placed"), string.Empty, ct);
            if (ShouldLogWait(instance.stateJson))
                await _repo.AppendLogAsync(instance.id, waitState, "wait", label + " " + cfg.HedgeLeg.Market + " " + sideWord + " Limit hedge not filled yet; backend waiting for trade history/private fill", "{}", ct);
            return;
        }

        string filledState = branch + "_" + label + "_" + sideWord + "LimitOrder_Filled";
        await _repo.SetStatusAsync(instance.id, "running", StateMerge(instance.stateJson, "hedge-filled", "armed", level, null, fill), string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, filledState, "fill", label + " " + cfg.HedgeLeg.Market + " " + cfg.HedgeSideName + " hedge filled qty=" + fill.Qty.ToString(CultureInfo.InvariantCulture) + " price=" + fill.Price.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(fill.ToPayload()), ct);
    }

    private async Task RunLimitLimitCycleAsync(CoincallBackendSpreadBotInstance instance, BackendGenericBotConfig cfg, CancellationToken ct)
    {
        if (cfg.Amount <= 0) throw new InvalidOperationException("Amount must be positive");
        if (!JsonBool(instance.stateJson, "firstOrderPlaced"))
        {
            await PlaceGenericFirstOrderAsync(instance, cfg, ct);
            return;
        }
        if (!JsonBool(instance.stateJson, "firstOrderFilled"))
        {
            await TryMarkGenericFirstFillAsync(instance, cfg, ct);
            return;
        }
        if (!JsonBool(instance.stateJson, "hedgeOrderPlaced"))
        {
            await PlaceGenericHedgeOrderAsync(instance, cfg, ct);
            return;
        }
        if (!JsonBool(instance.stateJson, "hedgeOrderFilled"))
        {
            await TryMarkGenericHedgeFillAsync(instance, cfg, ct);
            return;
        }
        await _repo.SetStatusAsync(instance.id, "completed", StateWithHeartbeat(instance.stateJson, "completed"), string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, "SpreadBot_LimitLimit_Completed", "complete", "Limit/Limit backend cycle completed", "{}", ct);
    }

    private async Task PlaceGenericFirstOrderAsync(CoincallBackendSpreadBotInstance instance, BackendGenericBotConfig cfg, CancellationToken ct)
    {
        decimal price = await GenericBestPriceAsync(instance.accountId, cfg.FirstLeg, cfg.FirstSide, ct);
        var order = await PlaceGenericOrderAsync(instance.accountId, cfg.FirstLeg, cfg.FirstSide, cfg.Amount, price, ct);
        await _repo.SetStatusAsync(instance.id, "running", BuildGenericState("first-placed", "armed", cfg, order.RootElement.Clone(), null, null, null, price), string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, "SpreadBot_LimitLimit_FirstOrder_Placed", "place", "Backend placed first " + cfg.FirstLeg.Symbol + " " + cfg.FirstSide + " limit @ " + price.ToString(CultureInfo.InvariantCulture), order.RootElement.GetRawText(), ct);
        order.Dispose();
    }

    private async Task TryMarkGenericFirstFillAsync(CoincallBackendSpreadBotInstance instance, BackendGenericBotConfig cfg, CancellationToken ct)
    {
        var fill = await FindGenericFillAsync(instance.accountId, instance.stateJson, "firstOrder", cfg.FirstLeg, cfg.FirstSide, JsonDecimal(instance.stateJson, "firstPrice", 0m), cfg.Amount, JsonDate(instance.stateJson, "firstPlacedAtUtc") ?? DateTime.UtcNow.AddMinutes(-10), ct);
        if (fill == null)
        {
            await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "first-placed"), string.Empty, ct);
            if (ShouldLogWait(instance.stateJson)) await _repo.AppendLogAsync(instance.id, "SpreadBot_LimitLimit_FirstOrder_Wait", "wait", "Backend waiting for first-leg fill", "{}", ct);
            return;
        }
        await _repo.SetStatusAsync(instance.id, "running", StateMergeGeneric(instance.stateJson, "first-filled", cfg, firstFill: fill), string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, "SpreadBot_LimitLimit_FirstOrder_Filled", "fill", "First leg filled qty=" + fill.Qty.ToString(CultureInfo.InvariantCulture) + " price=" + fill.Price.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(fill.ToPayload()), ct);
    }

    private async Task PlaceGenericHedgeOrderAsync(CoincallBackendSpreadBotInstance instance, BackendGenericBotConfig cfg, CancellationToken ct)
    {
        var firstFill = ReadFill(instance.stateJson, "firstFill");
        decimal qty = firstFill?.Qty > 0 ? Math.Min(firstFill.Qty, cfg.Amount) : cfg.Amount;
        decimal price = await GenericBestPriceAsync(instance.accountId, cfg.HedgeLeg, cfg.HedgeSide, ct);
        var order = await PlaceGenericOrderAsync(instance.accountId, cfg.HedgeLeg, cfg.HedgeSide, qty, price, ct);
        await _repo.SetStatusAsync(instance.id, "running", StateMergeGeneric(instance.stateJson, "hedge-placed", cfg, hedgeOrder: order.RootElement.Clone(), hedgePrice: price), string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, "SpreadBot_LimitLimit_HedgeOrder_Placed", "place", "Backend placed hedge " + cfg.HedgeLeg.Symbol + " " + cfg.HedgeSide + " limit @ " + price.ToString(CultureInfo.InvariantCulture), order.RootElement.GetRawText(), ct);
        order.Dispose();
    }

    private async Task TryMarkGenericHedgeFillAsync(CoincallBackendSpreadBotInstance instance, BackendGenericBotConfig cfg, CancellationToken ct)
    {
        decimal qty = ReadFill(instance.stateJson, "firstFill")?.Qty ?? cfg.Amount;
        var fill = await FindGenericFillAsync(instance.accountId, instance.stateJson, "hedgeOrder", cfg.HedgeLeg, cfg.HedgeSide, JsonDecimal(instance.stateJson, "hedgePrice", 0m), qty, JsonDate(instance.stateJson, "hedgePlacedAtUtc") ?? DateTime.UtcNow.AddMinutes(-10), ct);
        if (fill == null)
        {
            await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "hedge-placed"), string.Empty, ct);
            if (ShouldLogWait(instance.stateJson)) await _repo.AppendLogAsync(instance.id, "SpreadBot_LimitLimit_HedgeOrder_Wait", "wait", "Backend waiting for hedge fill", "{}", ct);
            return;
        }
        await _repo.SetStatusAsync(instance.id, "running", StateMergeGeneric(instance.stateJson, "hedge-filled", cfg, hedgeFill: fill), string.Empty, ct);
        await _repo.AppendLogAsync(instance.id, "SpreadBot_LimitLimit_HedgeOrder_Filled", "fill", "Hedge filled qty=" + fill.Qty.ToString(CultureInfo.InvariantCulture) + " price=" + fill.Price.ToString(CultureInfo.InvariantCulture), JsonSerializer.Serialize(fill.ToPayload()), ct);
    }

    private async Task RunDdhCycleAsync(CoincallBackendSpreadBotInstance instance, BackendGenericBotConfig cfg, CancellationToken ct)
    {
        if (cfg.Amount <= 0) throw new InvalidOperationException("Amount must be positive");
        if (!JsonBool(instance.stateJson, "activeOrderPlaced"))
        {
            bool useCachedDelta = JsonBool(instance.stateJson, "ddhUseCachedDelta");
            Task<decimal>? deltaTask = useCachedDelta ? null : GetTotalDeltaAsync(instance.accountId, cfg.Asset, ct);
            var shortMedianTask = DdhLegBidAskMedianAsync(instance.accountId, cfg.ShortLeg, ct);
            var longMedianTask = DdhLegBidAskMedianAsync(instance.accountId, cfg.LongLeg, ct);
            if (deltaTask != null) await Task.WhenAll(deltaTask, shortMedianTask, longMedianTask);
            else await Task.WhenAll(shortMedianTask, longMedianTask);
            decimal delta = deltaTask != null ? await deltaTask : JsonDecimal(instance.stateJson, "delta", 0m);
            var shortMedian = await shortMedianTask;
            var longMedian = await longMedianTask;
            if (shortMedian == null || longMedian == null)
            {
                await _repo.SetStatusAsync(instance.id, "running", BuildDdhReadyState(cfg, delta, JsonBool(instance.stateJson, "ddhStartLogged"), useCachedDelta: true), string.Empty, ct);
                if (ShouldLogWait(instance.stateJson))
                {
                    await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_BidAskMedian_Unavailable", "wait", "DDH live BidAskMedian unavailable; shortLeg=" + cfg.ShortLeg.Symbol + " median=" + FmtPrice(shortMedian) + "; longLeg=" + cfg.LongLeg.Symbol + " median=" + FmtPrice(longMedian) + "; backend will retry when fresh data returns", "{}", ct);
                }
                return;
            }
            bool startLogged = JsonBool(instance.stateJson, "ddhStartLogged");
            if (!startLogged)
            {
                await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Start_Params", "start", "DDH start params: pair=" + cfg.Pair + "; asset=" + cfg.Asset + "; amount=" + cfg.Amount.ToString(CultureInfo.InvariantCulture) + "; l1TrailSizeUsd=" + cfg.L1TrailSizeUsd.ToString(CultureInfo.InvariantCulture) + "; shortLeg=" + cfg.ShortLeg.Symbol + " (" + cfg.ShortLeg.Market + ") BidAskMedian=" + FmtPrice(shortMedian.Value) + "; longLeg=" + cfg.LongLeg.Symbol + " (" + cfg.LongLeg.Market + ") BidAskMedian=" + FmtPrice(longMedian.Value) + "; totalDelta=" + delta.ToString(CultureInfo.InvariantCulture), "{}", ct);
                startLogged = true;
            }
            if (Math.Abs(delta) <= 0.000000000001m)
            {
                await _repo.SetStatusAsync(instance.id, "running", BuildDdhIdleState(cfg, delta, startLogged), string.Empty, ct);
                if (ShouldLogWait(instance.stateJson)) await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_DeltaFlat", "delta", "DDH total delta is flat; backend waiting", "{}", ct);
                return;
            }
            var leg = delta > 0 ? cfg.ShortLeg : cfg.LongLeg;
            string side = delta > 0 ? "sell" : "buy";
            decimal selectedMedian = delta > 0 ? shortMedian.Value : longMedian.Value;
            decimal price = DdhInitialOrderPrice(selectedMedian, side, cfg.L1TrailSizeUsd);
            var openGuard = await FindDdhBlockingOpenOrderAsync(instance.accountId, cfg, ct);
            if (openGuard.HasValue)
            {
                string readyState = BuildDdhReadyState(cfg, delta, startLogged, useCachedDelta: true);
                await _repo.SetStatusAsync(instance.id, "running", readyState, string.Empty, ct);
                await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Open_Order_Guard", "wait", "DDH found existing open " + openGuard.Value.Leg.Symbol + " " + openGuard.Value.Side + " order matching DDH amount before placing new " + leg.Symbol + " " + side + "; backend will wait and recheck instead of creating a second order", openGuard.Value.Order.GetRawText(), ct);
                return;
            }
            ct.ThrowIfCancellationRequested();
            JsonDocument order;
            try
            {
                order = await PlaceGenericOrderAsync(instance.accountId, leg, side, cfg.Amount, price, ct);
            }
            catch (InvalidOperationException ex) when (IsCoincallOrderExpired(ex))
            {
                await _repo.SetStatusAsync(instance.id, "running", BuildDdhReadyState(cfg, delta, startLogged), string.Empty, ct);
                await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Order_Place_Retry", "wait", "DDH " + leg.Symbol + " " + side + " @ " + FmtPrice(price) + " rejected by CoinCall 10540 Order has expired; backend will recalc delta and retry on next tick", "{}", ct);
                return;
            }
            await _repo.SetStatusAsync(instance.id, "running", BuildDdhActiveState(cfg, delta, leg, side, order.RootElement.Clone(), price, selectedMedian, startLogged), string.Empty, ct);
            await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Order_Placed", "place", "DDH backend delta=" + delta.ToString(CultureInfo.InvariantCulture) + "; " + leg.Symbol + " BidAskMedian=" + FmtPrice(selectedMedian) + "; placed " + leg.Symbol + " " + side + " @ " + FmtPrice(price), order.RootElement.GetRawText(), ct);
            order.Dispose();
            return;
        }

        var activeLeg = new BackendGenericLeg(JsonText(instance.stateJson, "activeLeg", ""), JsonText(instance.stateJson, "activeMarket", "futures"));
        string activeSide = JsonText(instance.stateJson, "activeSide", "");
        var fill = await FindGenericFillAsync(instance.accountId, instance.stateJson, "activeOrder", activeLeg, activeSide, JsonDecimal(instance.stateJson, "activePrice", 0m), cfg.Amount, JsonDate(instance.stateJson, "activePlacedAtUtc") ?? DateTime.UtcNow.AddMinutes(-10), ct);
        if (fill != null)
        {
            string filledState = BuildDdhFilledState(instance.stateJson, fill, activeSide);
            await _repo.SetStatusAsync(instance.id, "running", filledState, string.Empty, ct);
            await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Order_Filled", "fill", "DDH order filled; backend will restart delta check immediately", JsonSerializer.Serialize(fill.ToPayload()), ct);
            await RunDdhCycleAsync(instance with { status = "running", stateJson = filledState }, cfg, ct);
            return;
        }

        decimal? median = await DdhLegBidAskMedianAsync(instance.accountId, activeLeg, ct);
        if (median == null)
        {
            await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "active"), string.Empty, ct);
            if (ShouldLogWait(instance.stateJson))
            {
                await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_BidAskMedian_Unavailable", "wait", "DDH active " + activeLeg.Symbol + " live BidAskMedian unavailable; backend keeps active order and will retry when fresh data returns", "{}", ct);
            }
            return;
        }
        decimal current = JsonDecimal(instance.stateJson, "activePrice", 0m);
        decimal trailSize = Math.Max(cfg.L1TrailSizeUsd, 0m);
        decimal restartDistance = trailSize + 10m;
        bool restart = Math.Abs(median.Value - current) >= restartDistance;
        if (restart)
        {
            JsonElement order = JsonObject(instance.stateJson, "activeOrder");
            if (order.ValueKind == JsonValueKind.Object)
            {
                var account = await _accounts.GetAccountByIdAsync(instance.accountId, ct) ?? throw new InvalidOperationException("CoinCall account #" + instance.accountId + " not found");
                var client = new CoincallApiClient(account);
                try
                {
                    using var cancel = activeLeg.IsSpot ? await client.CancelSpotOrderAsync(order.Clone(), ct) : await client.CancelFuturesOrderAsync(order.Clone(), ct);
                    await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Order_Restart_Cancel", "cancel", "DDH canceled old " + activeLeg.Symbol + " " + activeSide + " @ " + FmtPrice(current) + "; current BidAskMedian=" + FmtPrice(median.Value) + "; distance=" + FmtPrice(Math.Abs(median.Value - current)) + " >= L1TrailSize+10=" + FmtPrice(restartDistance) + "; backend will restart immediately", cancel.RootElement.GetRawText(), ct);
                }
                catch (InvalidOperationException ex) when (IsCoincallCancelFailed(ex))
                {
                    if (await IsGenericOrderStillOpenAsync(instance.accountId, instance.stateJson, "activeOrder", activeLeg, activeSide, ct))
                    {
                        await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "active"), string.Empty, ct);
                        await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Order_Restart_Cancel_Still_Open", "wait", "DDH cancel returned CoinCall 10537 for " + activeLeg.Symbol + " " + activeSide + " @ " + FmtPrice(current) + ", but order is still in open orders; backend keeps active order and will retry checks", "{}", ct);
                        return;
                    }
                    string freshReadyState = BuildDdhReadyState(cfg, JsonDecimal(instance.stateJson, "delta", 0m), false, useCachedDelta: false);
                    await _repo.SetStatusAsync(instance.id, "running", freshReadyState, string.Empty, ct);
                    await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Order_Restart_Cancel_Stale", "wait", "DDH cancel returned CoinCall 10537 for " + activeLeg.Symbol + " " + activeSide + " @ " + FmtPrice(current) + "; order is not in open orders; backend clears active order and restarts with fresh delta", "{}", ct);
                    await RunDdhCycleAsync(instance with { status = "running", stateJson = freshReadyState }, cfg, ct);
                    return;
                }
            }
            string readyState = BuildDdhReadyState(cfg, JsonDecimal(instance.stateJson, "delta", 0m), false, useCachedDelta: true);
            await _repo.SetStatusAsync(instance.id, "running", readyState, string.Empty, ct);
            await RunDdhCycleAsync(instance with { status = "running", stateJson = readyState }, cfg, ct);
            return;
        }

        await _repo.SetStatusAsync(instance.id, "running", StateWithHeartbeat(instance.stateJson, "active"), string.Empty, ct);
    }

    private async Task StopDdhOnProblemAsync(CoincallBackendSpreadBotInstance instance, string message, CancellationToken ct)
    {
        await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Stopped_Problem", "error", message, "{}", ct);
        try
        {
            await CancelOwnedOpenOrdersAsync(instance, ct);
        }
        catch (Exception ex)
        {
            await _repo.AppendLogAsync(instance.id, "SpreadBot_DDH_Stop_Cancel_Failed", "error", "DDH stop requested but owned-order cancellation failed: " + ex.Message, "{}", ct);
        }
        await _repo.SetStatusAsync(instance.id, "stopped", JsonSerializer.Serialize(new { phase = "stopped-problem", execution = "stopped", error = message, stoppedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) }), message, ct);
        if (_running.TryRemove(instance.id, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    private async Task<JsonDocument> PlaceGenericOrderAsync(int accountId, BackendGenericLeg leg, string side, decimal qty, decimal price, CancellationToken ct)
    {
        var account = await _accounts.GetAccountByIdAsync(accountId, ct) ?? throw new InvalidOperationException("CoinCall account #" + accountId + " not found");
        var client = new CoincallApiClient(account);
        if (leg.IsSpot)
        {
            var payload = new Dictionary<string, string?> { ["symbol"] = leg.Symbol, ["tradeSide"] = side == "sell" ? "SELL" : "BUY", ["tradeType"] = "POST_ONLY", ["qty"] = qty.ToString(CultureInfo.InvariantCulture), ["price"] = price.ToString(CultureInfo.InvariantCulture) };
            return await client.PlaceSpotOrderAsync(payload, ct);
        }
        else
        {
            var payload = new Dictionary<string, string?> { ["symbol"] = ApiSymbol(leg), ["tradeSide"] = side == "sell" ? "2" : "1", ["tradeType"] = "3", ["qty"] = qty.ToString(CultureInfo.InvariantCulture), ["price"] = price.ToString(CultureInfo.InvariantCulture) };
            return await client.PlaceFuturesOrderAsync(payload, ct);
        }
    }

    private static Dictionary<string, string?> OrdersNetOrderPayload(BackendOrdersNetLeg leg, string side, decimal qty, decimal price)
    {
        if (leg.IsSpot)
        {
            return new Dictionary<string, string?>
            {
                ["symbol"] = leg.Symbol,
                ["tradeSide"] = side == "sell" ? "SELL" : "BUY",
                ["tradeType"] = "POST_ONLY",
                ["qty"] = qty.ToString(CultureInfo.InvariantCulture),
                ["price"] = price.ToString(CultureInfo.InvariantCulture)
            };
        }
        return new Dictionary<string, string?>
        {
            ["symbol"] = ApiSymbol(leg),
            ["tradeSide"] = side == "sell" ? "2" : "1",
            ["tradeType"] = "3",
            ["qty"] = qty.ToString(CultureInfo.InvariantCulture),
            ["price"] = price.ToString(CultureInfo.InvariantCulture)
        };
    }

    private async Task<BackendOrdersNetFill?> FindGenericFillAsync(int accountId, string stateJson, string orderProperty, BackendGenericLeg leg, string side, decimal price, decimal qty, DateTime placedAt, CancellationToken ct)
    {
        var account = await _accounts.GetAccountByIdAsync(accountId, ct) ?? throw new InvalidOperationException("CoinCall account #" + accountId + " not found");
        var client = new CoincallApiClient(account);
        using var history = leg.IsSpot ? await client.GetSpotFillsAsync(leg.Symbol, ct) : await client.GetFuturesTradeHistoryAsync(100, 0, 0, 0, ct);
        return FindFill(stateJson, orderProperty, leg.Symbol, side, price, qty, placedAt, history.RootElement);
    }

    private async Task<bool> IsGenericOrderStillOpenAsync(int accountId, string stateJson, string orderProperty, BackendGenericLeg leg, string side, CancellationToken ct)
    {
        string orderId = JsonDeepTextIn(stateJson, orderProperty, "orderId", "ordId", "order_id", "id", "oid");
        string clientOrderId = JsonDeepTextIn(stateJson, orderProperty, "clientOrderId", "clientOid", "clOrdId", "coid");
        if (string.IsNullOrWhiteSpace(orderId) && string.IsNullOrWhiteSpace(clientOrderId)) return false;
        var account = await _accounts.GetAccountByIdAsync(accountId, ct) ?? throw new InvalidOperationException("CoinCall account #" + accountId + " not found");
        var client = new CoincallApiClient(account);
        using var open = leg.IsSpot ? await client.GetSpotOpenOrdersAsync(leg.Symbol, ct) : await client.GetFuturesOpenOrdersAsync(ApiSymbol(leg), ct);
        var rows = new List<JsonElement>();
        CollectRows(open.RootElement, rows);
        foreach (var row in rows)
        {
            string rowSymbol = ReadText(row, "symbol", "displaySymbol", "displayName", "instId", "instrument", "s");
            if (!string.IsNullOrWhiteSpace(rowSymbol) && !SymbolMatches(rowSymbol, leg.Symbol)) continue;
            string rowSide = ReadSide(row);
            if (!string.IsNullOrWhiteSpace(rowSide) && !string.IsNullOrWhiteSpace(side) && rowSide != side) continue;
            string rowOrderId = ReadText(row, "orderId", "ordId", "order_id", "id", "oid");
            string rowClientId = ReadText(row, "clientOrderId", "clientOid", "clOrdId", "coid");
            if ((!string.IsNullOrWhiteSpace(orderId) && orderId == rowOrderId) || (!string.IsNullOrWhiteSpace(clientOrderId) && clientOrderId == rowClientId)) return true;
        }
        return false;
    }

    private async Task<(BackendGenericLeg Leg, string Side, JsonElement Order)?> FindDdhBlockingOpenOrderAsync(int accountId, BackendGenericBotConfig cfg, CancellationToken ct)
    {
        var candidates = new (BackendGenericLeg Leg, string Side)[] { (cfg.ShortLeg, "sell"), (cfg.LongLeg, "buy") };
        foreach (var candidate in candidates)
        {
            var order = await FindOpenOrderByLegSideQtyAsync(accountId, candidate.Leg, candidate.Side, cfg.Amount, ct);
            if (order.HasValue) return (candidate.Leg, candidate.Side, order.Value);
        }
        return null;
    }

    private async Task<JsonElement?> FindOpenOrderByLegSideQtyAsync(int accountId, BackendGenericLeg leg, string side, decimal qty, CancellationToken ct)
    {
        var account = await _accounts.GetAccountByIdAsync(accountId, ct) ?? throw new InvalidOperationException("CoinCall account #" + accountId + " not found");
        var client = new CoincallApiClient(account);
        using var open = leg.IsSpot ? await client.GetSpotOpenOrdersAsync(leg.Symbol, ct) : await client.GetFuturesOpenOrdersAsync(ApiSymbol(leg), ct);
        var rows = new List<JsonElement>();
        CollectRows(open.RootElement, rows);
        foreach (var row in rows)
        {
            string rowSymbol = ReadText(row, "symbol", "displaySymbol", "displayName", "instId", "instrument", "s");
            if (!string.IsNullOrWhiteSpace(rowSymbol) && !SymbolMatches(rowSymbol, leg.Symbol)) continue;
            string rowSide = ReadSide(row);
            if (!string.IsNullOrWhiteSpace(rowSide) && !string.IsNullOrWhiteSpace(side) && rowSide != side) continue;
            decimal rowQty = ReadDecimal(row, "qty", "quantity", "orderQty", "orderQuantity", "size", "amount", "origQty", "baseQty");
            if (rowQty > 0m && qty > 0m && Math.Abs(rowQty - qty) > 0.00000001m) continue;
            return row.Clone();
        }
        return null;
    }

    private async Task<decimal> GenericBestPriceAsync(int accountId, BackendGenericLeg leg, string side, CancellationToken ct)
    {
        var account = await _accounts.GetAccountByIdAsync(accountId, ct) ?? throw new InvalidOperationException("CoinCall account #" + accountId + " not found");
        var client = new CoincallApiClient(account);
        using var book = leg.IsSpot ? await client.GetSpotOrderBookAsync(leg.Symbol, ct) : await client.GetFuturesOrderBookAsync(ApiSymbol(leg), ct);
        var top = ReadOrderBookTop(book.RootElement);
        if (top == null) throw new InvalidOperationException(leg.Symbol + " order book is unavailable");
        return side == "sell" ? top.Value.Ask : top.Value.Bid;
    }

    private async Task<decimal> DdhTargetPriceAsync(int accountId, BackendGenericLeg leg, string side, CancellationToken ct)
    {
        decimal? median = await DdhLegBidAskMedianAsync(accountId, leg, ct);
        if (median == null) throw new InvalidOperationException(leg.Symbol + " BidAskMedian source is unavailable");
        return DdhInitialOrderPrice(median.Value, side, 0m);
    }

    private async Task<decimal?> DdhLegBidAskMedianAsync(int accountId, BackendGenericLeg leg, CancellationToken ct)
    {
        (decimal Bid, decimal Ask)? top;
        if (leg.IsSpot)
        {
            var account = await _accounts.GetAccountByIdAsync(accountId, ct) ?? throw new InvalidOperationException("CoinCall account #" + accountId + " not found");
            var client = new CoincallApiClient(account);
            using var book = await client.GetSpotOrderBookAsync(leg.Symbol, ct);
            top = ReadOrderBookTop(book.RootElement);
        }
        else
        {
            top = await GetDdhFuturesBidAskAsync(leg.Symbol, ct);
        }
        if (top == null) return null;
        return (top.Value.Bid + top.Value.Ask) / 2m;
    }

    private static decimal DdhInitialOrderPrice(decimal bidAskMedian, string side, decimal l1TrailSizeUsd)
    {
        decimal trail = Math.Max(l1TrailSizeUsd, 0m);
        decimal raw = string.Equals(side, "sell", StringComparison.OrdinalIgnoreCase)
            ? bidAskMedian + trail
            : bidAskMedian - trail;
        decimal rounded = string.Equals(side, "sell", StringComparison.OrdinalIgnoreCase)
            ? Math.Ceiling(raw / 10m) * 10m
            : Math.Floor(raw / 10m) * 10m;
        return Math.Max(rounded, 0m);
    }

    private static string FmtPrice(decimal? value) => value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) : "unavailable";

    private async Task<(decimal Bid, decimal Ask)?> GetDdhFuturesBidAskAsync(string symbol, CancellationToken ct)
    {
        string apiSymbol = FuturesApiSymbol(symbol);
        var live = _candles.TryGetLatestLiveFuturesBidAsk(apiSymbol, TimeSpan.FromSeconds(30));
        if (live.HasValue && live.Value.Ask - live.Value.Bid <= 500m) return (live.Value.Bid, live.Value.Ask);
        return null;
    }

    private async Task<(decimal Bid, decimal Ask)?> GetOrdersNetTrailBidAskAsync(CoincallApiClient client, BackendOrdersNetLeg leg, CancellationToken ct)
    {
        (decimal Bid, decimal Ask)? top;
        if (leg.IsSpot)
        {
            using var book = await client.GetSpotOrderBookAsync(leg.Symbol, ct);
            top = ReadOrderBookTop(book.RootElement);
        }
        else
        {
            var live = _candles.TryGetLatestLiveFuturesBidAsk(ApiSymbol(leg), TimeSpan.FromSeconds(30));
            top = live.HasValue ? (live.Value.Bid, live.Value.Ask) : null;
        }

        if (!top.HasValue) return null;
        if (top.Value.Bid <= 0m || top.Value.Ask <= top.Value.Bid) return null;
        if (leg.IsFutures && top.Value.Ask - top.Value.Bid > 500m) return null;
        return top;
    }

    private static async Task<decimal> GetFuturesMarkAsync(CoincallApiClient client, string symbol, decimal bid, decimal ask, CancellationToken ct)
    {
        using var quotes = await client.GetFuturesQuoteSymbolsAsync(ct);
        var rows = new List<JsonElement>();
        CollectRows(quotes.RootElement, rows);
        foreach (var row in rows)
        {
            if (!SymbolMatches(ReadText(row, "symbol", "displaySymbol", "displayName", "ticker_id"), symbol)) continue;
            decimal mark = ReadDecimal(row, "markPrice", "mark_price", "indexPrice", "index_price", "price", "lastPrice");
            if (mark > 0) return mark;
        }
        return bid > 0 && ask > 0 ? (bid + ask) / 2m : 0m;
    }

    private async Task<decimal> GetTotalDeltaAsync(int accountId, string asset, CancellationToken ct)
    {
        var account = await _accounts.GetAccountByIdAsync(accountId, ct) ?? throw new InvalidOperationException("CoinCall account #" + accountId + " not found");
        var client = new CoincallApiClient(account);
        decimal total = 0m;
        using (var summary = await client.GetAccountSummaryAsync(ct))
        {
            var rows = new List<JsonElement>();
            CollectRows(summary.RootElement, rows);
            foreach (var row in rows)
            {
                string ccy = ReadText(row, "ccy", "currency", "coin", "asset", "base");
                if (!string.Equals(ccy, asset, StringComparison.OrdinalIgnoreCase)) continue;
                decimal bal = ReadDecimal(row, "equityAmount", "marginBalance", "accountEquity", "equity", "eq", "totalBalance", "walletBalance", "cashBalanceAmount", "cashBalance", "cashBal", "total", "balance", "bal");
                total += bal;
            }
        }
        using (var positions = await client.GetFuturesPositionsAsync(null, ct))
        {
            var rows = new List<JsonElement>();
            CollectRows(positions.RootElement, rows);
            foreach (var row in rows)
            {
                string sym = ReadText(row, "symbol", "displaySymbol", "displayName", "instrument");
                if (!sym.Contains(asset, StringComparison.OrdinalIgnoreCase)) continue;
                if (TryReadDecimal(row, out var directDelta, "delta"))
                {
                    total += directDelta;
                    continue;
                }
                decimal qty = ReadDecimal(row, "qty", "quantity", "positionQty", "positionSize", "size", "amount");
                if (qty <= 0) continue;
                total += ReadSide(row) == "sell" ? -qty : qty;
            }
        }
        return total;
    }

    private static string BuildGenericState(string phase, string execution, BackendGenericBotConfig cfg, JsonElement? firstOrder, BackendOrdersNetFill? firstFill, JsonElement? hedgeOrder, BackendOrdersNetFill? hedgeFill, decimal? firstPrice = null)
    {
        var payload = new Dictionary<string, object?> { ["phase"] = phase, ["execution"] = execution, ["strategy"] = cfg.Strategy, ["mode"] = cfg.Mode, ["pair"] = cfg.Pair, ["firstLeg"] = cfg.FirstLeg.Symbol, ["firstMarket"] = cfg.FirstLeg.Market, ["firstSide"] = cfg.FirstSide, ["hedgeLeg"] = cfg.HedgeLeg.Symbol, ["hedgeMarket"] = cfg.HedgeLeg.Market, ["hedgeSide"] = cfg.HedgeSide, ["amount"] = cfg.Amount, ["firstOrderPlaced"] = firstOrder.HasValue, ["firstOrderFilled"] = firstFill != null, ["hedgeOrderPlaced"] = hedgeOrder.HasValue, ["hedgeOrderFilled"] = hedgeFill != null, ["firstPlacedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
        if (firstPrice.HasValue) payload["firstPrice"] = firstPrice.Value;
        if (firstOrder.HasValue) payload["firstOrder"] = JsonForState(firstOrder.Value);
        if (firstFill != null) payload["firstFill"] = firstFill.ToPayload();
        if (hedgeOrder.HasValue) { payload["hedgeOrder"] = JsonForState(hedgeOrder.Value); payload["hedgePlacedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); }
        if (hedgeFill != null) payload["hedgeFill"] = hedgeFill.ToPayload();
        return JsonSerializer.Serialize(payload);
    }

    private static string StateMergeGeneric(string sourceJson, string phase, BackendGenericBotConfig cfg, BackendOrdersNetFill? firstFill = null, JsonElement? hedgeOrder = null, decimal? hedgePrice = null, BackendOrdersNetFill? hedgeFill = null)
    {
        var payload = new Dictionary<string, object?> { ["phase"] = phase, ["execution"] = "armed", ["strategy"] = cfg.Strategy, ["mode"] = cfg.Mode, ["pair"] = cfg.Pair, ["firstLeg"] = JsonText(sourceJson, "firstLeg", cfg.FirstLeg.Symbol), ["firstMarket"] = JsonText(sourceJson, "firstMarket", cfg.FirstLeg.Market), ["firstSide"] = JsonText(sourceJson, "firstSide", cfg.FirstSide), ["hedgeLeg"] = JsonText(sourceJson, "hedgeLeg", cfg.HedgeLeg.Symbol), ["hedgeMarket"] = JsonText(sourceJson, "hedgeMarket", cfg.HedgeLeg.Market), ["hedgeSide"] = JsonText(sourceJson, "hedgeSide", cfg.HedgeSide), ["amount"] = JsonDecimal(sourceJson, "amount", cfg.Amount), ["firstOrderPlaced"] = JsonBool(sourceJson, "firstOrderPlaced"), ["firstOrderFilled"] = JsonBool(sourceJson, "firstOrderFilled") || firstFill != null, ["hedgeOrderPlaced"] = JsonBool(sourceJson, "hedgeOrderPlaced") || hedgeOrder.HasValue, ["hedgeOrderFilled"] = JsonBool(sourceJson, "hedgeOrderFilled") || hedgeFill != null, ["firstPrice"] = JsonDecimal(sourceJson, "firstPrice", 0m), ["firstPlacedAtUtc"] = JsonText(sourceJson, "firstPlacedAtUtc", ""), ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson);
        CopyRaw(doc.RootElement, payload, "firstOrder"); CopyRaw(doc.RootElement, payload, "firstFill"); CopyRaw(doc.RootElement, payload, "hedgeOrder"); CopyRaw(doc.RootElement, payload, "hedgeFill"); CopyRaw(doc.RootElement, payload, "hedgePrice"); CopyRaw(doc.RootElement, payload, "hedgePlacedAtUtc");
        if (firstFill != null) payload["firstFill"] = firstFill.ToPayload();
        if (hedgeOrder.HasValue) { payload["hedgeOrder"] = JsonForState(hedgeOrder.Value); payload["hedgePlacedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); }
        if (hedgePrice.HasValue) payload["hedgePrice"] = hedgePrice.Value;
        if (hedgeFill != null) payload["hedgeFill"] = hedgeFill.ToPayload();
        return JsonSerializer.Serialize(payload);
    }

    private static string BuildDdhIdleState(BackendGenericBotConfig cfg, decimal delta, bool startLogged) => JsonSerializer.Serialize(new { phase = "delta-flat", execution = "armed", strategy = cfg.Strategy, asset = cfg.Asset, delta, amount = cfg.Amount, l1TrailSizeUsd = cfg.L1TrailSizeUsd, ddhStartLogged = startLogged, activeOrderPlaced = false, activeOrderFilled = false, heartbeatUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
    private static string BuildDdhReadyState(BackendGenericBotConfig cfg, decimal delta, bool startLogged, bool useCachedDelta = false) => JsonSerializer.Serialize(new Dictionary<string, object?> { ["phase"] = "ready-retry", ["execution"] = "armed", ["strategy"] = cfg.Strategy, ["asset"] = cfg.Asset, ["delta"] = delta, ["amount"] = cfg.Amount, ["l1TrailSizeUsd"] = cfg.L1TrailSizeUsd, ["ddhStartLogged"] = startLogged, ["ddhUseCachedDelta"] = useCachedDelta, ["activeOrderPlaced"] = false, ["activeOrderFilled"] = false, ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
    private static string BuildDdhActiveState(BackendGenericBotConfig cfg, decimal delta, BackendGenericLeg leg, string side, JsonElement order, decimal price, decimal bidAskMedian, bool startLogged) => JsonSerializer.Serialize(new Dictionary<string, object?> { ["phase"] = "active", ["execution"] = "armed", ["strategy"] = cfg.Strategy, ["asset"] = cfg.Asset, ["delta"] = delta, ["activeLeg"] = leg.Symbol, ["activeMarket"] = leg.Market, ["activeSide"] = side, ["activePrice"] = price, ["activeBidAskMedian"] = bidAskMedian, ["activeOrderPlaced"] = true, ["activeOrderFilled"] = false, ["activePlacedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), ["activeOrder"] = JsonForState(order), ["amount"] = cfg.Amount, ["l1TrailSizeUsd"] = cfg.L1TrailSizeUsd, ["ddhStartLogged"] = startLogged, ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
    private static string BuildDdhFilledState(string sourceJson, BackendOrdersNetFill fill, string side)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson);
        decimal previousDelta = JsonDecimal(sourceJson, "delta", 0m);
        decimal fillQty = fill.Qty > 0 ? fill.Qty : JsonDecimal(sourceJson, "amount", 0m);
        decimal signedFillDelta = string.Equals(side, "sell", StringComparison.OrdinalIgnoreCase) ? -fillQty : fillQty;
        decimal effectiveDelta = previousDelta + signedFillDelta;
        var payload = new Dictionary<string, object?> { ["phase"] = "filled-restart", ["execution"] = "armed", ["delta"] = effectiveDelta, ["ddhUseCachedDelta"] = true, ["activeOrderPlaced"] = false, ["activeOrderFilled"] = true, ["activeFill"] = fill.ToPayload(), ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
        CopyRaw(doc.RootElement, payload, "strategy"); CopyRaw(doc.RootElement, payload, "asset"); CopyRaw(doc.RootElement, payload, "amount");
        return JsonSerializer.Serialize(payload);
    }

    private static string StrategyName(CoincallBackendSpreadBotInstance instance) => string.IsNullOrWhiteSpace(instance.strategy) ? JsonText(instance.configJson, "strategy", "orders-net") : instance.strategy.Trim();
    private static string BaseAsset(string symbol) => symbol.Contains("ETH", StringComparison.OrdinalIgnoreCase) ? "ETH" : "BTC";

    private static string CleanPair(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string CleanName(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static decimal[] BuildLevels(BackendOrdersNetConfig cfg)
    {
        var levels = new decimal[cfg.Levels];
        for (int i = 0; i < levels.Length; i++) levels[i] = LevelPrice(cfg, i + 1);
        return levels;
    }

    private static string StateJson(string phase, string execution, bool? l1Placed, string? l1State)
    {
        return JsonSerializer.Serialize(new
        {
            phase,
            execution,
            l1Placed,
            l1State,
            heartbeatUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        });
    }

    private static string Branch(BackendOrdersNetConfig cfg) => "SpreadBot_OrNet_" + (cfg.LongFirst ? "LLF" : "SLF") + "_" + (cfg.BuySpread ? "BS" : "SS");
    private static decimal LevelPrice(BackendOrdersNetConfig cfg, int level) => cfg.L1Price + (cfg.LongFirst ? -1m : 1m) * (Math.Max(1, level) - 1) * cfg.StepUsd;
    private static string SideWord(string tradeSide) => tradeSide == "2" ? "Sell" : "Buy";
    private static decimal HedgePriceFromFirstFill(BackendOrdersNetConfig cfg, decimal firstFillPrice)
    {
        return string.Equals(cfg.HedgeLeg.Symbol, cfg.PairSecond.Symbol, StringComparison.OrdinalIgnoreCase)
            ? firstFillPrice - cfg.SpreadSizeUsd
            : firstFillPrice + cfg.SpreadSizeUsd;
    }
    private static string HedgeFormula(BackendOrdersNetConfig cfg)
    {
        return string.Equals(cfg.HedgeLeg.Symbol, cfg.PairSecond.Symbol, StringComparison.OrdinalIgnoreCase)
            ? "firstFillPrice - spreadSizeUsd"
            : "firstFillPrice + spreadSizeUsd";
    }

    private static decimal RoundTrailPrice(decimal price, string tradeSide)
    {
        const decimal step = 50m;
        if (price <= 0) return 0m;
        return tradeSide == "2" ? Math.Ceiling(price / step) * step : Math.Floor(price / step) * step;
    }

    private static (decimal Bid, decimal Ask)? ReadOrderBookTop(JsonElement root)
    {
        var bids = new List<decimal>();
        var asks = new List<decimal>();
        CollectBookPrices(root, "bids", bids);
        CollectBookPrices(root, "bid", bids);
        CollectBookPrices(root, "asks", asks);
        CollectBookPrices(root, "ask", asks);
        decimal bid = 0m, ask = 0m;
        foreach (var v in bids) if (v > bid) bid = v;
        foreach (var v in asks) if (v > 0 && (ask == 0m || v < ask)) ask = v;
        return bid > 0 && ask > 0 ? (bid, ask) : null;
    }

    private static void CollectBookPrices(JsonElement value, string name, List<decimal> prices)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in value.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) CollectPriceRows(prop.Value, prices);
                else CollectBookPrices(prop.Value, name, prices);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) CollectBookPrices(item, name, prices);
        }
    }

    private static void CollectPriceRows(JsonElement value, List<decimal> prices)
    {
        if (value.ValueKind != JsonValueKind.Array) return;
        foreach (var item in value.EnumerateArray())
        {
            decimal p = 0m;
            if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0) p = ElementDecimal(item[0]);
            else if (item.ValueKind == JsonValueKind.Object) p = ReadDecimal(item, "price", "p", "px");
            if (p > 0) prices.Add(p);
        }
    }

    private static string BuildLevelState(string phase, string execution, int level, BackendOrdersNetConfig cfg, string? botState, JsonElement? firstOrder, BackendOrdersNetFill? firstFill, BackendOrdersNetFill? hedgeFill, decimal? firstPriceOverride = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["phase"] = phase,
            ["execution"] = execution,
            ["currentLevel"] = level,
            ["levels"] = cfg.Levels,
            ["pair"] = cfg.Pair,
            ["mode"] = cfg.Mode,
            ["buy"] = cfg.BuyLeg.Symbol,
            ["sell"] = cfg.SellLeg.Symbol,
            ["firstLeg"] = cfg.FirstLeg.Symbol,
            ["firstMarket"] = cfg.FirstLeg.Market,
            ["firstSide"] = cfg.FirstSideName,
            ["hedgeLeg"] = cfg.HedgeLeg.Symbol,
            ["hedgeMarket"] = cfg.HedgeLeg.Market,
            ["hedgeSide"] = cfg.HedgeSideName,
            ["firstOrderPlaced"] = firstOrder.HasValue,
            ["firstOrderFilled"] = firstFill != null,
            ["hedgeOrderPlaced"] = false,
            ["hedgeOrderFilled"] = hedgeFill != null,
            ["firstState"] = botState ?? "",
            ["firstPrice"] = firstPriceOverride ?? LevelPrice(cfg, level),
            ["amount"] = cfg.Amount,
            ["firstPlacedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        if (firstOrder.HasValue) payload["firstOrder"] = JsonForState(firstOrder.Value);
        if (firstFill != null) payload["firstFill"] = firstFill.ToPayload();
        if (hedgeFill != null) payload["hedgeFill"] = hedgeFill.ToPayload();
        return JsonSerializer.Serialize(payload);
    }

    private static string BuildNextLevelStateAfterHedgePlacement(string sourceJson, int nextLevel, BackendOrdersNetConfig cfg)
    {
        string json = BuildLevelState("ready-next-level", "armed", nextLevel, cfg, null, null, null, null);
        JsonElement root = default;
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson); root = doc.RootElement.Clone(); }
        catch { }
        JsonDocument? targetDoc = null;
        var payload = new Dictionary<string, object?>();
        try
        {
            targetDoc = JsonDocument.Parse(json);
            foreach (var prop in targetDoc.RootElement.EnumerateObject()) payload[prop.Name] = prop.Value.Clone();
        }
        catch { }
        payload["phase"] = "ready-next-level";
        payload["currentLevel"] = nextLevel;
        payload["firstOrderPlaced"] = false;
        payload["firstOrderFilled"] = false;
        payload["hedgeOrderPlaced"] = false;
        payload["hedgeOrderFilled"] = false;
        if (root.ValueKind == JsonValueKind.Object)
        {
            CopyRaw(root, payload, "completedLevels");
            var pending = PendingPreviousHedgePayloads(sourceJson);
            if (root.TryGetProperty("hedgeOrder", out var hedgeOrder) && hedgeOrder.ValueKind == JsonValueKind.Object)
            {
                int hedgeLevel = Math.Max(1, nextLevel - 1);
                var item = new Dictionary<string, object?>
                {
                    ["level"] = hedgeLevel,
                    ["order"] = JsonForState(hedgeOrder),
                    ["price"] = JsonDecimal(sourceJson, "hedgePrice", 0m),
                    ["placedAtUtc"] = JsonText(sourceJson, "hedgePlacedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                };
                RemovePendingByOrderKey(pending, PendingHedgeKey(hedgeOrder, hedgeLevel, JsonDecimal(sourceJson, "hedgePrice", 0m), JsonText(sourceJson, "hedgePlacedAtUtc", "")));
                pending.Add(item);
                CopyRawAs(root, payload, "hedgeOrder", "previousHedgeOrder");
                CopyRawAs(root, payload, "hedgePrice", "previousHedgePrice");
                CopyRawAs(root, payload, "hedgePlacedAtUtc", "previousHedgePlacedAtUtc");
                payload["previousHedgeLevel"] = hedgeLevel;
                payload["previousHedgeOrderFilled"] = false;
            }
            payload["pendingPreviousHedges"] = pending;
        }
        doc?.Dispose();
        targetDoc?.Dispose();
        return JsonSerializer.Serialize(payload);
    }

    private static string PreservePreviousHedgeFields(string sourceJson, string targetJson)
    {
        JsonElement source = default;
        JsonDocument? sourceDoc = null;
        JsonDocument? targetDoc = null;
        var payload = new Dictionary<string, object?>();
        try { sourceDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson); source = sourceDoc.RootElement.Clone(); }
        catch { }
        try
        {
            targetDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(targetJson) ? "{}" : targetJson);
            foreach (var prop in targetDoc.RootElement.EnumerateObject()) payload[prop.Name] = prop.Value.Clone();
        }
        catch { return targetJson; }
        if (source.ValueKind == JsonValueKind.Object)
        {
            CopyRaw(source, payload, "previousHedgeOrder");
            CopyRaw(source, payload, "previousHedgePrice");
            CopyRaw(source, payload, "previousHedgePlacedAtUtc");
            CopyRaw(source, payload, "previousHedgeLevel");
            CopyRaw(source, payload, "previousHedgeOrderFilled");
            CopyRaw(source, payload, "completedLevels");
            CopyRaw(source, payload, "pendingPreviousHedges");
        }
        sourceDoc?.Dispose();
        targetDoc?.Dispose();
        return JsonSerializer.Serialize(payload);
    }

    private static string StateMerge(string sourceJson, string phase, string execution, int level, BackendOrdersNetFill? firstFill, BackendOrdersNetFill? hedgeFill)
    {
        return StateMerge(sourceJson, phase, execution, level, firstFill, null, null, null, hedgeFill);
    }

    private static string StateMerge(string sourceJson, string phase, string execution, int level, BackendOrdersNetFill? firstFill, JsonElement? hedgeOrder, decimal? hedgePrice, BackendOrdersNetConfig? cfg, BackendOrdersNetFill? hedgeFill = null)
    {
        JsonElement root = default;
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson); root = doc.RootElement.Clone(); }
        catch { }
        var payload = new Dictionary<string, object?>
        {
            ["phase"] = phase,
            ["execution"] = execution,
            ["currentLevel"] = level,
            ["levels"] = JsonInt(sourceJson, "levels", level),
            ["pair"] = JsonText(sourceJson, "pair", ""),
            ["mode"] = JsonText(sourceJson, "mode", ""),
            ["buy"] = JsonText(sourceJson, "buy", ""),
            ["sell"] = JsonText(sourceJson, "sell", ""),
            ["firstLeg"] = JsonText(sourceJson, "firstLeg", ""),
            ["firstMarket"] = JsonText(sourceJson, "firstMarket", cfg?.FirstLeg.Market ?? ""),
            ["firstSide"] = JsonText(sourceJson, "firstSide", ""),
            ["hedgeLeg"] = JsonText(sourceJson, "hedgeLeg", ""),
            ["hedgeMarket"] = JsonText(sourceJson, "hedgeMarket", cfg?.HedgeLeg.Market ?? ""),
            ["hedgeSide"] = JsonText(sourceJson, "hedgeSide", ""),
            ["firstOrderPlaced"] = JsonBool(sourceJson, "firstOrderPlaced"),
            ["firstOrderFilled"] = JsonBool(sourceJson, "firstOrderFilled") || firstFill != null,
            ["hedgeOrderPlaced"] = JsonBool(sourceJson, "hedgeOrderPlaced") || hedgeOrder.HasValue,
            ["hedgeOrderFilled"] = JsonBool(sourceJson, "hedgeOrderFilled") || hedgeFill != null,
            ["firstState"] = JsonText(sourceJson, "firstState", ""),
            ["firstPrice"] = JsonDecimal(sourceJson, "firstPrice", 0m),
            ["amount"] = JsonDecimal(sourceJson, "amount", 0m),
            ["firstPlacedAtUtc"] = JsonText(sourceJson, "firstPlacedAtUtc", ""),
            ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        CopyRaw(root, payload, "firstOrder");
        CopyRaw(root, payload, "firstFill");
        CopyRaw(root, payload, "hedgeOrder");
        CopyRaw(root, payload, "hedgeFill");
        CopyRaw(root, payload, "hedgePrice");
        CopyRaw(root, payload, "hedgePlacedAtUtc");
        CopyRaw(root, payload, "previousHedgeOrder");
        CopyRaw(root, payload, "previousHedgePrice");
        CopyRaw(root, payload, "previousHedgePlacedAtUtc");
        CopyRaw(root, payload, "previousHedgeLevel");
        CopyRaw(root, payload, "previousHedgeOrderFilled");
        CopyRaw(root, payload, "pendingPreviousHedges");
        CopyRaw(root, payload, "completedLevels");
        if (firstFill != null) payload["firstFill"] = firstFill.ToPayload();
        if (hedgeOrder.HasValue) payload["hedgeOrder"] = JsonForState(hedgeOrder.Value);
        if (hedgeOrder.HasValue) payload["hedgePlacedAtUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        if (hedgePrice.HasValue) payload["hedgePrice"] = hedgePrice.Value;
        if (cfg != null) payload["hedgeFormula"] = HedgeFormula(cfg);
        if (hedgeFill != null)
        {
            payload["hedgeFill"] = hedgeFill.ToPayload();
            payload["completedLevels"] = Math.Max(JsonInt(sourceJson, "completedLevels", 0), level);
        }
        doc?.Dispose();
        return JsonSerializer.Serialize(payload);
    }

    private static string StateWithHeartbeat(string sourceJson, string phase)
    {
        JsonElement root = default;
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson); root = doc.RootElement.Clone(); }
        catch { }
        var payload = new Dictionary<string, object?> { ["phase"] = phase, ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("phase") || prop.NameEquals("heartbeatUtc")) continue;
                payload[prop.Name] = prop.Value.Clone();
            }
        }
        doc?.Dispose();
        return JsonSerializer.Serialize(payload);
    }

    private static string StateWithPreviousHedgeFill(string sourceJson, BackendOrdersNetFill fill, int previousLevel)
    {
        JsonElement root = default;
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson); root = doc.RootElement.Clone(); }
        catch { }
        var payload = new Dictionary<string, object?> { ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("heartbeatUtc") || prop.NameEquals("previousHedgeOrderFilled") || prop.NameEquals("previousHedgeFill") || prop.NameEquals("completedLevels")) continue;
                payload[prop.Name] = prop.Value.Clone();
            }
        }
        payload["previousHedgeOrderFilled"] = true;
        payload["previousHedgeFill"] = fill.ToPayload();
        payload["completedLevels"] = Math.Max(JsonInt(sourceJson, "completedLevels", 0), previousLevel);
        doc?.Dispose();
        return JsonSerializer.Serialize(payload);
    }

    private static string StateWithPreviousHedgeFills(string sourceJson, IReadOnlyList<PendingOrdersNetHedge> pending, IReadOnlyList<PendingOrdersNetHedgeFill> filled, PendingOrdersNetHedgeFill rearm)
    {
        JsonElement root = default;
        JsonDocument? doc = null;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson); root = doc.RootElement.Clone(); }
        catch { }
        var payload = new Dictionary<string, object?> { ["heartbeatUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) };
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("heartbeatUtc") ||
                    prop.NameEquals("pendingPreviousHedges") ||
                    prop.NameEquals("previousHedgeOrder") ||
                    prop.NameEquals("previousHedgePrice") ||
                    prop.NameEquals("previousHedgePlacedAtUtc") ||
                    prop.NameEquals("previousHedgeLevel") ||
                    prop.NameEquals("previousHedgeOrderFilled") ||
                    prop.NameEquals("previousHedgeFill") ||
                    prop.NameEquals("completedLevels"))
                {
                    continue;
                }
                payload[prop.Name] = prop.Value.Clone();
            }
        }

        var filledKeys = new HashSet<string>();
        foreach (var item in filled) filledKeys.Add(PendingHedgeKey(item.Pending));

        var remaining = new List<Dictionary<string, object?>>();
        foreach (var item in pending)
        {
            if (filledKeys.Contains(PendingHedgeKey(item))) continue;
            remaining.Add(PendingPreviousHedgePayload(item));
        }

        int previousLevel = Math.Max(1, rearm.Pending.Level);
        payload["pendingPreviousHedges"] = remaining;
        payload["previousHedgeOrder"] = JsonForState(rearm.Pending.Order);
        payload["previousHedgePrice"] = rearm.Pending.Price;
        payload["previousHedgePlacedAtUtc"] = rearm.Pending.PlacedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        payload["previousHedgeLevel"] = previousLevel;
        payload["previousHedgeOrderFilled"] = true;
        payload["previousHedgeFill"] = rearm.Fill.ToPayload();
        payload["completedLevels"] = Math.Max(JsonInt(sourceJson, "completedLevels", 0), previousLevel);
        doc?.Dispose();
        return JsonSerializer.Serialize(payload);
    }

    private static void CopyRaw(JsonElement root, Dictionary<string, object?> payload, string name)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value)) payload[name] = JsonForState(value);
    }

    private static void CopyRawAs(JsonElement root, Dictionary<string, object?> payload, string sourceName, string targetName)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(sourceName, out var value)) payload[targetName] = JsonForState(value);
    }

    private static object? JsonForState(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var obj = new Dictionary<string, object?>();
                foreach (var prop in value.EnumerateObject()) obj[prop.Name] = JsonForState(prop.Value);
                return obj;
            }
            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                foreach (var item in value.EnumerateArray()) list.Add(JsonForState(item));
                return list;
            }
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.Number:
            {
                string raw = value.GetRawText();
                if (IsLargeIntegerJsonNumber(raw)) return raw;
                return value.TryGetDecimal(out var d) ? d : raw;
            }
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
            default:
                return null;
        }
    }

    private static bool IsLargeIntegerJsonNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();
        int start = raw.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
        if (raw.Length - start < 16) return false;
        for (int i = start; i < raw.Length; i++) if (raw[i] < '0' || raw[i] > '9') return false;
        return true;
    }

    private static bool IsCoincallOrderExpired(Exception ex)
    {
        string msg = ex.Message ?? string.Empty;
        return msg.Contains("CoinCall API error 10540", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Order has expired", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCoincallCancelFailed(Exception ex)
    {
        string msg = ex.Message ?? string.Empty;
        return msg.Contains("CoinCall API error 10537", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("Order cancellation failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool JsonBool(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        }
        catch { return false; }
    }

    private static bool? BoolJson(string json, string name) => JsonBool(json, name) ? true : null;

    private static JsonElement JsonObject(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object) return value.Clone();
        }
        catch { }
        return default;
    }

    private static string JsonText(string json, string name, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (!doc.RootElement.TryGetProperty(name, out var value)) return fallback;
            return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? fallback) : value.GetRawText();
        }
        catch { return fallback; }
    }

    private static int JsonInt(string json, string name, int fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (!doc.RootElement.TryGetProperty(name, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : fallback;
        }
        catch { return fallback; }
    }

    private static decimal JsonDecimal(string json, string name, decimal fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (!doc.RootElement.TryGetProperty(name, out var value)) return fallback;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
            return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n : fallback;
        }
        catch { return fallback; }
    }

    private static bool ShouldLogWait(string stateJson)
    {
        DateTime? heartbeat = JsonDate(stateJson, "heartbeatUtc");
        return !heartbeat.HasValue || heartbeat.Value <= DateTime.UtcNow.AddSeconds(-55);
    }

    private static BackendOrdersNetFill? ReadFill(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (!doc.RootElement.TryGetProperty(name, out var fill) || fill.ValueKind != JsonValueKind.Object) return null;
            decimal qty = ReadDecimal(fill, "qty", "quantity", "amount");
            decimal price = ReadDecimal(fill, "price", "avgPrice");
            return qty > 0 && price > 0 ? new BackendOrdersNetFill(qty, price, Array.Empty<object>()) : null;
        }
        catch { return null; }
    }

    private static BackendOrdersNetFill? FindFill(string stateJson, string orderProperty, string symbol, string side, decimal expectedPrice, decimal expectedQty, DateTime placedAt, JsonElement historyRoot)
    {
        return FindFillForOrder(JsonObject(stateJson, orderProperty), symbol, side, expectedPrice, expectedQty, placedAt, historyRoot);
    }

    private static BackendOrdersNetFill? FindFillForOrder(JsonElement order, string symbol, string side, decimal expectedPrice, decimal expectedQty, DateTime placedAt, JsonElement historyRoot)
    {
        var rows = new List<JsonElement>();
        CollectRows(historyRoot, rows);
        string orderId = order.ValueKind == JsonValueKind.Object ? FindDeepText(order, "orderId", "ordId", "order_id", "id", "oid", "data") : "";
        string clientOrderId = order.ValueKind == JsonValueKind.Object ? FindDeepText(order, "clientOrderId", "clientOid", "clOrdId", "coid") : "";
        decimal qty = 0m;
        decimal notional = 0m;
        var matched = new List<object>();
        foreach (var row in rows)
        {
            if (!SymbolMatches(ReadText(row, "symbol", "displaySymbol", "displayName", "instId", "instrument", "s"), symbol)) continue;
            if (ReadSide(row) != side) continue;
            string rowOrderId = ReadText(row, "orderId", "ordId", "order_id", "id", "oid");
            string rowClientId = ReadText(row, "clientOrderId", "clientOid", "clOrdId", "coid");
            bool identityMatch = (!string.IsNullOrWhiteSpace(orderId) && orderId == rowOrderId) || (!string.IsNullOrWhiteSpace(clientOrderId) && clientOrderId == rowClientId);
            DateTime? ts = ReadTime(row);
            decimal price = ReadDecimal(row, "price", "fillPrice", "filledPrice", "px", "tradePrice", "dealPrice", "matchPrice", "mpr");
            if (!identityMatch)
            {
                if (!ts.HasValue || ts.Value < placedAt.AddSeconds(-60)) continue;
                if (expectedPrice > 0 && (price <= 0 || Math.Abs(price - expectedPrice) > Math.Max(0.00000001m, Math.Abs(expectedPrice) * 0.00000001m))) continue;
            }
            decimal rowQty = ReadDecimal(row, "qty", "quantity", "amount", "fillQty", "filledQty", "filledQuantity", "dealQty", "baseQty", "q", "sz");
            if (rowQty <= 0 || price <= 0) continue;
            qty += rowQty;
            notional += rowQty * price;
            matched.Add(new { qty = rowQty, price, ts = ts?.ToString("O", CultureInfo.InvariantCulture), orderId = rowOrderId, clientOrderId = rowClientId });
        }
        if (qty + Math.Max(0.000000000001m, expectedQty * 0.00000001m) < expectedQty) return null;
        return new BackendOrdersNetFill(qty, notional / qty, matched);
    }

    private static List<PendingOrdersNetHedge> ReadPendingPreviousHedges(string stateJson)
    {
        var result = new List<PendingOrdersNetHedge>();
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(stateJson) ? "{}" : stateJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            if (doc.RootElement.TryGetProperty("pendingPreviousHedges", out var pending) && pending.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in pending.EnumerateArray())
                {
                    var parsed = ReadPendingPreviousHedge(item);
                    if (parsed != null) result.Add(parsed);
                }
            }
            if (!JsonBool(stateJson, "previousHedgeOrderFilled") &&
                doc.RootElement.TryGetProperty("previousHedgeOrder", out var previousOrder) &&
                previousOrder.ValueKind == JsonValueKind.Object)
            {
                int level = Math.Max(1, JsonInt(stateJson, "previousHedgeLevel", Math.Max(1, JsonInt(stateJson, "currentLevel", 1) - 1)));
                decimal price = JsonDecimal(stateJson, "previousHedgePrice", 0m);
                DateTime placedAt = JsonDate(stateJson, "previousHedgePlacedAtUtc") ?? DateTime.UtcNow.AddMinutes(-10);
                var legacy = new PendingOrdersNetHedge(level, price, placedAt, previousOrder.Clone());
                bool exists = false;
                string legacyKey = PendingHedgeKey(legacy);
                foreach (var item in result)
                {
                    if (PendingHedgeKey(item) == legacyKey) { exists = true; break; }
                }
                if (!exists) result.Add(legacy);
            }
        }
        catch { }
        return result;
    }

    private static PendingOrdersNetHedge? ReadPendingPreviousHedge(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        if (!item.TryGetProperty("order", out var order) || order.ValueKind != JsonValueKind.Object) return null;
        int level = ElementInt(item, "level", 1);
        decimal price = ReadDecimal(item, "price");
        DateTime placedAt = ReadTime(item, "placedAtUtc") ?? DateTime.UtcNow.AddMinutes(-10);
        return new PendingOrdersNetHedge(Math.Max(1, level), price, placedAt, order.Clone());
    }

    private static List<Dictionary<string, object?>> PendingPreviousHedgePayloads(string stateJson)
    {
        var payloads = new List<Dictionary<string, object?>>();
        foreach (var item in ReadPendingPreviousHedges(stateJson)) payloads.Add(PendingPreviousHedgePayload(item));
        return payloads;
    }

    private static Dictionary<string, object?> PendingPreviousHedgePayload(PendingOrdersNetHedge item)
    {
        return new Dictionary<string, object?>
        {
            ["level"] = item.Level,
            ["order"] = JsonForState(item.Order),
            ["price"] = item.Price,
            ["placedAtUtc"] = item.PlacedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };
    }

    private static void RemovePendingByOrderKey(List<Dictionary<string, object?>> pending, string key)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            try
            {
                string candidate = PendingHedgeKey(JsonSerializer.Serialize(pending[i]));
                if (candidate == key) pending.RemoveAt(i);
            }
            catch { }
        }
    }

    private static string PendingHedgeKey(PendingOrdersNetHedge item) => PendingHedgeKey(item.Order, item.Level, item.Price, item.PlacedAtUtc.ToString("O", CultureInfo.InvariantCulture));

    private static string PendingHedgeKey(string pendingJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(pendingJson) ? "{}" : pendingJson);
            var item = ReadPendingPreviousHedge(doc.RootElement);
            return item == null ? pendingJson : PendingHedgeKey(item);
        }
        catch { return pendingJson; }
    }

    private static string PendingHedgeKey(JsonElement order, int level, decimal price, string placedAtUtc)
    {
        string orderId = order.ValueKind == JsonValueKind.Object ? FindDeepText(order, "orderId", "ordId", "order_id", "id", "oid", "data") : "";
        if (!string.IsNullOrWhiteSpace(orderId)) return "order:" + orderId;
        string clientOrderId = order.ValueKind == JsonValueKind.Object ? FindDeepText(order, "clientOrderId", "clientOid", "clOrdId", "coid") : "";
        if (!string.IsNullOrWhiteSpace(clientOrderId)) return "client:" + clientOrderId;
        return "level:" + level.ToString(CultureInfo.InvariantCulture) + ":price:" + price.ToString(CultureInfo.InvariantCulture) + ":placed:" + placedAtUtc;
    }

    private static void CollectRows(JsonElement value, List<JsonElement> rows)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) rows.Add(item.Clone());
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var name in new[] { "data", "list", "rows", "items", "records", "result", "accounts", "balances", "positions" })
        {
            if (value.TryGetProperty(name, out var child)) CollectRows(child, rows);
        }
    }

    private static string JsonDeepText(string json, params string[] names)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return FindDeepText(doc.RootElement, names);
        }
        catch { return ""; }
    }

    private static string JsonDeepTextIn(string json, string propertyName, params string[] names)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(propertyName, out var child)) return "";
            return FindDeepText(child, names);
        }
        catch { return ""; }
    }

    private static string FindDeepText(JsonElement value, params string[] names)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (value.TryGetProperty(name, out var direct)) return ElementText(direct);
            }
            foreach (var prop in value.EnumerateObject())
            {
                var nested = FindDeepText(prop.Value, names);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var nested = FindDeepText(item, names);
                if (!string.IsNullOrWhiteSpace(nested)) return nested;
            }
        }
        return "";
    }

    private static DateTime? JsonDate(string json, string name)
    {
        string text = JsonText(json, name, "");
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt : null;
    }

    private static string ReadText(JsonElement row, params string[] names)
    {
        foreach (var name in names) if (row.TryGetProperty(name, out var value)) return ElementText(value);
        return "";
    }

    private static string ElementText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String) return value.GetString()?.Trim() ?? "";
        if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) return value.GetRawText();
        return "";
    }

    private static decimal ElementDecimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out n)) return n;
        return 0m;
    }

    private static int ElementInt(JsonElement row, string name, int fallback)
    {
        if (!row.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : fallback;
    }

    private static decimal ReadDecimal(JsonElement row, params string[] names)
    {
        foreach (var name in names)
        {
            if (!row.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
            if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out n)) return n;
        }
        return 0m;
    }

    private static bool TryReadDecimal(JsonElement row, out decimal value, params string[] names)
    {
        foreach (var name in names)
        {
            if (!row.TryGetProperty(name, out var element)) continue;
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value)) return true;
            if (element.ValueKind == JsonValueKind.String && decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return true;
        }
        value = 0m;
        return false;
    }

    private static DateTime? ReadTime(JsonElement row)
    {
        string raw = ReadText(row, "time", "ts", "tradeTime", "fillTime", "createTime", "createdTime", "updateTime", "t");
        return ParseTime(raw);
    }

    private static DateTime? ReadTime(JsonElement row, params string[] names)
    {
        string raw = ReadText(row, names);
        return ParseTime(raw);
    }

    private static DateTime? ParseTime(string raw)
    {
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
        {
            if (n < 1000000000000L) n *= 1000L;
            return DateTimeOffset.FromUnixTimeMilliseconds(n).UtcDateTime;
        }
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt) ? dt : null;
    }

    private static string ReadSide(JsonElement row)
    {
        string raw = ReadText(row, "side", "tradeSide", "direction", "sd", "si").Trim().ToLowerInvariant();
        if (raw == "1" || raw == "buy" || raw == "bid" || raw == "b") return "buy";
        if (raw == "2" || raw == "sell" || raw == "ask" || raw == "s") return "sell";
        return "";
    }

    private static bool SymbolMatches(string a, string b)
    {
        string Clean(string v) => (v ?? "").Trim().ToUpperInvariant().Replace(" ", "-");
        return Clean(a) == Clean(b) || Clean(FuturesApiSymbol(a)) == Clean(FuturesApiSymbol(b));
    }

    private static string ApiSymbol(BackendOrdersNetLeg leg) => leg.IsSpot ? leg.Symbol : FuturesApiSymbol(leg.Symbol);

    private static string ApiSymbol(BackendGenericLeg leg) => leg.IsSpot ? leg.Symbol : FuturesApiSymbol(leg.Symbol);

    private static string FuturesApiSymbol(string symbol)
    {
        string s = (symbol ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", "-");
        if (string.IsNullOrWhiteSpace(s)) return s;
        if (s.EndsWith("USDT-PERP", StringComparison.OrdinalIgnoreCase))
            return s[..^"USDT-PERP".Length] + "USD";
        int dated = s.IndexOf("USDT-", StringComparison.OrdinalIgnoreCase);
        if (dated > 0)
            return s[..dated] + "USD-" + s[(dated + "USDT-".Length)..];
        return s;
    }
}

internal sealed record BackendOrdersNetFill(decimal Qty, decimal Price, IReadOnlyList<object> Rows)
{
    public object ToPayload() => new { qty = Qty, price = Price, rows = Rows };
}

internal sealed record PendingOrdersNetHedge(int Level, decimal Price, DateTime PlacedAtUtc, JsonElement Order);

internal sealed record PendingOrdersNetHedgeFill(PendingOrdersNetHedge Pending, BackendOrdersNetFill Fill);

internal sealed record BackendOrdersNetLeg(string Symbol)
{
    public bool IsSpot => Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) && !Symbol.EndsWith("-PERP", StringComparison.OrdinalIgnoreCase);
    public bool IsFutures => !IsSpot;
    public string Market => IsSpot ? "spot" : "futures";
}

internal sealed class BackendOrdersNetConfig
{
    public string Pair { get; init; } = "";
    public string Strategy { get; init; } = "orders-net";
    public string Mode { get; init; } = "LLF-BS";
    public string Asset { get; init; } = "BTC";
    public bool ExecutionArmed { get; init; }
    public bool LongFirst { get; init; }
    public bool BuySpread { get; init; }
    public BackendOrdersNetLeg PairFirst { get; init; } = new("");
    public BackendOrdersNetLeg PairSecond { get; init; } = new("");
    public BackendOrdersNetLeg BuyLeg { get; init; } = new("");
    public BackendOrdersNetLeg SellLeg { get; init; } = new("");
    public BackendOrdersNetLeg FirstLeg { get; init; } = new("");
    public BackendOrdersNetLeg HedgeLeg { get; init; } = new("");
    public string FirstSide { get; init; } = "1";
    public string HedgeSide { get; init; } = "2";
    public string FirstSideName => FirstSide == "2" ? "sell" : "buy";
    public string HedgeSideName => HedgeSide == "2" ? "sell" : "buy";
    public decimal Amount { get; init; }
    public int Levels { get; init; }
    public decimal SpreadSizeUsd { get; init; }
    public string L1Mode { get; init; } = "price";
    public decimal L1Price { get; init; }
    public decimal L1TrailSizeUsd { get; init; }
    public decimal StepUsd { get; init; }

    public object ToLogPayload() => new { Pair, Strategy, Mode, Asset, ExecutionArmed, LongFirst, BuySpread, PairFirst, PairSecond, BuyLeg, SellLeg, FirstLeg, FirstSide, HedgeLeg, HedgeSide, Amount, Levels, SpreadSizeUsd, L1Mode, L1Price, L1TrailSizeUsd, StepUsd };

    public BackendOrdersNetConfig WithLongFirst(bool longFirst)
    {
        var firstLeg = longFirst ? BuyLeg : SellLeg;
        var hedgeLeg = longFirst ? SellLeg : BuyLeg;
        string firstSide = string.Equals(firstLeg.Symbol, BuyLeg.Symbol, StringComparison.OrdinalIgnoreCase) ? "1" : "2";
        string hedgeSide = string.Equals(hedgeLeg.Symbol, BuyLeg.Symbol, StringComparison.OrdinalIgnoreCase) ? "1" : "2";
        string prefix = longFirst ? "LLF" : "SLF";
        string suffix = BuySpread ? "BS" : "SS";
        return new BackendOrdersNetConfig
        {
            Pair = Pair,
            Strategy = Strategy,
            Mode = prefix + "-" + suffix,
            Asset = Asset,
            ExecutionArmed = ExecutionArmed,
            LongFirst = longFirst,
            BuySpread = BuySpread,
            PairFirst = PairFirst,
            PairSecond = PairSecond,
            BuyLeg = BuyLeg,
            SellLeg = SellLeg,
            FirstLeg = firstLeg,
            HedgeLeg = hedgeLeg,
            FirstSide = firstSide,
            HedgeSide = hedgeSide,
            Amount = Amount,
            Levels = Levels,
            SpreadSizeUsd = SpreadSizeUsd,
            L1Mode = L1Mode,
            L1Price = L1Price,
            L1TrailSizeUsd = L1TrailSizeUsd,
            StepUsd = StepUsd
        };
    }

    public static BackendOrdersNetConfig Parse(string configJson, string pair, string mode)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
        var root = doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement : default;
        string cleanPair = Clean(root, "pair", pair).ToUpperInvariant();
        string cleanMode = Clean(root, "mode", mode).ToUpperInvariant();
        string strategy = Clean(root, "strategy", "orders-net");
        string asset = Clean(root, "asset", InferAsset(cleanPair)).ToUpperInvariant() == "ETH" ? "ETH" : "BTC";
        string[] legs = NormalizePairLegs(cleanPair);
        if (legs.Length != 2) throw new InvalidOperationException("pair must be A_B");
        bool buySpread = cleanMode.Contains("_BS", StringComparison.OrdinalIgnoreCase) || cleanMode.EndsWith("BS", StringComparison.OrdinalIgnoreCase);
        bool longFirst = cleanMode.Contains("LLF", StringComparison.OrdinalIgnoreCase);
        var first = new BackendOrdersNetLeg(legs[0]);
        var second = new BackendOrdersNetLeg(legs[1]);
        var buyLeg = buySpread ? first : second;
        var sellLeg = buySpread ? second : first;
        var firstLeg = longFirst ? buyLeg : sellLeg;
        var hedgeLeg = longFirst ? sellLeg : buyLeg;
        string firstSide = string.Equals(firstLeg.Symbol, buyLeg.Symbol, StringComparison.OrdinalIgnoreCase) ? "1" : "2";
        string hedgeSide = string.Equals(hedgeLeg.Symbol, buyLeg.Symbol, StringComparison.OrdinalIgnoreCase) ? "1" : "2";
        return new BackendOrdersNetConfig
        {
            Pair = cleanPair,
            Strategy = strategy,
            Mode = cleanMode,
            Asset = asset,
            ExecutionArmed = Bool(root, "executionArmed", false),
            LongFirst = longFirst,
            BuySpread = buySpread,
            PairFirst = first,
            PairSecond = second,
            BuyLeg = buyLeg,
            SellLeg = sellLeg,
            FirstLeg = firstLeg,
            HedgeLeg = hedgeLeg,
            FirstSide = firstSide,
            HedgeSide = hedgeSide,
            Amount = Dec(root, "amount", Dec(root, "qty", 0m)),
            Levels = Math.Max(1, (int)Dec(root, "levels", 1m)),
            SpreadSizeUsd = Dec(root, "spreadSizeUsd", 0m),
            L1Mode = Clean(root, "l1Mode", "price").ToLowerInvariant(),
            L1Price = Dec(root, "l1Price", 0m),
            L1TrailSizeUsd = Dec(root, "l1TrailSizeUsd", 50m),
            StepUsd = Dec(root, "stepUsd", 0m)
        };
    }

    private static string Clean(JsonElement root, string name, string fallback)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!.Trim();
            if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
        }
        return fallback;
    }

    private static bool Bool(JsonElement root, string name, bool fallback)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        return value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) ? parsed : fallback;
    }

    private static decimal Dec(JsonElement root, string name, decimal fallback)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n : fallback;
    }

    private static string[] NormalizePairLegs(string cleanPair)
    {
        string p = (cleanPair ?? string.Empty).Trim().ToUpperInvariant();
        if (p.EndsWith("_PERP_SPOT", StringComparison.OrdinalIgnoreCase))
        {
            string asset = p.Contains("ETH", StringComparison.OrdinalIgnoreCase) ? "ETH" : "BTC";
            return new[] { asset + "USDT-PERP", asset + "USDT" };
        }
        if (p.EndsWith("_SPOT_PERP", StringComparison.OrdinalIgnoreCase))
        {
            string asset = p.Contains("ETH", StringComparison.OrdinalIgnoreCase) ? "ETH" : "BTC";
            return new[] { asset + "USDT", asset + "USDT-PERP" };
        }
        return p.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string InferAsset(string pair)
    {
        return (pair ?? string.Empty).Contains("ETH", StringComparison.OrdinalIgnoreCase) ? "ETH" : "BTC";
    }
}

internal sealed record BackendGenericLeg(string Symbol, string Market)
{
    public bool IsSpot => string.Equals(Market, "spot", StringComparison.OrdinalIgnoreCase);
}

internal sealed class BackendGenericBotConfig
{
    public string Pair { get; init; } = "";
    public string Strategy { get; init; } = "";
    public string Mode { get; init; } = "";
    public string Asset { get; init; } = "BTC";
    public decimal Amount { get; init; }
    public decimal L1TrailSizeUsd { get; init; } = 50m;
    public BackendGenericLeg ShortLeg { get; init; } = new("", "futures");
    public BackendGenericLeg LongLeg { get; init; } = new("", "futures");
    public BackendGenericLeg FirstLeg { get; init; } = new("", "futures");
    public BackendGenericLeg HedgeLeg { get; init; } = new("", "futures");
    public string FirstSide { get; init; } = "buy";
    public string HedgeSide { get; init; } = "sell";

    public static BackendGenericBotConfig Parse(string configJson, string pair, string mode, string strategy)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(configJson) ? "{}" : configJson);
        var root = doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement : default;
        string asset = Clean(root, "asset", InferAsset(Clean(root, "pair", pair))).ToUpperInvariant() == "ETH" ? "ETH" : "BTC";
        string cleanPair = Clean(root, "pair", pair);
        decimal amount = Dec(root, "amount", Dec(root, "qty", 0.001m));
        if (string.Equals(strategy, "ddh", StringComparison.OrdinalIgnoreCase))
        {
            string[] pairLegs = NormalizePairLegs(cleanPair);
            string defaultLongLeg = pairLegs.Length >= 2 ? pairLegs[0] : asset + "USDT";
            string defaultShortLeg = pairLegs.Length >= 2 ? pairLegs[1] : asset + "USDT-PERP";
            var shortLeg = Leg(pairLegs.Length >= 2 ? defaultShortLeg : Clean(root, "shortLeg", defaultShortLeg));
            var longLeg = Leg(pairLegs.Length >= 2 ? defaultLongLeg : Clean(root, "longLeg", defaultLongLeg));
            return new BackendGenericBotConfig { Pair = cleanPair, Strategy = "ddh", Mode = Clean(root, "mode", mode), Asset = asset, Amount = amount, L1TrailSizeUsd = Dec(root, "l1TrailSizeUsd", 50m), ShortLeg = shortLeg, LongLeg = longLeg, FirstLeg = shortLeg, HedgeLeg = longLeg, FirstSide = "sell", HedgeSide = "buy" };
        }

        string side = Clean(root, "side", mode).ToLowerInvariant().Contains("buy") ? "buy" : "sell";
        string legOrder = Clean(root, "legOrder", Clean(root, "legOrderMode", mode)).ToLowerInvariant();
        bool longFirst = !legOrder.Contains("short");
        string[] legs = NormalizePairLegs(cleanPair);
        BackendGenericLeg a;
        BackendGenericLeg b;
        if (legs.Length == 2)
        {
            a = Leg(legs[0]);
            b = Leg(legs[1]);
        }
        else
        {
            a = Leg(Clean(root, "futuresSymbol", asset + "USDT-PERP"));
            b = Leg(Clean(root, "spotSymbol", asset + "USDT"));
        }
        BackendGenericLeg buyLeg;
        BackendGenericLeg sellLeg;
        if (a.IsSpot != b.IsSpot)
        {
            var spot = a.IsSpot ? a : b;
            var fut = a.IsSpot ? b : a;
            buyLeg = side == "buy" ? fut : spot;
            sellLeg = side == "buy" ? spot : fut;
        }
        else
        {
            buyLeg = side == "buy" ? a : b;
            sellLeg = side == "buy" ? b : a;
        }
        var first = longFirst ? buyLeg : sellLeg;
        var hedge = longFirst ? sellLeg : buyLeg;
        return new BackendGenericBotConfig
        {
            Pair = cleanPair,
            Strategy = "limit-limit",
            Mode = (longFirst ? "LLF" : "SLF") + "-" + (side == "buy" ? "BS" : "SS"),
            Asset = asset,
            Amount = amount,
            L1TrailSizeUsd = Dec(root, "l1TrailSizeUsd", 50m),
            ShortLeg = sellLeg,
            LongLeg = buyLeg,
            FirstLeg = first,
            HedgeLeg = hedge,
            FirstSide = Same(first, buyLeg) ? "buy" : "sell",
            HedgeSide = Same(hedge, buyLeg) ? "buy" : "sell"
        };
    }

    private static BackendGenericLeg Leg(string symbol)
    {
        string s = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        bool spot = (s == "BTCUSDT" || s == "ETHUSDT") && !s.Contains("-");
        return new BackendGenericLeg(s, spot ? "spot" : "futures");
    }

    private static bool Same(BackendGenericLeg a, BackendGenericLeg b) => string.Equals(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Market, b.Market, StringComparison.OrdinalIgnoreCase);
    private static string InferAsset(string text) => (text ?? string.Empty).ToUpperInvariant().Contains("ETH") ? "ETH" : "BTC";

    private static string[] NormalizePairLegs(string cleanPair)
    {
        string p = (cleanPair ?? string.Empty).Trim().ToUpperInvariant();
        if (p.EndsWith("_PERP_SPOT", StringComparison.OrdinalIgnoreCase))
        {
            string asset = p.Contains("ETH", StringComparison.OrdinalIgnoreCase) ? "ETH" : "BTC";
            return new[] { asset + "USDT-PERP", asset + "USDT" };
        }
        if (p.EndsWith("_SPOT_PERP", StringComparison.OrdinalIgnoreCase))
        {
            string asset = p.Contains("ETH", StringComparison.OrdinalIgnoreCase) ? "ETH" : "BTC";
            return new[] { asset + "USDT", asset + "USDT-PERP" };
        }
        return p.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string Clean(JsonElement root, string name, string fallback)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) return value.GetString()!.Trim();
            if (value.ValueKind == JsonValueKind.Number) return value.GetRawText();
        }
        return fallback;
    }

    private static decimal Dec(JsonElement root, string name, decimal fallback)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out n) ? n : fallback;
    }
}

public sealed class CoincallBackendSpreadBotHostedService : IHostedService
{
    private readonly CoincallBackendSpreadBotManager _manager;

    public CoincallBackendSpreadBotHostedService(CoincallBackendSpreadBotManager manager)
    {
        _manager = manager;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _manager.ResumeRunningAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => _manager.StopAllAsync(cancellationToken);
}
