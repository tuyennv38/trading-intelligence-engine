using System;
using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.MarketContext;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.Liquidity;

public class SupportResistanceAnalyzer : ISupportResistanceAnalyzer
{
    public IReadOnlyList<PriceZone> Analyze(IReadOnlyList<SwingPoint> swings, decimal atrTolerance)
    {
        var zones = new List<PriceZone>();
        if (swings.Count == 0) return zones;

        var sortedSwings = swings.OrderBy(s => s.Price).ToList();
        
        var currentCluster = new List<SwingPoint> { sortedSwings[0] };

        for (int i = 1; i < sortedSwings.Count; i++)
        {
            if (sortedSwings[i].Price - currentCluster.Last().Price <= atrTolerance)
            {
                currentCluster.Add(sortedSwings[i]);
            }
            else
            {
                if (currentCluster.Count > 1)
                {
                    zones.Add(new PriceZone(
                        High: currentCluster.Max(s => s.Price),
                        Low: currentCluster.Min(s => s.Price)
                    ));
                }
                currentCluster = new List<SwingPoint> { sortedSwings[i] };
            }
        }

        if (currentCluster.Count > 1)
        {
            zones.Add(new PriceZone(
                High: currentCluster.Max(s => s.Price),
                Low: currentCluster.Min(s => s.Price)
            ));
        }

        return zones;
    }
}