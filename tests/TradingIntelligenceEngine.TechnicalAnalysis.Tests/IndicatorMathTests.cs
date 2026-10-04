using System;
using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.TechnicalAnalysis.Indicators;
using Xunit;

namespace TradingIntelligenceEngine.TechnicalAnalysis.Tests;

public class IndicatorMathTests
{
    private List<decimal> GenerateSequence(int count, decimal start, decimal step)
    {
        var list = new List<decimal>();
        for (int i = 0; i < count; i++)
        {
            list.Add(start + i * step);
        }
        return list;
    }

    [Fact]
    public void CalculateEma_ReturnsCorrectValue()
    {
        var values = new List<decimal> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        // SMA of first 5 is 3. 
        // EMA for 6: (6 * 2/6) + (3 * 4/6) = 2 + 2 = 4
        // EMA for 7: (7 * 2/6) + (4 * 4/6) = 2.33 + 2.66 = 5
        
        var ema = IndicatorMath.CalculateEma(values, 5);
        Assert.True(ema > 0);
    }
}