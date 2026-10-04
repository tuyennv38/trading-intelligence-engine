using System;
using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;
using TradingIntelligenceEngine.MarketStructure.Swings;
using Xunit;

namespace TradingIntelligenceEngine.MarketStructure.Tests;

public class SwingDetectorTests
{
    private List<Candle> CreateCandles(params decimal[] prices)
    {
        var list = new List<Candle>();
        for (int i = 0; i < prices.Length; i++)
        {
            var p = prices[i];
            list.Add(new Candle(DateTimeOffset.UtcNow.AddMinutes(i), p, p, p, p, 100));
        }
        return list;
    }

    [Fact]
    public void Detect_FindsSwingHigh_AndSwingLow()
    {
        var detector = new SwingDetector();
        var candles = CreateCandles(
            10, 11, 12, 11, 10,  // Swing High at index 2 (12)
            9, 8, 9, 10, 11      // Swing Low at index 6 (8)
        );

        var options = new SwingOptions { FractalStrength = 2, MinAtrMultiplier = 0.1m };
        var swings = detector.Detect(candles, options);

        Assert.Equal(3, swings.Count);
        
        Assert.Equal(SwingType.High, swings[0].Type);
        Assert.Equal(2, swings[0].Index);
        Assert.Equal(12m, swings[0].Price);
        Assert.Equal(SwingStatus.Confirmed, swings[0].Status);

        Assert.Equal(SwingType.Low, swings[1].Type);
        Assert.Equal(6, swings[1].Index);
        Assert.Equal(8m, swings[1].Price);
        Assert.Equal(SwingStatus.Confirmed, swings[1].Status);

        Assert.Equal(SwingType.High, swings[2].Type);
        Assert.Equal(9, swings[2].Index);
        Assert.Equal(11m, swings[2].Price);
        Assert.Equal(SwingStatus.Potential, swings[2].Status);
    }
}