using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.Domain.MarketState;

public sealed record MarketAnalysisRequest(
    string Symbol,
    Timeframe Timeframe,
    IReadOnlyList<Candle> Candles
);

public interface IMarketAnalyzer
{
    MarketState Analyze(MarketAnalysisRequest request);
}