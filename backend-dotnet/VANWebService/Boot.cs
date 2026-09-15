using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoreDTO.Configs;
using CoreDTO.Logger;
using CoreDTO.Tools;
using CoreDTO.VANDTO;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Npgsql;

namespace VANWebService;

public static class Boot
{
	public static (CoreDTO.Logger.Enums.LogAction logAction, StreamWriter excLogger, Version? version, string revision) InitLogger(string initSubsystem, string loggerFolderName, DateTime startDateUtc)
	{
		Version version = Assembly.GetExecutingAssembly().GetName().Version;
		string text = ((version != null) ? version.Revision.ToString() : "NA");
		string directoryName = Path.GetDirectoryName(AppContext.BaseDirectory);
		string text2 = directoryName + "/" + loggerFolderName;
		InitTools.CreateDirectory(text2);
		string text3 = $"{startDateUtc:yyyyMMdd-HHmmss.fff}.{initSubsystem}.Rev_{text}.txt";
		StreamWriter streamWriter = new StreamWriter(text2 + "/" + text3);
		CoreDTO.Logger.Enums.LogAction debugFunction = Tools.GetDebugFunction(initSubsystem, LocalTime.GetLocalTimeFunc(0), streamWriter, CoreDTO.Logger.Enums.LogLevel.llFull);
		return (logAction: debugFunction, excLogger: streamWriter, version: version, revision: text);
	}

