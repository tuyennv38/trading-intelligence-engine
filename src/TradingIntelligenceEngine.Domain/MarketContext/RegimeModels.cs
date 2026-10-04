namespace TradingIntelligenceEngine.Domain.MarketContext;

public enum MarketRegimeType
{
    TrendingUp,
    TrendingDown,
    Ranging,
    Transition,
    HighVolatility,
    LowVolatility
}

public sealed record RegimeState(
    MarketRegimeType Type,
    decimal Confidence
);