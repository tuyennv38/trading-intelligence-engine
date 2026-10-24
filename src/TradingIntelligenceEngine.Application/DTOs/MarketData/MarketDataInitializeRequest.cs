using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.Application.DTOs.MarketData;

public class MarketDataInitializeRequest
{
    /// <summary>
    /// Tên cặp giao dịch (Ví dụ: "XAUUSD", "EURUSD")
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// true = AI nhận dạng cấu trúc thị trường từ nến (thay Quant), rồi mới gọi AI Strategist lập kịch bản.
    /// false = Quant phân tích cấu trúc như hiện tại (AI Strategist vẫn chạy ở bước sau).
    /// </summary>
    public bool UseAiStructure { get; set; } = false;

    /// <summary>
    /// Trần lỗ chấp nhận mỗi lệnh (USD). 0 hoặc bỏ trống = 100. AI dùng khi chọn kịch bản; EA tính lot từ SL.
    /// </summary>
    public decimal MaxRiskUsd { get; set; }

    /// <summary>
    /// Tick value / volume min của đúng cặp chart — lot phụ thuộc hợp đồng này.
    /// </summary>
    public SymbolContractSpec? Contract { get; set; }
    
    /// <summary>
    /// Dữ liệu nến cho từng khung thời gian.
    /// Key là tên khung (Ví dụ: "H4", "H1", "M15") và Value là mảng các nến tương ứng.
    /// </summary>
    public Dictionary<string, List<RawCandleDto>> Timeframes { get; set; } = new();
}
