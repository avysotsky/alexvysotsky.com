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

public sealed class CoincallFundingHostedService : BackgroundService
{
    private static readonly FundingSymbol[] Symbols =
    {
        new("BTCUSD", "BTCUSDT-Perp", new[] { "BTCUSD", "BTCUSDT-Perp", "BTCUSDT" }),
        new("ETHUSD", "ETHUSDT-Perp", new[] { "ETHUSD", "ETHUSDT-Perp", "ETHUSDT" })
    };

    private readonly Enums.LogAction _log;
    private readonly CoincallRepository _repo;

    public CoincallFundingHostedService(Enums.LogAction log, CoincallRepository repo)
    {
        _log = log;
        _repo = repo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await _repo.EnsureSchemaAsync(ct);
            var account = await _repo.GetActiveAccountAsync(ct);
            if (account == null) return;

            var client = new CoincallApiClient(account);
            DateTime observedMinuteUtc = CoincallRepository.TruncateToMinuteUtc(DateTime.UtcNow);
            foreach (var symbol in Symbols)
            {
                var rows = await LoadSymbolRowsAsync(client, symbol, observedMinuteUtc, ct);
                foreach (var row in rows)
                {
                    await _repo.UpsertFuturesFundingHistoryAsync(row, ct);
                }
            }

            foreach (var row in await LoadCurrentFundingRowsAsync(client, observedMinuteUtc, ct))
            {
                await _repo.UpsertFuturesFundingMinuteAsync(row, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _log($"[COINCALL][FUNDING][ERROR] {ex.Message}", Enums.LogLevel.llBaselogic);
        }
    }

    private static async Task<List<CoincallFuturesFundingRow>> LoadSymbolRowsAsync(CoincallApiClient client, FundingSymbol symbol, DateTime observedMinuteUtc, CancellationToken ct)
    {
        Exception? lastError = null;
        foreach (var apiSymbol in symbol.ApiSymbols)
        {
            try
            {
                using var doc = await client.GetFuturesFundingHistoryAsync(apiSymbol, 50, 1, 0, 0, ct);
                var rows = ExtractFundingRows(doc.RootElement, symbol, observedMinuteUtc).ToList();
                if (rows.Count > 0) return rows;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }
        if (lastError != null) throw lastError;
        return new List<CoincallFuturesFundingRow>();
    }

    private static IEnumerable<CoincallFuturesFundingRow> ExtractFundingRows(JsonElement root, FundingSymbol requestedSymbol, DateTime observedMinuteUtc)
    {
        foreach (var item in EnumerateRows(root))
        {
            string rawJson = item.GetRawText();
            string symbol = ReadString(item, "symbol", "instrument", "instrumentName");
            if (string.IsNullOrWhiteSpace(symbol)) symbol = requestedSymbol.Symbol;
            symbol = NormalizeFundingSymbol(symbol);
            if (!string.Equals(symbol, requestedSymbol.Symbol, StringComparison.OrdinalIgnoreCase)) continue;

            string recordId = ReadString(item, "id", "recordId", "settleId", "orderId", "tradeId", "billId", "transactionId");
            DateTime? recordTimeUtc = ReadTime(item, "ctime", "time", "ts", "createTime", "createdTime", "fundingTime", "settleTime", "recordTime", "updatedTime");
            long? recordMs = recordTimeUtc.HasValue ? new DateTimeOffset(recordTimeUtc.Value).ToUnixTimeMilliseconds() : ReadLong(item, "ctime", "time", "ts", "createTime", "createdTime", "fundingTime", "settleTime", "recordTime", "updatedTime");
            string recordKey = BuildRecordKey(requestedSymbol.Symbol, observedMinuteUtc, recordId, recordMs);

            yield return new CoincallFuturesFundingRow
            {
                symbol = requestedSymbol.Symbol,
                displayName = requestedSymbol.DisplayName,
                recordKey = recordKey,
                recordId = recordId,
                observedMinuteUtc = observedMinuteUtc,
                recordTimeUtc = recordTimeUtc,
                tradeSide = ReadInt(item, "tradeSide", "side"),
                qty = ReadDecimal(item, "qty", "quantity", "amount"),
                fundFee = ReadDecimal(item, "fundFee", "funding", "fee", "fundingFee"),
                fundRate = ReadDecimal(item, "fundRate", "fundingRate"),
                interest8h = ReadDecimal(item, "interest_8h", "interest8h", "interestRate8h"),
                rawJson = rawJson
            };
        }
    }

    private static async Task<List<CoincallFuturesFundingRow>> LoadCurrentFundingRowsAsync(CoincallApiClient client, DateTime observedMinuteUtc, CancellationToken ct)
    {
        using var doc = await client.GetFuturesPublicFundingRateAsync("BTCUSD,ETHUSD", ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return new List<CoincallFuturesFundingRow>();
        var wanted = Symbols.ToDictionary(x => x.Symbol, StringComparer.OrdinalIgnoreCase);
        var rows = new List<CoincallFuturesFundingRow>();
        foreach (var item in data.EnumerateArray())
        {
            string symbol = NormalizeFundingSymbol(ReadString(item, "symbol", "ticker_id", "instrument", "instrumentName"));
            if (!wanted.TryGetValue(symbol, out var requestedSymbol)) continue;

            rows.Add(new CoincallFuturesFundingRow
            {
                symbol = requestedSymbol.Symbol,
                displayName = requestedSymbol.DisplayName,
                recordKey = "market:" + CoincallRepository.TruncateToMinuteUtc(observedMinuteUtc).Ticks.ToString(CultureInfo.InvariantCulture),
                recordId = "",
                observedMinuteUtc = observedMinuteUtc,
                recordTimeUtc = observedMinuteUtc,
                tradeSide = null,
                qty = null,
                fundFee = null,
                fundRate = ReadDecimal(item, "rate", "funding_rate", "fundingRate", "current_funding", "currentFunding"),
                interest8h = null,
                rawJson = item.GetRawText()
            });
        }
        return rows;
    }

    private static string BuildRecordKey(string symbol, DateTime observedMinuteUtc, string recordId, long? recordMs)
    {
        if (!string.IsNullOrWhiteSpace(recordId)) return "id:" + recordId.Trim();
        if (recordMs.HasValue && recordMs.Value > 0) return "time:" + recordMs.Value.ToString(CultureInfo.InvariantCulture);
        return "observed:" + CoincallRepository.TruncateToMinuteUtc(observedMinuteUtc).Ticks.ToString(CultureInfo.InvariantCulture);
    }

    private static IEnumerable<JsonElement> EnumerateRows(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) yield return item;
            yield break;
        }
        if (value.ValueKind != JsonValueKind.Object) yield break;
        foreach (var name in new[] { "list", "rows", "items", "records", "result" })
        {
            if (value.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in child.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) yield return item;
                yield break;
            }
        }
        if (value.TryGetProperty("data", out var data))
        {
            foreach (var item in EnumerateRows(data)) yield return item;
            yield break;
        }
        if (value.TryGetProperty("symbol", out _) || value.TryGetProperty("fundFee", out _) || value.TryGetProperty("fundRate", out _)) yield return value;
    }

