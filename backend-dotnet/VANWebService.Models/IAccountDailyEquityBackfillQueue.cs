using System.Collections.Generic;
using System.Threading;

namespace VANWebService.Models;

public interface IAccountDailyEquityBackfillQueue
{
	void Enqueue(int accId);

	IAsyncEnumerable<int> DequeueAllAsync(CancellationToken ct);

	void MarkDone(int accId);
}
