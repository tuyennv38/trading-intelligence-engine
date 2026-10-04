using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketContext;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.Domain.MarketState;

public sealed record TrendState(
    EventDirection Direction,
    decimal Strength
);

public sealed record StructureState(
    StructureLabel LastLabel,
    IReadOnlyList<string> Sequence
);

public sealed class MarketState
{
    public string Symbol { get; init; } = string.Empty;
    public Timeframe Timeframe { get; init; }
    
    // We combine parts from Regime, Tech, and Structure to formulate TrendState and StructureState
    public TrendState? Trend { get; init; }
    public StructureState? Structure { get; init; }
    
    public IReadOnlyList<StructureEvent> Events { get; init; } = new List<StructureEvent>();
    public LiquidityState? Liquidity { get; init; }
    public TechnicalState? Technical { get; init; }
    public RegimeState? Regime { get; init; }
}