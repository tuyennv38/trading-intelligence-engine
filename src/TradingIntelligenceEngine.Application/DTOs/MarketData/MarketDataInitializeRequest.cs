namespace TradingIntelligenceEngine.Application.DTOs.MarketData;

public class MarketDataInitializeRequest
{
    /// <summary>
    /// Tên cặp giao dịch (Ví dụ: "XAUUSD", "EURUSD")
    /// </summary>
    public string Symbol { get; set; } = string.Empty;
    
    /// <summary>
    /// Dữ liệu nến cho từng khung thời gian. 
    /// Key là tên khung (Ví dụ: "H4", "H1", "M15") và Value là mảng các nến tương ứng.
    /// </summary>
    public Dictionary<string, List<RawCandleDto>> Timeframes { get; set; } = new();
}