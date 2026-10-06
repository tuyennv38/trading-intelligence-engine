namespace TradingIntelligenceEngine.Application.DTOs.MarketData;

public class RawCandleDto
{
    /// <summary>
    /// Thời gian mở nến (Unix timestamp theo giây hoặc mili-giây)
    /// </summary>
    public long Time { get; set; }
    
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    
    /// <summary>
    /// Khối lượng giao dịch (Tick Volume cho Forex/Vàng)
    /// </summary>
    public long TickVolume { get; set; }
}