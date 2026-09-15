using Microsoft.AspNetCore.Builder;

namespace VANWebService;

public static class EndpointMapping
{
	public static void MapNdhEndpoints(this WebApplication app)
	{
		app.MapCoreEndpoints();
		app.MapPublicEndpoints();
		app.MapUserEndpoints();
		app.MapAdminEndpoints();
		app.MapOkxEndpoints();
		app.MapBybitEndpoints();
		app.MapCoincallEndpoints();
		app.MapMexcEndpoints();
		app.MapBinanceEndpoints();
		app.MapArbitrageEndpoints();
		app.MapDeribitPrivateStreamEndpoints();
	}
}
