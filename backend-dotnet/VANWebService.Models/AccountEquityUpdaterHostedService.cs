using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;
using Npgsql;
using NpgsqlTypes;
using VANWebService.Security;

namespace VANWebService.Models;

public sealed class AccountEquityUpdaterHostedService : BackgroundService
{
	private sealed record AccountRow(int AccId, string ClientId, string? SecretCipher, string? SecretHashLegacy);

	private sealed record AccountEquityRow(int AccId, DateTime TimestampUtc, double? EqUsdcUsd, double? EqBtcUsd, double? EqEthUsd);

	private record RpcRequest([property: JsonPropertyName("method")] string Method, [property: JsonPropertyName("params")] object Params, [property: JsonPropertyName("jsonrpc")] string JsonRpc = "2.0", [property: JsonPropertyName("id")] int Id = 1);

	private record AuthEnvelope([property: JsonPropertyName("result")] AuthResult? Result);

	private record AuthResult([property: JsonPropertyName("access_token")] string AccessToken);

	private record AccountSummaryEnvelope([property: JsonPropertyName("result")] AccountSummaryResult? Result);

	private record AccountSummaryResult([property: JsonPropertyName("equity")] double Equity);

	private record TickerEnvelope([property: JsonPropertyName("result")] TickerResult? Result);

	private record TickerResult([property: JsonPropertyName("mark_price")] double MarkPrice, [property: JsonPropertyName("timestamp")] long? Timestamp);

	private readonly VANWebServiceConfig _config;

	private readonly Enums.LogAction logAction;

	private const string DeribitBaseUrl = "https://www.deribit.com";

	private readonly HttpClient _http;

	private const int LeadMilliseconds = 150;

