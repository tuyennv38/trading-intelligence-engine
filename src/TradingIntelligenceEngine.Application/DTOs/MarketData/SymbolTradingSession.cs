using System.Collections.Generic;

namespace TradingIntelligenceEngine.Application.DTOs.MarketData;

/// <summary>
/// Session theo symbol sau initialize — giữ mode cấu trúc và danh sách timeframe.
/// </summary>
public class SymbolTradingSession
{
    public string Symbol { get; set; } = string.Empty;
    public bool UseAiStructure { get; set; }
    public List<string> Timeframes { get; set; } = new();
}
