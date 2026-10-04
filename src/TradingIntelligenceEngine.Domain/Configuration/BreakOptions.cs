namespace TradingIntelligenceEngine.Domain.Configuration;

public enum BreakConfirmationType
{
    Close,
    Wick
}

public sealed class BreakOptions
{
    public BreakConfirmationType Confirmation { get; init; } = BreakConfirmationType.Close;
}