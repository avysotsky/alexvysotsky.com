using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace VANWebService.Models;

public class MutationsHub : Hub
{
	private static int _clients;

	public override Task OnConnectedAsync()
	{
		Interlocked.Increment(ref _clients);
		Console.WriteLine($"[Hub] Connected. Clients={_clients}");
		return base.OnConnectedAsync();
	}

	public override Task OnDisconnectedAsync(Exception? ex)
	{
		Interlocked.Decrement(ref _clients);
		Console.WriteLine($"[Hub] Disconnected. Clients={_clients}");
		return base.OnDisconnectedAsync(ex);
	}
}
