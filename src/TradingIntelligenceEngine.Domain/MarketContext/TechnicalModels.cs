namespace TradingIntelligenceEngine.Domain.MarketContext;

public sealed record TechnicalState(
    decimal EmaFast,
    decimal EmaSlow,
    decimal Rsi,
    decimal Atr,
    decimal Adx,
    decimal AtrPercentile // optional for V1 but mentioned in spec
);