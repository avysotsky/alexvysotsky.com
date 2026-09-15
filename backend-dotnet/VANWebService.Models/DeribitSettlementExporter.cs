using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CoreDTO.Logger;

namespace VANWebService.Models;

public sealed class DeribitSettlementExporter
{
	public sealed record DailyAccountEquityPoint(DateOnly DateUtc, double? UsdcEquity, double? EthEquity, double? BtcEquity, double? EthPrice, double? BtcPrice, double? EthEquityUsd, double? BtcEquityUsd);

	public sealed record DeribitEquityPoint(DateOnly Date, double? TotalEquityUsd, double? EthUsd);

	private record RpcRequest([property: JsonPropertyName("method")] string Method, [property: JsonPropertyName("params")] object Params, [property: JsonPropertyName("jsonrpc")] string JsonRpc = "2.0", [property: JsonPropertyName("id")] int Id = 1);

	private record AuthEnvelope([property: JsonPropertyName("result")] AuthResponse Result);

	private record AuthResponse([property: JsonPropertyName("access_token")] string AccessToken);

	private record TxLogEnvelope([property: JsonPropertyName("result")] TxLogResult? Result);

	private record TxLogResult([property: JsonPropertyName("logs")] LogEntry[]? Logs, [property: JsonPropertyName("continuation")] int? Continuation);

	private record LogEntry([property: JsonPropertyName("timestamp")] long Timestamp, [property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("currency")] string Currency, [property: JsonPropertyName("balance")] double Balance, [property: JsonPropertyName("equity")] double? Equity, [property: JsonPropertyName("price")] double? Price, [property: JsonPropertyName("instrument_name")] string? InstrumentName, [property: JsonPropertyName("instrument")] string? Instrument)
	{
		public bool CurrencyEq(string cur)
		{
			return string.Equals(Currency, cur, StringComparison.OrdinalIgnoreCase);
		}
	}

	private sealed class DayRow
	{
		public double? EthEquity { get; set; }

		public double? BtcEquity { get; set; }

		public double? UsdcEquity { get; set; }

		public double? EthPrice { get; set; }

		public double? BtcPrice { get; set; }

		public double? EthEquityUsd { get; set; }

		public double? BtcEquityUsd { get; set; }

		public double? TotalEquityUsd { get; set; }

		public double? ETHEquityPnLUsd { get; set; }

		public double? BTCEquityPnLUsd { get; set; }

		public double? USDCEquityPnLUsd { get; set; }

		public double? TotalPnLUsd { get; set; }

		public double? ETHEquityPnLPct { get; set; }

		public double? BTCEquityPnLPct { get; set; }

		public double? USDCEquityPnLPct { get; set; }

		public double? TotalPnLPct { get; set; }
	}

	private readonly string _baseUrl;

	private readonly string _clientId;

	private readonly string _clientSecret;

	private readonly DateOnly _startDate;

	private readonly DateOnly _endDate;

	private readonly string _outFile;

	private readonly int? _subaccountId;

	private double _initialInvestmentUsd;

	private readonly Enums.LogAction? _log;

	private const int TxHeadRowsToLog = 10;

	private const int TxTailRowsToLog = 10;

