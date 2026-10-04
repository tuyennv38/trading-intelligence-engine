namespace TradingIntelligenceEngine.Domain.MarketStructure;

public enum StructureEventType
{
    BOS,
    CHOCH
}

public enum EventDirection
{
    Bullish,
    Bearish
}

public sealed record StructureEvent(
    StructureEventType Type,
    EventDirection Direction,
    decimal Price,
    int CandleIndex,
    DateTimeOffset ConfirmedAt,
    string Status // Potential, Confirmed
);