    private static string NormalizeFundingSymbol(string value)
    {
        string s = (value ?? string.Empty).Trim().ToUpperInvariant().Replace("USDT-PERP", "USD").Replace("USDT PERP", "USD");
        s = s.Replace("-", "");
        if (s == "BTCUSDT" || s == "BTCUSDTPERP") return "BTCUSD";
        if (s == "ETHUSDT" || s == "ETHUSDTPERP") return "ETHUSD";
        return s;
    }

    private static string ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value)) continue;
            string text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }
        return string.Empty;
    }

    private static decimal? ReadDecimal(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n)) return n;
            if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        }
        return null;
    }

    private static int? ReadInt(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n)) return n;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        }
        return null;
    }

    private static long? ReadLong(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)) return n;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)) return s;
        }
        return null;
    }

    private static DateTime? ReadTime(JsonElement item, params string[] names)
    {
        long? raw = ReadLong(item, names);
        if (raw.HasValue && raw.Value > 0)
        {
            long value = raw.Value;
            if (value < 10_000_000_000L) value *= 1000;
            try { return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime; }
            catch (ArgumentOutOfRangeException) { }
        }
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) continue;
            if (DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) return parsed.UtcDateTime;
        }
        return null;
    }

    private sealed record FundingSymbol(string Symbol, string DisplayName, IReadOnlyList<string> ApiSymbols);
}
