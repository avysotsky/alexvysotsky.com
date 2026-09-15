using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CoreDTO.Logger;
using Microsoft.Extensions.Hosting;
using VANWebService.Services;

namespace VANWebService.Models;

public sealed class CoincallFuturesOrderEventsHostedService : BackgroundService
{
    private readonly Enums.LogAction _log;
    private readonly CoincallRepository _repo;
    private readonly object _subscribersLock = new();
    private readonly List<Channel<string>> _subscribers = new();
    private readonly object _ordersLock = new();
    private readonly Dictionary<string, string> _activeOrders = new(StringComparer.Ordinal);

    public CoincallFuturesOrderEventsHostedService(Enums.LogAction log, CoincallRepository repo)
    {
        _log = log;
        _repo = repo;
    }

    public ChannelReader<string> Subscribe(CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        lock (_subscribersLock) _subscribers.Add(channel);
        var snapshot = BuildSnapshotPayload();
        if (!string.IsNullOrWhiteSpace(snapshot)) channel.Writer.TryWrite(snapshot);
        ct.Register(() =>
        {
            lock (_subscribersLock) _subscribers.Remove(channel);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var account = await _repo.GetActiveAccountAsync(stoppingToken);
                if (account == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }
                await ConnectAndListenAsync(account, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log("[CoinCall futures order WS] " + ex.Message, Enums.LogLevel.llBaselogic);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConnectAndListenAsync(CoincallAccountRecord account, CancellationToken ct)
    {
        var client = new CoincallApiClient(account);
        var auth = client.BuildAccountFuturesWebSocketAuth();

        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "https://www.coincall.com");
        socket.Options.SetRequestHeader("User-Agent", "Mozilla/5.0");
        socket.Options.SetRequestHeader("Referer", "https://www.coincall.com/");
        await socket.ConnectAsync(new Uri(auth.Url), ct);
        _log("[CoinCall futures order WS] connected", Enums.LogLevel.llBaselogic);

        await SendJsonAsync(socket, new { action = "subscribe", dataType = "order" }, ct);

        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var payload = await ReceiveTextAsync(socket, ct);
            if (payload == null) break;

            if (TryReadPing(payload, out var ping))
            {
                await SendRawAsync(socket, "{\"pong\":" + ping + "}", ct);
                continue;
            }

            if (IsOrderEvent(payload))
            {
                ApplyOrderEventToCache(payload);
                Broadcast(payload);
            }
        }

        _log("[CoinCall futures order WS] disconnected", Enums.LogLevel.llBaselogic);
    }


    private string BuildSnapshotPayload()
    {
        string[] orders;
        lock (_ordersLock) orders = _activeOrders.Values.ToArray();
        return orders.Length == 0 ? string.Empty : "{\"dt\":35,\"d\":[" + string.Join(",", orders) + "]}";
    }

    private void ApplyOrderEventToCache(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("d", out var data)) return;
            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray()) ApplyOrderToCache(item);
                return;
            }
            if (data.ValueKind == JsonValueKind.Object) ApplyOrderToCache(data);
        }
        catch { }
    }

    private void ApplyOrderToCache(JsonElement order)
    {
        if (order.ValueKind != JsonValueKind.Object) return;
        var key = ReadOrderField(order, "oid", "orderId", "ordId", "order_id", "id");
        var clientKey = ReadOrderField(order, "coid", "clientOrderId", "clientOid", "clOrdId");
        var cacheKey = !string.IsNullOrWhiteSpace(key) ? "o:" + key : (!string.IsNullOrWhiteSpace(clientKey) ? "c:" + clientKey : string.Empty);
        if (string.IsNullOrWhiteSpace(cacheKey)) return;

        lock (_ordersLock)
        {
            if (IsTerminalOrder(order)) _activeOrders.Remove(cacheKey);
            else _activeOrders[cacheKey] = order.GetRawText();
        }
    }

    private static bool IsTerminalOrder(JsonElement order)
    {
        var statusText = ReadOrderField(order, "os", "status", "state", "orderStatus").Trim();
        if (int.TryParse(statusText, out var status) && (status == 1 || status == 3 || status == 6)) return true;
        return statusText.Equals("FILLED", StringComparison.OrdinalIgnoreCase)
            || statusText.Equals("CANCELED", StringComparison.OrdinalIgnoreCase)
            || statusText.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)
            || statusText.Equals("INVALID", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadOrderField(JsonElement order, params string[] names)
    {
        foreach (var name in names)
        {
            if (!order.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? string.Empty;
            if (value.ValueKind == JsonValueKind.Number || value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) return value.GetRawText();
        }
        return string.Empty;
    }

    private void Broadcast(string payload)
    {
        Channel<string>[] subscribers;
        lock (_subscribersLock) subscribers = _subscribers.ToArray();
        foreach (var channel in subscribers)
        {
            if (!channel.Writer.TryWrite(payload))
            {
                lock (_subscribersLock) _subscribers.Remove(channel);
                channel.Writer.TryComplete();
            }
        }
    }

    private static bool IsOrderEvent(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("dt", out var dt)) return false;
            if (dt.ValueKind == JsonValueKind.Number && dt.TryGetInt32(out var n)) return n == 35;
            if (dt.ValueKind == JsonValueKind.String && int.TryParse(dt.GetString(), out n)) return n == 35;
        }
        catch { }
        return false;
    }

    private static bool TryReadPing(string payload, out string ping)
    {
        ping = "";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("ping", out var el)) return false;
            ping = el.ValueKind == JsonValueKind.String ? JsonSerializer.Serialize(el.GetString()) : el.GetRawText();
            return !string.IsNullOrWhiteSpace(ping);
        }
        catch { return false; }
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new System.IO.MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.Count > 0) ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static Task SendJsonAsync(ClientWebSocket socket, object payload, CancellationToken ct) =>
        SendRawAsync(socket, JsonSerializer.Serialize(payload), ct);

    private static Task SendRawAsync(ClientWebSocket socket, string payload, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
}
