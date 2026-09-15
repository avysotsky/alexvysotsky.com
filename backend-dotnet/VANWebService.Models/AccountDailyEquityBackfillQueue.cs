using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace VANWebService.Models;

public sealed class AccountDailyEquityBackfillQueue : IAccountDailyEquityBackfillQueue
{
	private readonly Channel<int> _ch = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
	{
		SingleReader = true,
		SingleWriter = false
	});

	private readonly ConcurrentDictionary<int, byte> _inflight = new ConcurrentDictionary<int, byte>();

	public void Enqueue(int accId)
	{
		if (accId > 0 && _inflight.TryAdd(accId, 0))
		{
			_ch.Writer.TryWrite(accId);
		}
	}

	public async IAsyncEnumerable<int> DequeueAllAsync([EnumeratorCancellation] CancellationToken ct)
	{
		while (await _ch.Reader.WaitToReadAsync(ct))
		{
			int item;
			while (_ch.Reader.TryRead(out item))
			{
				yield return item;
			}
		}
	}

	public void MarkDone(int accId)
	{
		_inflight.TryRemove(accId, out var _);
	}
}
