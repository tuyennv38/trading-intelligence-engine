namespace TradingIntelligenceEngine.Domain.MarketStructure;

public enum SwingType
{
    High,
    Low
}

public enum SwingStatus
{
    Potential,
    Confirmed,
    Invalidated
}

public sealed record SwingPoint(
    int Index,
    DateTimeOffset Time,
    SwingType Type,
    decimal Price,
    int Strength,
    SwingStatus Status
);