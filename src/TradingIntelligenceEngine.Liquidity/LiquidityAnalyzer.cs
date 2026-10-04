using System;
using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketContext;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.Liquidity;

public class LiquidityAnalyzer : ILiquidityAnalyzer
{
    public LiquidityState Analyze(IReadOnlyList<Candle> candles, IReadOnlyList<SwingPoint> swings, LiquidityOptions options)
    {
        var levels = new List<LiquidityLevel>();
        var sweeps = new List<LiquiditySweep>();

        if (swings.Count == 0 || candles.Count == 0)
        {
            return new LiquidityState(levels, sweeps);
        }

        // We use ATR of the latest candle to define tolerance
        // For simplicity, we can estimate ATR as the average candle size or take it from technical analysis.
        // Wait, the ILiquidityAnalyzer doesn't receive ATR directly.
        // Let's compute a simple recent average TR.
        decimal atr = CalculateSimpleAtr(candles);
        decimal tolerance = atr * options.EqualLevelAtrTolerance;

        // Group Highs and Lows
        var highs = swings.Where(s => s.Type == SwingType.High).ToList();
        var lows = swings.Where(s => s.Type == SwingType.Low).ToList();

        levels.AddRange(FindEqualLevels(highs, LiquidityType.BuySide, tolerance));
        levels.AddRange(FindEqualLevels(lows, LiquidityType.SellSide, tolerance));

        // Detect Sweeps
        // A sweep occurs when price goes beyond the level but closes back inside.
        // We only check the most recent candles for sweeps of existing levels.
        
        foreach (var level in levels)
        {
            // Only check candles after the last swing that formed this level
            int startIndex = level.SourceCandleIndices.Max() + 1;
            for (int i = startIndex; i < candles.Count; i++)
            {
                var c = candles[i];
                if (level.Type == LiquidityType.BuySide)
                {
                    if (c.High > level.Price && c.Close < level.Price)
                    {
                        sweeps.Add(new LiquiditySweep(LiquidityType.BuySide, c.High, level.Price, i, c.Time));
                    }
                }
                else
                {
                    if (c.Low < level.Price && c.Close > level.Price)
                    {
                        sweeps.Add(new LiquiditySweep(LiquidityType.SellSide, c.Low, level.Price, i, c.Time));
                    }
                }
            }
        }

        return new LiquidityState(levels, sweeps);
    }

    private IEnumerable<LiquidityLevel> FindEqualLevels(List<SwingPoint> points, LiquidityType type, decimal tolerance)
    {
        var levels = new List<LiquidityLevel>();
        var usedIndices = new HashSet<int>();

        for (int i = 0; i < points.Count; i++)
        {
            if (usedIndices.Contains(points[i].Index)) continue;

            var cluster = new List<SwingPoint> { points[i] };
            
            for (int j = i + 1; j < points.Count; j++)
            {
                if (Math.Abs(points[i].Price - points[j].Price) <= tolerance)
                {
                    cluster.Add(points[j]);
                    usedIndices.Add(points[j].Index);
                }
            }

            if (cluster.Count >= 2)
            {
                // We have equal highs/lows
                decimal avgPrice = cluster.Average(s => s.Price);
                levels.Add(new LiquidityLevel(type, avgPrice, cluster.Select(s => s.Index).ToList()));
            }
        }

        return levels;
    }

    private decimal CalculateSimpleAtr(IReadOnlyList<Candle> candles)
    {
        int period = Math.Min(14, candles.Count);
        if (period == 0) return 0;

        decimal sum = 0;
        for (int i = candles.Count - period; i < candles.Count; i++)
        {
            sum += candles[i].High - candles[i].Low;
        }
        return sum / period;
    }
}