	private readonly JsonSerializerOptions _json = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		NumberHandling = JsonNumberHandling.AllowReadingFromString
	};

	public DeribitSettlementExporter(string baseUrl, string clientId, string clientSecret, DateOnly startDate, DateOnly endDate, string outFile, Enums.LogAction? logAction = null, int? subaccountId = null)
	{
		_baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
		if (string.IsNullOrWhiteSpace(_baseUrl))
		{
			_baseUrl = "https://www.deribit.com";
		}
		_clientId = clientId;
		_clientSecret = clientSecret;
		_startDate = startDate;
		_endDate = endDate;
		_outFile = outFile;
		_log = logAction;
		_subaccountId = subaccountId;
		switch (clientId)
		{
		case "cY6l7_ft":
			_initialInvestmentUsd = 1000.0;
			break;
		case "nNYTo8hj":
			_initialInvestmentUsd = 25286.26;
			break;
		case "cgJcQS_-":
			_initialInvestmentUsd = 11663.95;
			break;
		case "uWH9TdFR":
			_initialInvestmentUsd = 7899.0;
			break;
		case "QoMHu03M":
			_initialInvestmentUsd = 28856.0;
			break;
		case "JUXXlNql":
			_initialInvestmentUsd = 1488.0;
			break;
		case "6X5RM8BB":
			_initialInvestmentUsd = 14741.0;
			break;
		default:
			_log?.Invoke("DeribitSettlementExporter: unknown clientId (INITIAL_INVESTMENT_USD left as 0)", Enums.LogLevel.llBaselogic);
			break;
		}
	}

	public async Task ValidateCredentialsAsync()
	{
		using HttpClient http = new HttpClient(new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes(5.0)
		})
		{
			BaseAddress = new Uri(_baseUrl)
		};
		await AuthAsync(http);
	}

	public async Task<List<DailyAccountEquityPoint>> GetDailyAccountEquitySeriesAsync()
	{
		return (from kv in await BuildByDateAsync()
			orderby kv.Key
			select new DailyAccountEquityPoint(kv.Key, kv.Value.UsdcEquity, kv.Value.EthEquity, kv.Value.BtcEquity, kv.Value.EthPrice, kv.Value.BtcPrice, kv.Value.EthEquityUsd, kv.Value.BtcEquityUsd)).ToList();
	}

	public async Task<List<DeribitEquityPoint>> GetEquitySeriesAsync()
	{
		return (from kv in await BuildByDateAsync()
			orderby kv.Key
			select new DeribitEquityPoint(kv.Key, kv.Value.TotalEquityUsd, kv.Value.EthEquityUsd)).ToList();
	}

	public async Task RunAsync()
	{
		List<KeyValuePair<DateOnly, DayRow>> list = (await BuildByDateAsync()).OrderBy((KeyValuePair<DateOnly, DayRow> kv) => kv.Key).ToList();
		double initialInvestmentUsd = _initialInvestmentUsd;
		DateOnly key;
		DayRow value;
		foreach (KeyValuePair<DateOnly, DayRow> item in list)
		{
			item.Deconstruct(out key, out value);
			DayRow dayRow = value;
			dayRow.ETHEquityPnLUsd = (dayRow.EthEquityUsd.HasValue ? new double?(Math.Round(dayRow.EthEquityUsd.Value, 2, MidpointRounding.AwayFromZero)) : ((double?)null));
			dayRow.BTCEquityPnLUsd = (dayRow.BtcEquityUsd.HasValue ? new double?(Math.Round(dayRow.BtcEquityUsd.Value, 2, MidpointRounding.AwayFromZero)) : ((double?)null));
			dayRow.USDCEquityPnLUsd = (dayRow.UsdcEquity.HasValue ? new double?(Math.Round(dayRow.UsdcEquity.Value, 2, MidpointRounding.AwayFromZero)) : ((double?)null));
			dayRow.TotalPnLUsd = (dayRow.TotalEquityUsd.HasValue ? new double?(Math.Round(dayRow.TotalEquityUsd.Value - initialInvestmentUsd, 2, MidpointRounding.AwayFromZero)) : ((double?)null));
			if (initialInvestmentUsd != 0.0)
			{
				dayRow.ETHEquityPnLPct = (dayRow.ETHEquityPnLUsd.HasValue ? new double?(Math.Round(dayRow.ETHEquityPnLUsd.Value / initialInvestmentUsd * 100.0, 2, MidpointRounding.AwayFromZero)) : ((double?)null));
				dayRow.BTCEquityPnLPct = (dayRow.BTCEquityPnLUsd.HasValue ? new double?(Math.Round(dayRow.BTCEquityPnLUsd.Value / initialInvestmentUsd * 100.0, 2, MidpointRounding.AwayFromZero)) : ((double?)null));
				dayRow.USDCEquityPnLPct = (dayRow.USDCEquityPnLUsd.HasValue ? new double?(Math.Round(dayRow.USDCEquityPnLUsd.Value / initialInvestmentUsd * 100.0, 2, MidpointRounding.AwayFromZero)) : ((double?)null));
				dayRow.TotalPnLPct = (dayRow.TotalPnLUsd.HasValue ? new double?(Math.Round(dayRow.TotalPnLUsd.Value / initialInvestmentUsd * 100.0, 2, MidpointRounding.AwayFromZero)) : ((double?)null));
			}
			else
			{
				dayRow.ETHEquityPnLPct = null;
				dayRow.BTCEquityPnLPct = null;
				dayRow.USDCEquityPnLPct = null;
				dayRow.TotalPnLPct = null;
			}
		}
		using FileStream stream = File.Create(_outFile);
		using StreamWriter streamWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine(string.Join(";", "date_utc", "eth_equity", "btc_equity", "usdc_equity", "eth_price", "btc_price", "eth_equity_usd", "btc_equity_usd", "total_equity_usd", "ETHEquity_PnL_USD", "BTCEquity_PnL_USD", "USDCEquity_PnL_USD", "Total_PnL_USD", "ETHEquity_PnL_%", "BTCEquity_PnL_%", "USDCEquity_PnL_%", "Total_PnL_%"));
		foreach (KeyValuePair<DateOnly, DayRow> item2 in list)
		{
			item2.Deconstruct(out key, out value);
			DateOnly dateOnly = key;
			DayRow dayRow2 = value;
			streamWriter.WriteLine(string.Join(";", dateOnly.ToString("yyyy-MM-dd"), FormatNum(dayRow2.EthEquity), FormatNum(dayRow2.BtcEquity), FormatNum(dayRow2.UsdcEquity), FormatNum(dayRow2.EthPrice), FormatNum(dayRow2.BtcPrice), FormatNum(dayRow2.EthEquityUsd), FormatNum(dayRow2.BtcEquityUsd), FormatMoney2(dayRow2.TotalEquityUsd), FormatMoney2(dayRow2.ETHEquityPnLUsd), FormatMoney2(dayRow2.BTCEquityPnLUsd), FormatMoney2(dayRow2.USDCEquityPnLUsd), FormatMoney2(dayRow2.TotalPnLUsd), FormatMoney2(dayRow2.ETHEquityPnLPct), FormatMoney2(dayRow2.BTCEquityPnLPct), FormatMoney2(dayRow2.USDCEquityPnLPct), FormatMoney2(dayRow2.TotalPnLPct)));
		}
		_log?.Invoke("OK: " + _outFile, Enums.LogLevel.llBaselogic);
	}

	private async Task<Dictionary<DateOnly, DayRow>> BuildByDateAsync()
	{
		using HttpClient http = new HttpClient(new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes(5.0)
		})
		{
			BaseAddress = new Uri(_baseUrl)
		};
		string parameter = await AuthAsync(http);
		http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", parameter);
		var targets = (from d in EachDate(_startDate, _endDate)
			select new
			{
				Date = d,
				SettleMs = ToUnixMs(DateTime.SpecifyKind(d.ToDateTime(new TimeOnly(8, 1)), DateTimeKind.Utc))
			}).ToArray();
		long rangeStartMs = ToUnixMs(DateTime.SpecifyKind(_startDate.AddDays(-1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc));
		long rangeEndMs = ToUnixMs(DateTime.SpecifyKind(_endDate.AddDays(1).ToDateTime(new TimeOnly(23, 59, 59)), DateTimeKind.Utc));
		_log?.Invoke($"[DeribitTxLogs][START] baseUrl={_baseUrl} dates=[{_startDate:yyyy-MM-dd}..{_endDate:yyyy-MM-dd}] rangeUtc=[{UnixMsToUtc(rangeStartMs):O}..{UnixMsToUtc(rangeEndMs):O}] subaccountId={_subaccountId}", Enums.LogLevel.llBaselogic);
		List<LogEntry> ethLogs = await GetTransactionLogsAsync(http, "ETH", rangeStartMs, rangeEndMs, null, _subaccountId);
		List<LogEntry> btcLogs = await GetTransactionLogsAsync(http, "BTC", rangeStartMs, rangeEndMs, null, _subaccountId);
		List<LogEntry> list = await GetTransactionLogsAsync(http, "USDC", rangeStartMs, rangeEndMs, null, _subaccountId);
		ethLogs.Sort((LogEntry a, LogEntry b) => a.Timestamp.CompareTo(b.Timestamp));
		btcLogs.Sort((LogEntry a, LogEntry b) => a.Timestamp.CompareTo(b.Timestamp));
		list.Sort((LogEntry a, LogEntry b) => a.Timestamp.CompareTo(b.Timestamp));
		LogTxLogsSummary("ETH", ethLogs);
		LogTxLogsSummary("BTC", btcLogs);
		LogTxLogsSummary("USDC", list);
		LogTxLogsSample("ETH", ethLogs, 10, 10);
		LogTxLogsSample("BTC", btcLogs, 10, 10);
		LogTxLogsSample("USDC", list, 10, 10);
		Dictionary<DateOnly, DayRow> dictionary = new Dictionary<DateOnly, DayRow>();
		string[] preferredTypes = new string[2] { "options session pnl", "settlement" };
		var array = targets;
		foreach (var anon in array)
		{
			if (!dictionary.TryGetValue(anon.Date, out var value))
			{
				value = (dictionary[anon.Date] = new DayRow());
			}
			LogEntry logEntry = SelectEquityLogAt08(ethLogs, anon.SettleMs, 300000L, 600000L, preferredTypes);
			value.EthEquity = logEntry?.Equity;
			LogEntry logEntry2 = ((logEntry != null && IsEthPerp(logEntry) && logEntry.Price.HasValue) ? logEntry : SelectPriceLogAt08FilteredByInstrument(ethLogs, anon.SettleMs, 300000L, 600000L, "ETH-PERPETUAL", preferredTypes));
			value.EthPrice = logEntry2?.Price;
			value.EthEquityUsd = ((value.EthEquity.HasValue && value.EthPrice.HasValue) ? new double?(value.EthEquity.Value * value.EthPrice.Value) : ((double?)null));
			LogEntry logEntry3 = SelectEquityLogAt08(btcLogs, anon.SettleMs, 300000L, 600000L, preferredTypes);
			value.BtcEquity = logEntry3?.Equity;
			LogEntry logEntry4 = ((logEntry3 != null && IsBtcPerp(logEntry3) && logEntry3.Price.HasValue) ? logEntry3 : SelectPriceLogAt08FilteredByInstrument(btcLogs, anon.SettleMs, 300000L, 600000L, "BTC-PERPETUAL", preferredTypes));
			value.BtcPrice = logEntry4?.Price;
			value.BtcEquityUsd = ((value.BtcEquity.HasValue && value.BtcPrice.HasValue) ? new double?(value.BtcEquity.Value * value.BtcPrice.Value) : ((double?)null));
			LogEntry logEntry5 = SelectEquityLogAt08StrictWindow(list, anon.SettleMs, 300000L, 600000L, preferredTypes);
			value.UsdcEquity = logEntry5?.Equity;
			if (value.EthEquityUsd.HasValue || value.BtcEquityUsd.HasValue || value.UsdcEquity.HasValue)
			{
				double value2 = value.EthEquityUsd.GetValueOrDefault() + value.BtcEquityUsd.GetValueOrDefault() + value.UsdcEquity.GetValueOrDefault();
				value.TotalEquityUsd = Math.Round(value2, 2, MidpointRounding.AwayFromZero);
			}
			else
			{
				value.TotalEquityUsd = null;
			}
			LogDailySelectionAndRow(anon.Date, anon.SettleMs, logEntry, logEntry2, logEntry3, logEntry4, logEntry5, value);
		}
		_log?.Invoke($"[DeribitTxLogs][DONE] days={dictionary.Count}", Enums.LogLevel.llBaselogic);
		return dictionary;
	}

	private async Task<List<LogEntry>> GetTransactionLogsAsync(HttpClient http, string currency, long startMs, long endMs, string? filterQuery, int? subaccountId)
	{
		List<LogEntry> result = new List<LogEntry>();
		int? continuation = null;
		while (true)
		{
			Dictionary<string, object> dictionary = new Dictionary<string, object>
			{
				["currency"] = currency,
				["start_timestamp"] = startMs,
				["end_timestamp"] = endMs,
				["count"] = 250
			};
			if (!string.IsNullOrWhiteSpace(filterQuery))
			{
				dictionary["query"] = filterQuery;
			}
			if (subaccountId.HasValue)
			{
				dictionary["subaccount_id"] = subaccountId.Value;
			}
			if (continuation.HasValue)
			{
				dictionary["continuation"] = continuation.Value;
			}
			RpcRequest req = new RpcRequest("private/get_transaction_log", dictionary);
			TxLogEnvelope txLogEnvelope;
			try
			{
				txLogEnvelope = await PostAsync<TxLogEnvelope>(http, req);
			}
			catch (Exception ex)
			{
				_log?.Invoke("[DeribitTxLogs][" + currency + "][ERROR] fetch failed: " + ex.Message, Enums.LogLevel.llExceptions);
				break;
			}
			if (txLogEnvelope.Result?.Logs != null)
			{
				result.AddRange(txLogEnvelope.Result.Logs.Where((LogEntry l) => l.CurrencyEq(currency)));
			}
			if (!(txLogEnvelope.Result?.Continuation).HasValue)
			{
				break;
			}
			continuation = txLogEnvelope.Result.Continuation;
			await Task.Delay(50);
		}
		return result;
	}

	private static LogEntry? SelectLogAt08ForEquity(List<LogEntry> logs, long targetMs, long winAfterMs, long winBeforeMs, string[] preferredTypes)
	{
		if (logs.Count == 0)
		{
			return null;
		}
		HashSet<string> pref = preferredTypes.Select(NormType).ToHashSet();
		LogEntry logEntry = logs.FirstOrDefault((LogEntry l) => l.Timestamp > targetMs && l.Timestamp <= targetMs + winAfterMs && HasPref(l) && l.Equity.HasValue);
		if (logEntry != null)
		{
			return logEntry;
		}
		LogEntry logEntry2 = logs.LastOrDefault((LogEntry l) => l.Timestamp >= targetMs - winBeforeMs && l.Timestamp <= targetMs && HasPref(l) && l.Equity.HasValue);
		if (logEntry2 != null)
		{
			return logEntry2;
		}
		LogEntry logEntry3 = logs.FirstOrDefault((LogEntry l) => l.Timestamp > targetMs && l.Equity.HasValue);
		if (logEntry3 != null)
		{
			return logEntry3;
		}
		return logs.LastOrDefault((LogEntry l) => l.Timestamp <= targetMs && l.Equity.HasValue);
		bool HasPref(LogEntry l)
		{
			if (pref.Count != 0)
			{
				return pref.Contains(NormType(l.Type));
			}
			return true;
		}
	}

	private static LogEntry? SelectEquityLogAt08(List<LogEntry> logs, long targetMs, long winAfterMs, long winBeforeMs, string[] preferredTypes)
	{
		return SelectLogAt08ForEquity(logs, targetMs, winAfterMs, winBeforeMs, preferredTypes);
	}

	private static LogEntry? SelectEquityLogAt08StrictWindow(List<LogEntry> logs, long targetMs, long winAfterMs, long winBeforeMs, string[] preferredTypes)
	{
		if (logs.Count == 0)
		{
			return null;
		}
		HashSet<string> pref = preferredTypes.Select(NormType).ToHashSet();
		LogEntry logEntry = logs.FirstOrDefault((LogEntry l) => l.Timestamp > targetMs && l.Timestamp <= targetMs + winAfterMs && HasPref(l) && l.Equity.HasValue);
		if (logEntry != null)
		{
			return logEntry;
		}
		LogEntry logEntry2 = logs.LastOrDefault((LogEntry l) => l.Timestamp >= targetMs - winBeforeMs && l.Timestamp <= targetMs && HasPref(l) && l.Equity.HasValue);
		if (logEntry2 != null)
		{
			return logEntry2;
		}
		return null;
		bool HasPref(LogEntry l)
		{
			if (pref.Count != 0)
			{
				return pref.Contains(NormType(l.Type));
			}
			return true;
		}
	}

	private static LogEntry? SelectPriceLogAt08FilteredByInstrument(List<LogEntry> logs, long targetMs, long winAfterMs, long winBeforeMs, string instrumentName, string[] preferredTypes)
	{
		if (logs.Count == 0)
		{
			return null;
		}
		HashSet<string> pref = preferredTypes.Select(NormType).ToHashSet();
		LogEntry logEntry = logs.FirstOrDefault((LogEntry l) => l.Timestamp > targetMs && l.Timestamp <= targetMs + winAfterMs && HasPref(l) && IsInstr(l) && l.Price.HasValue);
		if (logEntry != null)
		{
			return logEntry;
		}
		LogEntry logEntry2 = logs.LastOrDefault((LogEntry l) => l.Timestamp >= targetMs - winBeforeMs && l.Timestamp <= targetMs && HasPref(l) && IsInstr(l) && l.Price.HasValue);
		if (logEntry2 != null)
		{
			return logEntry2;
		}
		LogEntry logEntry3 = logs.FirstOrDefault((LogEntry l) => l.Timestamp > targetMs && IsInstr(l) && l.Price.HasValue);
		if (logEntry3 != null)
		{
			return logEntry3;
		}
		return logs.LastOrDefault((LogEntry l) => l.Timestamp <= targetMs && IsInstr(l) && l.Price.HasValue);
		bool HasPref(LogEntry l)
		{
			if (pref.Count != 0)
			{
				return pref.Contains(NormType(l.Type));
			}
			return true;
		}
		bool IsInstr(LogEntry l)
		{
			return string.Equals(GetInstr(l), instrumentName, StringComparison.OrdinalIgnoreCase);
		}
	}

	private static bool IsEthPerp(LogEntry l)
	{
		return string.Equals(GetInstr(l), "ETH-PERPETUAL", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsBtcPerp(LogEntry l)
	{
		return string.Equals(GetInstr(l), "BTC-PERPETUAL", StringComparison.OrdinalIgnoreCase);
	}

	private static string GetInstr(LogEntry l)
	{
		if (!string.IsNullOrWhiteSpace(l.InstrumentName))
		{
			return l.InstrumentName;
		}
		return l.Instrument ?? "";
	}

	private static string NormType(string? s)
	{
		return (s ?? "").Trim().ToLowerInvariant();
	}

	private static IEnumerable<DateOnly> EachDate(DateOnly start, DateOnly end)
	{
		DateOnly d = start;
		while (d <= end)
		{
			yield return d;
			d = d.AddDays(1);
		}
	}

	private static long ToUnixMs(DateTime dtUtc)
	{
		return new DateTimeOffset(dtUtc).ToUnixTimeMilliseconds();
	}

	private void LogTxLogsSummary(string cur, List<LogEntry> logs)
	{
		if (_log == null)
		{
			return;
		}
		if (logs.Count == 0)
		{
			_log("[DeribitTxLogs][" + cur + "][SUMMARY] count=0", Enums.LogLevel.llBaselogic);
			return;
		}
		long timestamp = logs.First().Timestamp;
		long timestamp2 = logs.Last().Timestamp;
		int value = logs.Count((LogEntry l) => !l.Equity.HasValue);
		int value2 = logs.Count((LogEntry l) => !l.Price.HasValue);
		IEnumerable<string> values = from g in (from l in logs
				group l by NormType(l.Type) into g
				orderby g.Count() descending
				select g).Take(10)
			select $"{g.Key}:{g.Count()}";
		_log($"[DeribitTxLogs][{cur}][SUMMARY] count={logs.Count} utc=[{UnixMsToUtc(timestamp):O}..{UnixMsToUtc(timestamp2):O}] nulls(eq={value}, px={value2}) topTypes={string.Join(", ", values)}", Enums.LogLevel.llBaselogic);
	}

	private void LogTxLogsSample(string cur, List<LogEntry> logs, int head, int tail)
	{
		if (_log == null || logs.Count == 0)
		{
			return;
		}
		foreach (LogEntry item in logs.Take(Math.Max(0, head)))
		{
			LogOne("HEAD", item);
		}
		if (logs.Count <= head)
		{
			return;
		}
		foreach (LogEntry item2 in logs.Skip(Math.Max(head, logs.Count - Math.Max(0, tail))))
		{
			LogOne("TAIL", item2);
		}
		void LogOne(string tag, LogEntry l)
		{
			_log($"[DeribitTxLogs][{cur}][{tag}] ts={UnixMsToUtc(l.Timestamp):O} type={NormType(l.Type)} eq={FormatNum(l.Equity)} px={FormatNum(l.Price)} instr={GetInstr(l)}", Enums.LogLevel.llBaselogic);
		}
	}

	private void LogDailySelectionAndRow(DateOnly date, long settleMs, LogEntry? ethEqLog, LogEntry? ethPriceLog, LogEntry? btcEqLog, LogEntry? btcPriceLog, LogEntry? usdcEqLog, DayRow row)
	{
		if (_log != null)
		{
			_log($"[DeribitDaily][{date:yyyy-MM-dd}][TARGET] settleUtc={UnixMsToUtc(settleMs):O}", Enums.LogLevel.llBaselogic);
			_log($"[DeribitDaily][{date:yyyy-MM-dd}][SEL][ETH_EQ] {L(ethEqLog)}", Enums.LogLevel.llBaselogic);
			_log($"[DeribitDaily][{date:yyyy-MM-dd}][SEL][ETH_PX] {L(ethPriceLog)}", Enums.LogLevel.llBaselogic);
			_log($"[DeribitDaily][{date:yyyy-MM-dd}][SEL][BTC_EQ] {L(btcEqLog)}", Enums.LogLevel.llBaselogic);
			_log($"[DeribitDaily][{date:yyyy-MM-dd}][SEL][BTC_PX] {L(btcPriceLog)}", Enums.LogLevel.llBaselogic);
			_log($"[DeribitDaily][{date:yyyy-MM-dd}][SEL][USDC_EQ] {L(usdcEqLog)}", Enums.LogLevel.llBaselogic);
			_log($"[DeribitDaily][{date:yyyy-MM-dd}][ROW] usdc={FormatNum(row.UsdcEquity)} eth={FormatNum(row.EthEquity)} btc={FormatNum(row.BtcEquity)} eth_px={FormatNum(row.EthPrice)} btc_px={FormatNum(row.BtcPrice)} eth_usd={FormatNum(row.EthEquityUsd)} btc_usd={FormatNum(row.BtcEquityUsd)} total={FormatMoney2(row.TotalEquityUsd)}", Enums.LogLevel.llBaselogic);
		}
		static string L(LogEntry? l)
		{
			if ((object)l != null)
			{
				return $"ts={UnixMsToUtc(l.Timestamp):O} type={NormType(l.Type)} eq={FormatNum(l.Equity)} px={FormatNum(l.Price)} instr={GetInstr(l)}";
			}
			return "NULL";
		}
	}

	private static DateTime UnixMsToUtc(long ms)
	{
		return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
	}

	private async Task<string> AuthAsync(HttpClient http)
	{
		RpcRequest req = new RpcRequest("public/auth", new
		{
			grant_type = "client_credentials",
			client_id = _clientId,
			client_secret = _clientSecret
		});
		return (await PostAsync<AuthEnvelope>(http, req)).Result.AccessToken;
	}

	private async Task<T> PostAsync<T>(HttpClient http, RpcRequest req)
	{
		using HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, "/api/v2/" + req.Method);
		msg.Content = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
		using HttpResponseMessage resp = await http.SendAsync(msg);
		byte[] array = await resp.Content.ReadAsByteArrayAsync();
		if (!resp.IsSuccessStatusCode)
		{
			string value = Encoding.UTF8.GetString(array);
			_log?.Invoke($"HTTP {resp.StatusCode} {resp.StatusCode} on {req.Method}: {value}", Enums.LogLevel.llExceptions);
			throw new HttpRequestException($"Deribit HTTP {resp.StatusCode} {resp.StatusCode} on {req.Method}: {value}");
		}
		return JsonSerializer.Deserialize<T>(array, _json);
	}

	private static string FormatNum(double? x)
	{
		if (!x.HasValue)
		{
			return "";
		}
		return x.Value.ToString("0.##########", CultureInfo.InvariantCulture);
	}

	private static string FormatMoney2(double? x)
	{
		if (!x.HasValue)
		{
			return "";
		}
		return x.Value.ToString("0.00", CultureInfo.InvariantCulture);
	}
}
