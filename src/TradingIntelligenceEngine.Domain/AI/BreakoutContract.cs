using System;
using System.Collections.Generic;
using System.Linq;

namespace TradingIntelligenceEngine.Domain.AI;

/// <summary>Cách EA vào lệnh sau khi nến xác nhận phá vỡ.</summary>
public static class BreakoutEntryMode
{
    /// <summary>Chờ giá hồi chạm lại trigger trên khung retest rồi mới vào.</summary>
    public const string Retest = "RETEST";

    /// <summary>Vào ngay khi nến xác nhận đóng cửa phá trigger (nếu giá chưa chạy quá xa).</summary>
    public const string Momentum = "MOMENTUM";

    public static string Normalize(string? mode) =>
        string.Equals(mode?.Trim(), Momentum, StringComparison.OrdinalIgnoreCase) ? Momentum : Retest;
}

/// <summary>
/// Mã mô hình nến chuẩn dùng chung giữa Backend (Quant/AI) và EA BreakoutTrader.
/// EA chỉ hiểu các mã này; tên tự do từ AI được chuẩn hóa qua <see cref="Normalize(string?)"/>.
/// </summary>
public static class BreakoutCandlePatterns
{
    /// <summary>Thân nến >= 50% biên độ, đóng cửa ở 1/3 cuối theo hướng phá vỡ.</summary>
    public const string StrongBody = "STRONG_BODY";

    /// <summary>Thân nến >= 80% biên độ.</summary>
    public const string Marubozu = "MARUBOZU";

    /// <summary>Thân nến nhấn chìm thân nến trước ngược màu.</summary>
    public const string Engulfing = "ENGULFING";

    /// <summary>Bóng nến phía ngược hướng >= 2 lần thân (từ chối giá).</summary>
    public const string Pinbar = "PINBAR";

    public static readonly IReadOnlyList<string> All = new[] { StrongBody, Marubozu, Engulfing, Pinbar };

    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');

        if (n.Contains("MARUBOZU")) return Marubozu;
        if (n.Contains("ENGULF")) return Engulfing;
        if (n.Contains("PIN") || n.Contains("HAMMER") || n.Contains("SHOOTING") || n.Contains("REJECT")) return Pinbar;
        if (n.Contains("STRONG") || n.Contains("EXPANSION") || n.Contains("MOMENTUM") || n.Contains("BODY")) return StrongBody;
        return null;
    }

    public static List<string> NormalizeList(IEnumerable<string>? names) =>
        (names ?? Enumerable.Empty<string>())
            .Select(Normalize)
            .Where(code => code != null)
            .Select(code => code!)
            .Distinct()
            .ToList();
}
