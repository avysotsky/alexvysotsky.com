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
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;

namespace VANWebService.Models;

public sealed class BybitSnapshotHostedService : BackgroundService
{
    private const string MainBaseUrl = "https://api.bybit.com";
    private const string DemoBaseUrl = "https://api-demo.bybit.com";
    private static readonly HttpClient Http = new HttpClient();

    private readonly Enums.LogAction _log;
    private readonly BybitRepository _repo;

    public BybitSnapshotHostedService(Enums.LogAction log, BybitRepository repo)
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
            _log("[BYBIT][SNAPSHOT][SCHEMA][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);
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
            DateTime minuteUtc = TruncateToMinuteUtc(nowUtc);
            var accounts = await _repo.GetAccountsAsync(includeSecrets: true, ct);
            foreach (var account in accounts)
            {
                if (!account.storeMinuteEquity) continue;
                try
                {
                    var result = await FetchWalletBalanceAsync(account, ct);
                    if (!result.ok)
                    {
                        _log($"[BYBIT][SNAPSHOT][ACCOUNT {account.id}][ERROR] {result.message} retCode={result.retCode}", Enums.LogLevel.llBaselogic);
                        continue;
                    }

                    using var doc = result.document!;
                    var snap = ParseWalletSnapshot(doc.RootElement);
                    string accountUid = BybitRepository.BuildAccountUid(account.id.ToString(CultureInfo.InvariantCulture), account.apiKey, account.isDemo);
                    string equityCurrency = BybitRepository.NormalizeEquityCurrency(account.equityCurrency);
                    await _repo.UpsertEquityAsync(accountUid, account.name, minuteUtc, equityCurrency, snap.totalEquityUsdt, snap.availableEquityUsdt, snap.unrealizedPnlUsdt, snap.marginRatio, true, ct);
                }
                catch (Exception ex)
                {
                    _log($"[BYBIT][SNAPSHOT][ACCOUNT {account.id}][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
                }
            }
        }
        catch (Exception ex)
        {
            _log("[BYBIT][SNAPSHOT][CYCLE][ERROR] " + ex, Enums.LogLevel.llExceptions);
        }
    }

    private static async Task<(bool ok, string message, int retCode, JsonDocument? document)> FetchWalletBalanceAsync(BybitAccountRecord account, CancellationToken ct)
    {
        const string recvWindow = "5000";
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        string query = "accountType=UNIFIED";
        string signPayload = timestamp + account.apiKey + recvWindow + query;
        string sign;
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(account.apiSecret)))
        {
            sign = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signPayload))).ToLowerInvariant();
        }

        string baseUrl = account.isDemo ? DemoBaseUrl : MainBaseUrl;
        using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/v5/account/wallet-balance?" + query);
        req.Headers.TryAddWithoutValidation("X-BAPI-API-KEY", account.apiKey);
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
        decimal totalEquity = ReadDecimal(list, "totalEquity");
        decimal available = ReadDecimal(list, "totalAvailableBalance");
        decimal upl = ReadDecimal(list, "totalPerpUPL");
        decimal marginRatio = ReadDecimal(list, "accountMMRate");
        return new BybitWalletSnapshot(totalEquity, available, upl, marginRatio, new List<object>());
    }

    private static decimal ReadDecimal(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Undefined || !el.TryGetProperty(prop, out var p)) return 0m;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
        if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d)) return d;
        return 0m;
    }

    private static DateTime TruncateToMinuteUtc(DateTime utc)
    {
        var normalized = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc.ToUniversalTime(), DateTimeKind.Utc);
        return new DateTime(normalized.Year, normalized.Month, normalized.Day, normalized.Hour, normalized.Minute, 0, DateTimeKind.Utc);
    }
}
