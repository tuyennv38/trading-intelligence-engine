namespace TradingIntelligenceEngine.Domain.Configuration;

public sealed class SwingOptions
{
    public int FractalStrength { get; init; } = 3;
    public decimal MinAtrMultiplier { get; init; } = 0.5m;
}