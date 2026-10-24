using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.Application.DTOs.MarketData;

public class MarketDataUpdateRequest
{
    /// <summary>
    /// Tên cặp giao dịch (Ví dụ: "XAUUSD")
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// true = EA chủ động xin kịch bản mới (chạy lại structure theo mode lúc init + Strategist).
    /// false (mặc định) = chỉ cập nhật nến / trạng thái scenario, KHÔNG tạo plan mới.
    /// </summary>
    public bool RefreshPlan { get; set; } = false;

    /// <summary>
    /// Trần lỗ chấp nhận mỗi lệnh (USD). &gt; 0 thì cập nhật session (phục hồi sau mất init).
    /// </summary>
    public decimal MaxRiskUsd { get; set; }

    /// <summary>Cập nhật hợp đồng cặp (phục hồi session / đổi symbol).</summary>
    public SymbolContractSpec? Contract { get; set; }
    
    /// <summary>
    /// Dữ liệu nến mới nhất gửi 2 phút/lần.
    /// Key: Tên khung (Ví dụ: "M15"), Value: 1 cây nến duy nhất vừa đóng hoặc đang chạy.
    /// </summary>
    public Dictionary<string, RawCandleDto> Timeframes { get; set; } = new();
}