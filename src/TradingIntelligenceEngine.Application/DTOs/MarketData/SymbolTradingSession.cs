using System.Collections.Generic;
using System.Linq;

namespace TradingIntelligenceEngine.Application.DTOs.MarketData;

/// <summary>
/// Session theo symbol sau initialize — giữ mode cấu trúc và danh sách timeframe.
/// </summary>
public class SymbolTradingSession
{
    public const int LostHistoryCapacity = 100;

    public string Symbol { get; set; } = string.Empty;
    public bool UseAiStructure { get; set; }
    public List<string> Timeframes { get; set; } = new();

    /// <summary>
    /// true khi đang có job AI (init/refresh) chưa xong — tránh xếp hàng trùng mỗi 5 phút.
    /// </summary>
    public bool PlanRefreshInProgress { get; set; }

    /// <summary>
    /// Số nến mỗi khung lúc initialize. Khung không có trong init dùng MaxInitCandleCount.
    /// </summary>
    public Dictionary<string, int> CandleCapacityByTimeframe { get; set; } = new();

    /// <summary>
    /// Độ lớn nhất của mảng nến lúc init. 0 = chưa init (mất lịch sử → cap 100).
    /// </summary>
    public int MaxInitCandleCount { get; set; }

    public int ResolveCandleCapacity(string timeframe)
    {
        if (MaxInitCandleCount > 0)
            return MaxInitCandleCount;

        if (CandleCapacityByTimeframe != null
            && CandleCapacityByTimeframe.TryGetValue(timeframe, out var cap)
            && cap > 0)
            return cap;

        return LostHistoryCapacity;
    }

    public static int MaxCount(IEnumerable<int> counts)
    {
        var list = counts?.ToList() ?? new List<int>();
        return list.Count == 0 ? 0 : list.Max();
    }
}
