namespace TradingIntelligenceEngine.Domain.Configuration;

public sealed class LiquidityOptions
{
    public decimal EqualLevelAtrTolerance { get; init; } = 0.2m;
}

public sealed class IndicatorOptions
{
    public int EmaFast { get; init; } = 34;
    public int EmaSlow { get; init; } = 89;
    public int RsiPeriod { get; init; } = 14;
    public int AtrPeriod { get; init; } = 14;
    public int AdxPeriod { get; init; } = 14;
    public decimal TrendingAdxThreshold { get; init; } = 25m;
}