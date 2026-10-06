using System.Collections.Generic;

namespace TradingIntelligenceEngine.Application.DTOs.MarketData;

public class MarketDataUpdateRequest
{
    /// <summary>
    /// Tên cặp giao dịch (Ví dụ: "XAUUSD")
    /// </summary>
    public string Symbol { get; set; } = string.Empty;
    
    /// <summary>
    /// Dữ liệu nến mới nhất gửi 2 phút/lần.
    /// Key: Tên khung (Ví dụ: "M15"), Value: 1 cây nến duy nhất vừa đóng hoặc đang chạy.
    /// </summary>
    public Dictionary<string, RawCandleDto> Timeframes { get; set; } = new();
}