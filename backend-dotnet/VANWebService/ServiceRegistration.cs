using System;
using CoreDTO.Configs;
using CoreDTO.Logger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VANWebService.Models;
using VANWebService.Services;

namespace VANWebService;

public static class ServiceRegistration
{
	public static IServiceCollection AddNdhServices(this IServiceCollection services, VANWebServiceConfig cfg, Enums.LogAction logAction)
	{
		services.AddSingleton((IServiceProvider _) => logAction);
		services.AddSingleton((IServiceProvider _) => cfg);
		services.AddSingleton((Func<IServiceProvider, IRecordsRepository>)((IServiceProvider sp) => new PostgresRecordsRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>())));
		services.AddSingleton((Func<IServiceProvider, IAccountsRepository>)((IServiceProvider sp) => new PostgresAccountsRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>())));
		services.AddSingleton((IServiceProvider sp) => new AccountDailyEquityRepository(cfg, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton(delegate(IServiceProvider sp)
		{
			IAccountsRepository requiredService = sp.GetRequiredService<IAccountsRepository>();
			IRecordsRepository requiredService2 = sp.GetRequiredService<IRecordsRepository>();
			Enums.LogAction requiredService3 = sp.GetRequiredService<Enums.LogAction>();
			return new Orchestrator(cfg.orchestratorConfig, requiredService, requiredService2, requiredService3);
		});
		services.AddSingleton((IServiceProvider sp) => new Service(sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton((IServiceProvider sp) => new AuthorityRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton((IServiceProvider sp) => new FundEquityRepository(cfg, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton((IServiceProvider sp) => new NotificationService(cfg, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton((IServiceProvider sp) => new OkxRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton((IServiceProvider sp) => new BybitRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton((IServiceProvider sp) => new CoincallRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton<CoincallBackendSpreadBotEventHub>();
		services.AddSingleton((IServiceProvider sp) => new CoincallSpreadBotRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>(), sp.GetRequiredService<CoincallBackendSpreadBotEventHub>()));
		services.AddSingleton((IServiceProvider sp) => new MexcRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton((IServiceProvider sp) => new BinanceRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton((IServiceProvider sp) => new ArbitrageBidAskRepository(cfg.AuthorityConnectionString, sp.GetRequiredService<Enums.LogAction>()));
		services.AddSingleton<ArbitrageRobotManager>();
		services.Configure(delegate(HostOptions o)
		{
			o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
		});
		services.AddSingleton<IAccountDailyEquityBackfillQueue, AccountDailyEquityBackfillQueue>();
		services.AddSingleton<CoincallCandlesHostedService>();
		services.AddSingleton<CoincallFuturesMarkPriceHostedService>();
		services.AddSingleton<CoincallFuturesOrderEventsHostedService>();
		services.AddSingleton<CoincallBackendSpreadBotManager>();
		services.AddSingleton<ArbitrageBidAskCollectorHostedService>();
		if (HostedServicesDisabled())
		{
			logAction("Hosted background services disabled by VAN_DISABLE_HOSTED_SERVICES", Enums.LogLevel.llBaselogic);
		}
		else
		{
			services.AddHostedService<FundEquityUpdaterHostedService>();
			services.AddHostedService((IServiceProvider sp) => new AccountEquityUpdaterHostedService(sp.GetRequiredService<VANWebServiceConfig>(), sp.GetRequiredService<Enums.LogAction>()));
			services.AddHostedService<AccountDailyEquityUpdaterHostedService>();
			services.AddHostedService<OkxSnapshotHostedService>();
			services.AddHostedService<OkxFundingHostedService>();
			services.AddHostedService<CoincallSnapshotHostedService>();
			services.AddHostedService((IServiceProvider sp) => sp.GetRequiredService<CoincallCandlesHostedService>());
			services.AddHostedService((IServiceProvider sp) => sp.GetRequiredService<CoincallFuturesMarkPriceHostedService>());
			services.AddHostedService((IServiceProvider sp) => sp.GetRequiredService<CoincallFuturesOrderEventsHostedService>());
			services.AddHostedService<CoincallBackendSpreadBotHostedService>();
			services.AddHostedService<CoincallFundingHostedService>();
			services.AddHostedService<BybitSnapshotHostedService>();
			services.AddHostedService<BybitQuoteCollectorHostedService>();
			services.AddHostedService<BinanceFundingHostedService>();
			services.AddHostedService<BinanceSnapshotHostedService>();
			services.AddHostedService((IServiceProvider sp) => sp.GetRequiredService<ArbitrageBidAskCollectorHostedService>());
		}
		return services;
	}

	private static bool HostedServicesDisabled()
	{
		return string.Equals(Environment.GetEnvironmentVariable("VAN_DISABLE_HOSTED_SERVICES"), "true", StringComparison.OrdinalIgnoreCase);
	}
}