	public static int RequirePort(Dictionary<string, string> argsDict, CoreDTO.Logger.Enums.LogAction logAction)
	{
		int num = -1;
		if (argsDict.ContainsKey("port"))
		{
			num = int.Parse(argsDict["port"]);
		}
		if (num < 0)
		{
			logAction("[FAIL] port < 0! Stop program.", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			Environment.Exit(-1);
		}
		return num;
	}

	public static VANWebServiceConfig LoadConfig(string configPath, CoreDTO.Logger.Enums.LogAction logAction)
	{
		VANWebServiceConfig vANWebServiceConfig = new VANWebServiceConfig();
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configPath);
		if (File.Exists(path))
		{
			vANWebServiceConfig = JsonSerializer.Deserialize<VANWebServiceConfig>(File.ReadAllText(path));
			if (vANWebServiceConfig == null)
			{
				logAction("[VANWebService][ERROR] cfg = null. configPath = " + configPath + ". Stop of program.", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
				Environment.Exit(-1);
			}
			vANWebServiceConfig.DeribitBaseUrl = (vANWebServiceConfig.DeribitBaseUrl ?? "").Trim();
			if (string.IsNullOrWhiteSpace(vANWebServiceConfig.DeribitBaseUrl))
			{
				vANWebServiceConfig.DeribitBaseUrl = "https://www.deribit.com";
			}
			vANWebServiceConfig.DeribitBaseUrl = vANWebServiceConfig.DeribitBaseUrl.TrimEnd('/');
		}
		if (string.IsNullOrWhiteSpace(vANWebServiceConfig.AuthorityConnectionString))
		{
			logAction("[AUTH] AuthorityConnectionString is empty in config.json", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
		}
		return vANWebServiceConfig;
	}

	public static async Task EnsureAccountEquityPartitionAsync(int accId, string connectionString, CoreDTO.Logger.Enums.LogAction log, CancellationToken ct)
	{
		if (accId <= 0)
		{
			throw new ArgumentOutOfRangeException("accId", "accId must be > 0");
		}
		string text = $"van_account_equity_a{accId}";
		string value = text + "_acc_ts_desc";
		string sql = $"\ndo $$\ndeclare\n  part_name text := '{text}';\n  idx_name  text := '{value}';\nbegin\n  if not exists (\n    select 1\n    from pg_class c\n    join pg_namespace n on n.oid = c.relnamespace\n    where n.nspname = 'public'\n      and c.relname = part_name\n  ) then\n    execute format(\n      'create table public.%I partition of public.van_account_equity for values in ({accId});',\n      part_name\n    );\n  end if;\n\n  if not exists (\n    select 1\n    from pg_indexes\n    where schemaname = 'public'\n      and indexname = idx_name\n  ) then\n    execute format(\n      'create index %I on public.%I using btree (acc_id, timestamp_utc desc);',\n      idx_name, part_name\n    );\n  end if;\nend $$;\n";
		await using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
		await conn.OpenAsync(ct);
		await using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
		await cmd.ExecuteNonQueryAsync(ct);
	}

	public static async Task EnsureAccountDailyEquityPartitionAsync(int accId, string connectionString, CoreDTO.Logger.Enums.LogAction log, CancellationToken ct)
	{
		if (accId <= 0)
		{
			throw new ArgumentOutOfRangeException("accId", "accId must be > 0");
		}
		string text = $"van_account_daily_equity_a{accId}";
		string value = text + "_acc_date_desc";
		string sql = $"\ndo $$\ndeclare\n  part_name text := '{text}';\n  idx_name  text := '{value}';\nbegin\n  -- защита от гонки\n  perform pg_advisory_xact_lock(hashtext(part_name)::bigint);\n\n  if not exists (\n    select 1\n    from pg_class c\n    join pg_namespace n on n.oid = c.relnamespace\n    where n.nspname = 'public'\n      and c.relname = part_name\n  ) then\n    execute format(\n      'create table public.%I partition of public.van_account_daily_equity for values in ({accId});',\n      part_name\n    );\n  end if;\n\n  if not exists (\n    select 1\n    from pg_indexes\n    where schemaname = 'public'\n      and indexname = idx_name\n  ) then\n    execute format(\n      'create index %I on public.%I using btree (acc_id, date_utc desc);',\n      idx_name, part_name\n    );\n  end if;\nend $$;\n";
		await using NpgsqlConnection conn = new NpgsqlConnection(connectionString);
		await conn.OpenAsync(ct);
		await using NpgsqlCommand cmd = new NpgsqlCommand(sql, conn);
		await cmd.ExecuteNonQueryAsync(ct);
	}

	public static async Task SendStartupEmailAsync(VANWebServiceConfig cfg, CoreDTO.Logger.Enums.LogAction log)
	{
		if (string.IsNullOrWhiteSpace(cfg.SmtpHost) || string.IsNullOrWhiteSpace(cfg.SmtpUser) || string.IsNullOrWhiteSpace(cfg.SmtpPassword) || string.IsNullOrWhiteSpace(cfg.NotificationEmailTo))
		{
			log("Startup e-mail skipped: SMTP config incomplete", CoreDTO.Logger.Enums.LogLevel.llBaselogic);
			return;
		}
		try
		{
			MimeMessage message = new MimeMessage
			{
				From = { (InternetAddress)new MailboxAddress("VANWebService", cfg.SmtpUser) },
				To = { (InternetAddress)MailboxAddress.Parse(cfg.NotificationEmailTo) },
				Subject = "VANWebService started on " + Environment.MachineName,
				Body = new TextPart("plain")
				{
					Text = $"VANWebService started at {DateTime.UtcNow:O} (UTC){Environment.NewLine}Machine: {Environment.MachineName}{Environment.NewLine}"
				}
			};
			using SmtpClient client = new SmtpClient();
			client.Timeout = 15000;
			SecureSocketOptions options = (cfg.SmtpUseSsl ? ((cfg.SmtpPort == 465) ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls) : SecureSocketOptions.None);
			using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(15.0));
			await client.ConnectAsync(cfg.SmtpHost, cfg.SmtpPort, options, cts.Token);
			await client.AuthenticateAsync(cfg.SmtpUser, cfg.SmtpPassword, cts.Token);
			await client.SendAsync(message, cts.Token);
			await client.DisconnectAsync(quit: true, cts.Token);
			log("Startup e-mail sent to " + cfg.NotificationEmailTo, CoreDTO.Logger.Enums.LogLevel.llBaselogic);
		}
		catch (Exception value)
		{
			log($"Startup e-mail error: {value}", CoreDTO.Logger.Enums.LogLevel.llExceptions);
		}
	}

	public static EquityCurveDTO BuildSimpleCurve(double startValueUsd, int days = 60)
	{
		List<EquityCurvePointDTO> list = new List<EquityCurvePointDTO>();
		DateTime date = DateTime.UtcNow.Date;
		if (startValueUsd <= 0.0)
		{
			startValueUsd = 100000.0;
		}
		for (int num = days - 1; num >= 0; num--)
		{
			DateTime dt = date.AddDays(-num);
			double num2 = 1.0 + ((double)num - (double)days / 2.0) * 0.001;
			list.Add(new EquityCurvePointDTO
			{
				dt = dt,
				equityUsd = startValueUsd * num2
			});
		}
		return new EquityCurveDTO
		{
			points = list
		};
	}
}
