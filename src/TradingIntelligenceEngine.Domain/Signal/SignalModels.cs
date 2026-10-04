namespace TradingIntelligenceEngine.Domain.Signal;

public enum SignalDirection
{
    Long,
    Short,
    Neutral
}

public sealed record TradingSignal(
    SignalDirection Direction,
    string Setup,
    int Score,
    decimal EntryPrice,
    decimal? SuggestedStopLoss,
    decimal? SuggestedTakeProfit,
    string Reasoning
);