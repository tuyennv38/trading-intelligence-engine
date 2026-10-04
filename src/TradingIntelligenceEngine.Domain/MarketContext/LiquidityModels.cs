using System;
using System.Collections.Generic;

namespace TradingIntelligenceEngine.Domain.MarketContext;

public enum LiquidityType
{
    BuySide,  // Equal Highs
    SellSide  // Equal Lows
}

public sealed record LiquidityLevel(
    LiquidityType Type,
    decimal Price,
    IReadOnlyList<int> SourceCandleIndices
);

public sealed record LiquiditySweep(
    LiquidityType Type,
    decimal SweepPrice,
    decimal LevelPrice,
    int CandleIndex,
    DateTimeOffset Time
);

public sealed record LiquidityState(
    IReadOnlyList<LiquidityLevel> ActiveLevels,
    IReadOnlyList<LiquiditySweep> RecentSweeps
);

public sealed record PriceZone(
    decimal High,
    decimal Low
);