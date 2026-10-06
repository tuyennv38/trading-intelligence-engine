using System.Collections.Generic;
using System.Threading.Tasks;
using TradingIntelligenceEngine.Application.DTOs.MarketData;

namespace TradingIntelligenceEngine.Application.Interfaces;

public interface IMarketDataStore
{
    /// <summary>
    /// Lưu đè mảng nến khởi tạo vào bộ nhớ (Cache/Redis)
    /// </summary>
    Task InitializeCandlesAsync(string symbol, string timeframe, List<RawCandleDto> candles);

    /// <summary>
    /// Nạp thêm 1 nến mới vào cuối mảng, và đẩy nến cũ nhất ra ngoài để duy trì Capacity
    /// </summary>
    Task AppendCandleAsync(string symbol, string timeframe, RawCandleDto newCandle, int maxCapacity = 1000);

    /// <summary>
    /// Lấy toàn bộ nến hiện tại đang lưu trong bộ nhớ
    /// </summary>
    Task<List<RawCandleDto>> GetCandlesAsync(string symbol, string timeframe);

    /// <summary>
    /// Lưu trạng thái cấu trúc thị trường mới nhất
    /// </summary>
    Task SetLatestStateAsync(string symbol, Dictionary<string, Domain.MarketState.MarketState> state);

    /// <summary>
    /// Lấy trạng thái cấu trúc thị trường cũ để so sánh
    /// </summary>
    Task<Dictionary<string, Domain.MarketState.MarketState>?> GetLatestStateAsync(string symbol);

    /// <summary>
    /// Lưu kế hoạch giao dịch mới nhất do AI tạo ra
    /// </summary>
    Task SetLatestPlanAsync(string symbol, Domain.AI.TradingPlanResponse plan);

    /// <summary>
    /// Lấy kế hoạch giao dịch cũ để kiểm tra (Cắn SL, Breakout, v.v.)
    /// </summary>
    Task<Domain.AI.TradingPlanResponse?> GetLatestPlanAsync(string symbol);
}