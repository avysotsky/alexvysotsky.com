using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CoreDTO.Args;
using CoreDTO.Configs;
using CoreDTO.Logger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using VANWebService;
using VANWebService.Models;

[CompilerGenerated]
internal class Program
{
	static async Task Main(string[] args)
	{
		DateTime utcNow = DateTime.UtcNow;
		(Enums.LogAction, StreamWriter, Version, string) tuple = Boot.InitLogger("VANWebService", "logs", utcNow);
		var (logAction, _, _, _) = tuple;
		_ = tuple.Item2;
		Version item = tuple.Item3;
		_ = tuple.Item4;
		logAction("VANWebService v " + item?.ToString() + " initiation started", Enums.LogLevel.llInit);
		Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		Console.OutputEncoding = Encoding.GetEncoding("windows-1251");
		Dictionary<string, string> argsDict = Parser.ParseArgs(args, logAction);
		int port = Boot.RequirePort(argsDict, logAction);
		VANWebServiceConfig vANWebServiceConfig = Boot.LoadConfig("config.json", logAction);
		logAction("=== Update 2 ===", Enums.LogLevel.llBaselogic);
		logAction("Config:", Enums.LogLevel.llBaselogic);
		logAction($"   AccountEquityMaxParallel = {vANWebServiceConfig.AccountEquityMaxParallel}", Enums.LogLevel.llBaselogic);
		logAction("   TelegramBotToken         = " + vANWebServiceConfig.TelegramBotToken, Enums.LogLevel.llBaselogic);
		logAction("   TelegramChatId           = " + vANWebServiceConfig.TelegramChatId, Enums.LogLevel.llBaselogic);
		logAction("   ContactTelegramChatId    = " + vANWebServiceConfig.ContactTelegramChatId, Enums.LogLevel.llBaselogic);
		logAction("   NotificationEmailTo      = " + vANWebServiceConfig.NotificationEmailTo, Enums.LogLevel.llBaselogic);
		logAction("   ContactEmailTo           = " + vANWebServiceConfig.ContactEmailTo, Enums.LogLevel.llBaselogic);
		logAction("   SmtpHost                 = " + vANWebServiceConfig.SmtpHost, Enums.LogLevel.llBaselogic);
		logAction($"   SmtpPort                 = {vANWebServiceConfig.SmtpPort}", Enums.LogLevel.llBaselogic);
		logAction("   SmtpUser                 = " + vANWebServiceConfig.SmtpUser, Enums.LogLevel.llBaselogic);
		logAction($"   SmtpUseSsl               = {vANWebServiceConfig.SmtpUseSsl}", Enums.LogLevel.llBaselogic);
		WebApplicationBuilder webApplicationBuilder = WebApplication.CreateBuilder(args);
		webApplicationBuilder.Services.AddSignalR();
		webApplicationBuilder.Services.ConfigureHttpJsonOptions(delegate(JsonOptions o)
		{
			o.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
			o.SerializerOptions.MaxDepth = 128;
		});
		webApplicationBuilder.Services.AddCors(delegate(CorsOptions o)
		{
			o.AddDefaultPolicy(delegate(CorsPolicyBuilder p)
			{
				p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
			});
		});
		webApplicationBuilder.Services.AddNdhServices(vANWebServiceConfig, logAction);
		webApplicationBuilder.WebHost.UseKestrel(delegate(KestrelServerOptions serverOption)
		{
			serverOption.ListenAnyIP(port);
		});
		WebApplication app = webApplicationBuilder.Build();
		app.Services.GetRequiredService<Orchestrator>();
		app.UseCors();
		app.Lifetime.ApplicationStopping.Register(delegate
		{
			logAction("[Finalize] Stopping...", Enums.LogLevel.llBaselogic);
		});
		app.MapNdhEndpoints();
		if (string.Equals(Environment.GetEnvironmentVariable("VAN_DISABLE_STARTUP_EMAIL"), "true", StringComparison.OrdinalIgnoreCase))
		{
			logAction("Startup e-mail disabled by VAN_DISABLE_STARTUP_EMAIL", Enums.LogLevel.llBaselogic);
		}
		else
		{
			await Boot.SendStartupEmailAsync(vANWebServiceConfig, logAction);
		}
		Console.CancelKeyPress += delegate(object? sender, ConsoleCancelEventArgs e)
		{
			logAction("[Finalize] Process had been shut down correctly.", Enums.LogLevel.llBaselogic);
			e.Cancel = true;
		};
		app.Run();
	}
}