	private static readonly JsonSerializerOptions _json = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public AccountEquityUpdaterHostedService(VANWebServiceConfig config, Enums.LogAction log)
	{
		_config = config;
		logAction = log;
		_http = new HttpClient(new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes(5.0)
		})
		{
			BaseAddress = new Uri("https://www.deribit.com")
		};
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logAction("[AccountEquity] Background loop started", Enums.LogLevel.llBaselogic);
		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				TimeSpan timeSpan = DelayToNextMinuteUtc(150);
				if (timeSpan > TimeSpan.Zero)
				{
					await Task.Delay(timeSpan, stoppingToken);
				}
			}
			catch (TaskCanceledException)
			{
				break;
			}
			DateTime startedAt = DateTime.UtcNow;
			try
			{
				await TickOnceAsync(stoppingToken);
			}
			catch (Exception value)
			{
				logAction($"[AccountEquity][ERROR] {value}", Enums.LogLevel.llExceptions);
			}
			TimeSpan timeSpan2 = DateTime.UtcNow - startedAt;
			if (timeSpan2 > TimeSpan.FromMinutes(1.0))
			{
				try
				{
					await SendSlowTickAlertAsync(timeSpan2, stoppingToken);
				}
				catch (Exception ex2)
				{
					logAction("[AccountEquity][ALERT][ERROR] " + ex2.Message, Enums.LogLevel.llExceptions);
				}
			}
		}
		logAction("[AccountEquity] Background loop stopped", Enums.LogLevel.llBaselogic);
	}

	private static TimeSpan DelayToNextMinuteUtc(int leadMs)
	{
		DateTime utcNow = DateTime.UtcNow;
		DateTime dateTime = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute, 0, DateTimeKind.Utc).AddMinutes(1.0);
		DateTime dateTime2 = dateTime - TimeSpan.FromMilliseconds(leadMs);
		TimeSpan timeSpan = dateTime2 - utcNow;
		if (!(timeSpan < TimeSpan.Zero))
		{
			return timeSpan;
		}
		return TimeSpan.Zero;
	}

	private async Task TickOnceAsync(CancellationToken ct)
	{
		await using NpgsqlConnection conn = new NpgsqlConnection(_config.AuthorityConnectionString);
		await conn.OpenAsync(ct);
		List<AccountRow> accounts = new List<AccountRow>();
		await using (NpgsqlCommand cmd = new NpgsqlCommand("\nselect acc_id,\n       api_public_key,\n       api_secret_cipher,\n       api_secret_hash\nfrom public.van_account\nwhere api_public_key is not null\n  and (api_secret_cipher is not null or api_secret_hash is not null)\norder by acc_id;\n", conn))
		{
			await using NpgsqlDataReader rdr = await cmd.ExecuteReaderAsync(ct);
			while (await rdr.ReadAsync(ct))
			{
				accounts.Add(new AccountRow(rdr.GetInt32(0), rdr.GetString(1), rdr.IsDBNull(2) ? null : rdr.GetString(2), rdr.IsDBNull(3) ? null : rdr.GetString(3)));
			}
		}
		if (accounts.Count == 0)
		{
			logAction("[AccountEquity] No accounts with API keys, skip.", Enums.LogLevel.llBaselogic);
			return;
		}
		int num = ((_config.AccountEquityMaxParallel <= 0) ? 10 : _config.AccountEquityMaxParallel);
		logAction($"[AccountEquity] Tick, accounts with API keys: {accounts.Count}, maxParallel={num}", Enums.LogLevel.llBaselogic);
		ConcurrentBag<AccountEquityRow> bag = new ConcurrentBag<AccountEquityRow>();
		await Parallel.ForEachAsync(accounts, new ParallelOptions
		{
			MaxDegreeOfParallelism = num,
			CancellationToken = ct
		}, async delegate(AccountRow a, CancellationToken token)
		{
			AccountEquityRow accountEquityRow = await ProcessAccountAsync(a, token);
			if (accountEquityRow != null)
			{
				bag.Add(accountEquityRow);
			}
		});
		await using NpgsqlCommand upsertCmd = new NpgsqlCommand("\ninsert into public.van_account_equity\n(acc_id, timestamp_utc,\n equity_usdc_usd, equity_btc_usd, equity_eth_usd)\nvalues\n(@acc_id, @ts_utc,\n @eq_usdc, @eq_btc, @eq_eth)\non conflict (acc_id, minute_utc)\ndo update set\n  timestamp_utc    = excluded.timestamp_utc,\n  equity_usdc_usd  = excluded.equity_usdc_usd,\n  equity_btc_usd   = excluded.equity_btc_usd,\n  equity_eth_usd   = excluded.equity_eth_usd;\n", conn);
		NpgsqlParameter pAcc = upsertCmd.Parameters.Add("@acc_id", NpgsqlDbType.Integer);
		NpgsqlParameter pTs = upsertCmd.Parameters.Add("@ts_utc", NpgsqlDbType.TimestampTz);
		NpgsqlParameter pUsdc = upsertCmd.Parameters.Add("@eq_usdc", NpgsqlDbType.Double);
		NpgsqlParameter pBtc = upsertCmd.Parameters.Add("@eq_btc", NpgsqlDbType.Double);
		NpgsqlParameter pEth = upsertCmd.Parameters.Add("@eq_eth", NpgsqlDbType.Double);
		foreach (AccountEquityRow item in bag)
		{
			pAcc.Value = item.AccId;
			pTs.Value = item.TimestampUtc;
			pUsdc.Value = ((object)item.EqUsdcUsd) ?? DBNull.Value;
			pBtc.Value = ((object)item.EqBtcUsd) ?? DBNull.Value;
			pEth.Value = ((object)item.EqEthUsd) ?? DBNull.Value;
			await upsertCmd.ExecuteNonQueryAsync(ct);
		}
	}

	private async Task<AccountEquityRow?> ProcessAccountAsync(AccountRow a, CancellationToken ct)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(a.SecretCipher))
			{
				logAction($"[AccountEquity] acc {a.AccId} SKIP: api_secret_cipher is NULL/empty. (api_secret_hash legacy='{Short(a.SecretHashLegacy)}')", Enums.LogLevel.llExceptions);
				return null;
			}
			if (a.SecretCipher.StartsWith("PBKDF2.", StringComparison.OrdinalIgnoreCase))
			{
				logAction($"[AccountEquity] acc {a.AccId} SKIP: api_secret_cipher contains PBKDF2 hash, not cipher. Fill api_secret_cipher with encrypted Deribit secret.", Enums.LogLevel.llExceptions);
				return null;
			}
			string clientSecret;
			try
			{
				clientSecret = ApiSecretCrypto.Decrypt(a.SecretCipher);
			}
			catch (FormatException)
			{
				logAction($"[AccountEquity] acc {a.AccId} SKIP: api_secret_cipher is not valid base64 cipher. Fill correct encrypted secret.", Enums.LogLevel.llExceptions);
				return null;
			}
			Dictionary<string, double?> dictionary = await FetchEquitiesUsdAsync(_http, a.ClientId, clientSecret, ct);
			dictionary.TryGetValue("USDC", out var value);
			dictionary.TryGetValue("BTC", out var value2);
			dictionary.TryGetValue("ETH", out var value3);
			DateTime utcNow = DateTime.UtcNow;
			logAction($"[AccountEquity] acc {a.AccId}: USDC={value?.ToString("0.00") ?? "null"} USD, BTC→USD={value2?.ToString("0.00") ?? "null"} USD, ETH→USD={value3?.ToString("0.00") ?? "null"} USD, ts={utcNow:O} (TOTAL in DB)", Enums.LogLevel.llBaselogic);
			return new AccountEquityRow(a.AccId, utcNow, value, value2, value3);
		}
		catch (Exception value4)
		{
			logAction($"[AccountEquity] acc {a.AccId} ERROR: {value4}", Enums.LogLevel.llExceptions);
			return null;
		}
	}

	private async Task<Dictionary<string, double?>> FetchEquitiesUsdAsync(HttpClient http, string clientId, string clientSecret, CancellationToken ct)
	{
		string token = await AuthAsync(http, clientId, clientSecret, ct);
		if (string.IsNullOrWhiteSpace(token))
		{
			return new Dictionary<string, double?>();
		}
		double? eqUsdc = await GetEquityAsync(http, "USDC", token, ct);
		double? eqBtc = await GetEquityAsync(http, "BTC", token, ct);
		double? eqEth = await GetEquityAsync(http, "ETH", token, ct);
		TickerResult btcTicker = await GetPerpMarkPriceAsync(http, "BTC-PERPETUAL", ct);
		TickerResult tickerResult = await GetPerpMarkPriceAsync(http, "ETH-PERPETUAL", ct);
		long num = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		long? num2 = btcTicker?.Timestamp;
		if (num2.HasValue)
		{
			long valueOrDefault = num2.GetValueOrDefault();
			if ((double)(num - valueOrDefault) / 1000.0 > 90.0)
			{
				double value = (double)(num - valueOrDefault) / 1000.0;
				logAction($"[AccountEquity] SKIP: BTC-PERPETUAL ticker stale by {value:0}s (>{90}s)", Enums.LogLevel.llBaselogic);
				return new Dictionary<string, double?>();
			}
		}
		num2 = tickerResult?.Timestamp;
		if (num2.HasValue)
		{
			long valueOrDefault2 = num2.GetValueOrDefault();
			if ((double)(num - valueOrDefault2) / 1000.0 > 90.0)
			{
				double value2 = (double)(num - valueOrDefault2) / 1000.0;
				logAction($"[AccountEquity] SKIP: ETH-PERPETUAL ticker stale by {value2:0}s (>{90}s)", Enums.LogLevel.llBaselogic);
				return new Dictionary<string, double?>();
			}
		}
		Dictionary<string, double?> obj = new Dictionary<string, double?> { ["USDC"] = eqUsdc };
		double? value3;
		if (eqBtc.HasValue)
		{
			double? num3 = btcTicker?.MarkPrice;
			if (num3.HasValue)
			{
				double valueOrDefault3 = num3.GetValueOrDefault();
				value3 = eqBtc.Value * valueOrDefault3;
				goto IL_053a;
			}
		}
		value3 = null;
		goto IL_053a;
		IL_053a:
		obj["BTC"] = value3;
		double? value4;
		if (eqEth.HasValue)
		{
			double? num3 = tickerResult?.MarkPrice;
			if (num3.HasValue)
			{
				double valueOrDefault4 = num3.GetValueOrDefault();
				value4 = eqEth.Value * valueOrDefault4;
				goto IL_05a1;
			}
		}
		value4 = null;
		goto IL_05a1;
		IL_05a1:
		obj["ETH"] = value4;
		return obj;
	}

	private async Task<double?> GetEquityAsync(HttpClient http, string currency, string bearerToken, CancellationToken ct)
	{
		RpcRequest req = new RpcRequest("private/get_account_summary", new
		{
			currency = currency,
			extended = true
		});
		return (await PostAsync<AccountSummaryEnvelope>(http, req, bearerToken, ct)).Result?.Equity;
	}

	private async Task<TickerResult?> GetPerpMarkPriceAsync(HttpClient http, string instrumentName, CancellationToken ct)
	{
		string requestUri = "/api/v2/public/ticker?instrument_name=" + Uri.EscapeDataString(instrumentName);
		using HttpResponseMessage resp = await http.GetAsync(requestUri, ct);
		byte[] array = await resp.Content.ReadAsByteArrayAsync(ct);
		if (!resp.IsSuccessStatusCode)
		{
			string value = Encoding.UTF8.GetString(array);
			logAction($"[AccountEquity] HTTP {resp.StatusCode} {resp.StatusCode} on public/ticker: {value}", Enums.LogLevel.llExceptions);
			throw new HttpRequestException($"Deribit HTTP {resp.StatusCode} {resp.StatusCode} on public/ticker: {value}");
		}
		TickerEnvelope tickerEnvelope = JsonSerializer.Deserialize<TickerEnvelope>(array, _json);
		return tickerEnvelope.Result;
	}

	private async Task<string?> AuthAsync(HttpClient http, string clientId, string clientSecret, CancellationToken ct)
	{
		RpcRequest req = new RpcRequest("public/auth", new
		{
			grant_type = "client_credentials",
			client_id = clientId,
			client_secret = clientSecret
		});
		return (await PostAsync<AuthEnvelope>(http, req, null, ct)).Result?.AccessToken;
	}

	private async Task<T> PostAsync<T>(HttpClient http, RpcRequest req, string? bearerToken, CancellationToken ct)
	{
		using HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, "/api/v2/" + req.Method);
		if (!string.IsNullOrWhiteSpace(bearerToken))
		{
			msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
		}
		msg.Content = new StringContent(JsonSerializer.Serialize(req, _json), Encoding.UTF8, "application/json");
		using HttpResponseMessage resp = await http.SendAsync(msg, ct);
		byte[] array = await resp.Content.ReadAsByteArrayAsync(ct);
		if (!resp.IsSuccessStatusCode)
		{
			string value = Encoding.UTF8.GetString(array);
			logAction($"[AccountEquity] HTTP {resp.StatusCode} {resp.StatusCode} on {req.Method}: {value}", Enums.LogLevel.llExceptions);
			throw new HttpRequestException($"Deribit HTTP {resp.StatusCode} {resp.StatusCode} on {req.Method}: {value}");
		}
		return JsonSerializer.Deserialize<T>(array, _json);
	}

	private async Task SendSlowTickAlertAsync(TimeSpan elapsed, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(_config.TelegramBotToken) || string.IsNullOrWhiteSpace(_config.TelegramChatId))
		{
			logAction($"[AccountEquity][ALERT] Tick took {elapsed.TotalSeconds:0} sec (>60), Telegram not configured", Enums.LogLevel.llBaselogic);
			return;
		}
		using HttpClient http = new HttpClient();
		string value = "NDH AccountEquityUpdater SLOW TICK\n" + $"Elapsed: {elapsed.TotalSeconds:0} seconds (> 60)\n" + $"Time (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
		string requestUri = "https://api.telegram.org/bot" + _config.TelegramBotToken + "/sendMessage";
		Dictionary<string, string> nameValueCollection = new Dictionary<string, string>
		{
			["chat_id"] = _config.TelegramChatId,
			["text"] = value
		};
		using HttpResponseMessage resp = await http.PostAsync(requestUri, new FormUrlEncodedContent(nameValueCollection), ct);
		string value2 = await resp.Content.ReadAsStringAsync(ct);
		if (!resp.IsSuccessStatusCode)
		{
			logAction($"[AccountEquity][ALERT][TG][ERROR] status={resp.StatusCode}, body={value2}", Enums.LogLevel.llBaselogic);
		}
		else
		{
			logAction("[AccountEquity][ALERT] Telegram slow-tick alert sent", Enums.LogLevel.llBaselogic);
		}
	}

	private static string Short(string? s)
	{
		if (!string.IsNullOrWhiteSpace(s))
		{
			if (s.Length > 16)
			{
				return s.Substring(0, 16) + "...";
			}
			return s;
		}
		return "null";
	}
}
