using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.Application.Interfaces;

public interface IMobiusKlinesClient
{
    /// <summary>
    /// Lấy danh sách nến từ MobiusQuant API (vd: https://api.mobiusquant.ai/api/klines?exchange=commodity&market=spot&symbol=XAUUSD&interval=5m&limit=10)
    /// </summary>
    Task<List<Candle>> FetchKlinesAsync(
        string symbol = "XAUUSD",
        string interval = "5m",
        int limit = 60,
        string exchange = "commodity",
        string market = "spot",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách nến và chuyển đổi thành RawCandleDto (đã tính sẵn EMA/RSI/ATR nếu thiếu).
    /// </summary>
    Task<List<RawCandleDto>> FetchRawCandlesAsync(
        string symbol = "XAUUSD",
        string interval = "5m",
        int limit = 60,
        string exchange = "commodity",
        string market = "spot",
        CancellationToken cancellationToken = default);
}
