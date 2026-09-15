using CoreDTO.Logger;

namespace VANWebService.Models;

public class Service
{
	private const string InitSubsystem = "WebService";

	private const string spreadsDataFilePath = "SpreadsData.json";

	private readonly Enums.LogAction logAction;

	public Service(Enums.LogAction logAction)
	{
		this.logAction = logAction;
	}
}
