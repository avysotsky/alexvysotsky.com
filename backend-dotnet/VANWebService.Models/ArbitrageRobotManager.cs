using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VANWebService.Models;

public sealed class ArbitrageRobotManager
{
    private readonly ConcurrentDictionary<string, ArbitrageRobotInstance> _instances = new(StringComparer.OrdinalIgnoreCase);

    public ArbitrageRobotSnapshot Ensure(ArbitrageRobotRequest request)
    {
        string key = BuildKey(request);
        var instance = _instances.GetOrAdd(key, _ => new ArbitrageRobotInstance(key, request));
        instance.Touch(request);
        return instance.Snapshot();
    }

    public IReadOnlyList<ArbitrageRobotSnapshot> List()
    {
        return _instances.Values
            .OrderBy(x => x.PairKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Snapshot())
            .ToList();
    }

    public ArbitrageRobotSnapshot Start(ArbitrageRobotRequest request)
    {
        string key = BuildKey(request);
        var instance = _instances.GetOrAdd(key, _ => new ArbitrageRobotInstance(key, request));
        instance.Start(request);
        return instance.Snapshot();
    }

    public ArbitrageRobotSnapshot? Stop(ArbitrageRobotRequest request)
    {
        string key = BuildKey(request);
        return _instances.TryGetValue(key, out var instance) ? instance.Stop(request).Snapshot() : null;
    }

    private static string BuildKey(ArbitrageRobotRequest request)
    {
        string explicitKey = Clean(request.pairKey);
        if (!string.IsNullOrWhiteSpace(explicitKey)) return explicitKey;
        string leg1 = Clean(request.leg1ExchangeId) + ":" + Clean(request.leg1ApiSymbol ?? request.leg1Symbol);
        string leg2 = Clean(request.leg2ExchangeId) + ":" + Clean(request.leg2ApiSymbol ?? request.leg2Symbol);
        return leg1 + "|" + leg2;
    }

    private static string Clean(string? value)
    {
        return (value ?? "").Trim().ToUpperInvariant();
    }
}

public sealed class ArbitrageRobotInstance
{
    private readonly object _sync = new();
    private readonly List<ArbitrageRobotLogRow> _logs = new();
    private ArbitrageRobotRequest _request;

    public ArbitrageRobotInstance(string pairKey, ArbitrageRobotRequest request)
    {
        PairKey = pairKey;
        InstanceId = Guid.NewGuid().ToString("N");
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        Status = "created";
        _request = request;
        AddLog("created", "Robot instance created for pair " + pairKey);
    }

    public string PairKey { get; }
    public string InstanceId { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; private set; }
    public string Status { get; private set; }

    public void Touch(ArbitrageRobotRequest request)
    {
        lock (_sync)
        {
            _request = request;
            UpdatedAtUtc = DateTime.UtcNow;
            AddLog("ensure", "Robot instance ensured");
        }
    }

    public void Start(ArbitrageRobotRequest request)
    {
        lock (_sync)
        {
            _request = request;
            Status = "running";
            UpdatedAtUtc = DateTime.UtcNow;
            AddLog("start", "Robot instance marked running");
        }
    }

    public ArbitrageRobotInstance Stop(ArbitrageRobotRequest request)
    {
        lock (_sync)
        {
            _request = request;
            Status = "stopped";
            UpdatedAtUtc = DateTime.UtcNow;
            AddLog("stop", "Robot instance marked stopped");
            return this;
        }
    }

    public ArbitrageRobotSnapshot Snapshot()
    {
        lock (_sync)
        {
            return new ArbitrageRobotSnapshot(
                PairKey,
                InstanceId,
                Status,
                CreatedAtUtc,
                UpdatedAtUtc,
                _request.leg1ExchangeId,
                _request.leg1Symbol,
                _request.leg1ApiSymbol,
                _request.leg2ExchangeId,
                _request.leg2Symbol,
                _request.leg2ApiSymbol,
                _logs.TakeLast(50).ToArray()
            );
        }
    }

    private void AddLog(string action, string message)
    {
        _logs.Add(new ArbitrageRobotLogRow(DateTime.UtcNow, action, message));
        if (_logs.Count > 200) _logs.RemoveRange(0, _logs.Count - 200);
    }
}

public sealed record ArbitrageRobotRequest(
    string? pairKey,
    string? leg1ExchangeId,
    string? leg1Symbol,
    string? leg1ApiSymbol,
    string? leg2ExchangeId,
    string? leg2Symbol,
    string? leg2ApiSymbol
);

public sealed record ArbitrageRobotSnapshot(
    string pairKey,
    string instanceId,
    string status,
    DateTime createdAtUtc,
    DateTime updatedAtUtc,
    string? leg1ExchangeId,
    string? leg1Symbol,
    string? leg1ApiSymbol,
    string? leg2ExchangeId,
    string? leg2Symbol,
    string? leg2ApiSymbol,
    IReadOnlyList<ArbitrageRobotLogRow> logs
);

public sealed record ArbitrageRobotLogRow(DateTime tsUtc, string action, string message);
