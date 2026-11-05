using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TradingIntelligenceEngine.Api.Services;

/// <summary>Đầy đủ tham số một lần quét Breakout, do EA gửi qua API scan.</summary>
public sealed record BreakoutScanRequest(
    string Symbol,
    string Interval,
    int Limit,
    decimal MaxRiskUsd,
    string PatternMode,
    string ScenarioMode,
    bool Notify);

/// <summary>
/// Danh sách symbol mà EA đã yêu cầu quét (kèm tham số), để job định kỳ quét lại đúng tham số đó.
/// Symbol hết hạn khi EA không gọi scan / poll kế hoạch trong một khoảng thời gian (EA đã tắt).
/// </summary>
public sealed class BreakoutScanRegistry
{
    private sealed class Entry
    {
        public required BreakoutScanRequest Request { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _bySymbol = new();

    private static string Key(string symbol) => (symbol ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Ghi nhận / cập nhật tham số scan của EA cho symbol.</summary>
    public void Register(BreakoutScanRequest request)
    {
        var entry = new Entry { Request = request, LastSeenUtc = DateTimeOffset.UtcNow };
        _bySymbol.AddOrUpdate(Key(request.Symbol), entry, (_, _) => entry);
    }

    /// <summary>EA còn hoạt động (poll kế hoạch) -> gia hạn đăng ký.</summary>
    public void Touch(string symbol)
    {
        if (_bySymbol.TryGetValue(Key(symbol), out var entry))
            entry.LastSeenUtc = DateTimeOffset.UtcNow;
    }

    public BreakoutScanRequest? Get(string symbol) =>
        _bySymbol.TryGetValue(Key(symbol), out var entry) ? entry.Request : null;

    /// <summary>Các yêu cầu scan của EA còn hoạt động trong khoảng <paramref name="ttl"/>.</summary>
    public IReadOnlyList<BreakoutScanRequest> GetActive(TimeSpan ttl)
    {
        var cutoff = DateTimeOffset.UtcNow - ttl;
        return _bySymbol.Values
            .Where(e => e.LastSeenUtc >= cutoff)
            .Select(e => e.Request)
            .ToList();
    }
}
