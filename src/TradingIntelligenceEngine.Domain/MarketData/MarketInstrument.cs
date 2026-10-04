namespace TradingIntelligenceEngine.Domain.MarketData;

public sealed record MarketInstrument(
    string Symbol,
    string AssetClass
);