using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;

namespace VANWebService.Models;

public sealed class CoincallBackendSpreadBotEventHub
{
    private readonly object _lock = new();
    private readonly List<Subscriber> _subscribers = new();

    public ChannelReader<string> Subscribe(Guid? instanceId, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        var subscriber = new Subscriber(instanceId, channel);
        lock (_lock) _subscribers.Add(subscriber);
        ct.Register(() =>
        {
            lock (_lock) _subscribers.Remove(subscriber);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }

    public void PublishSnapshot(Guid instanceId, object payload) => Publish(instanceId, "snapshot", payload);
    public void PublishInstance(Guid instanceId, object payload) => Publish(instanceId, "instance", payload);
    public void PublishLog(Guid instanceId, object payload) => Publish(instanceId, "log", payload);

    private void Publish(Guid instanceId, string type, object payload)
    {
        string json = JsonSerializer.Serialize(new
        {
            type,
            instanceId,
            payload,
            tsUtc = DateTime.UtcNow
        });
        Subscriber[] subscribers;
        lock (_lock) subscribers = _subscribers.ToArray();
        foreach (var subscriber in subscribers)
        {
            if (subscriber.InstanceId.HasValue && subscriber.InstanceId.Value != instanceId) continue;
            if (subscriber.Channel.Writer.TryWrite(json)) continue;
            lock (_lock) _subscribers.Remove(subscriber);
            subscriber.Channel.Writer.TryComplete();
        }
    }

    private sealed record Subscriber(Guid? InstanceId, Channel<string> Channel);
}
