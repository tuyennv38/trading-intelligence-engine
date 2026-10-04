using System;
using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;
using TradingIntelligenceEngine.MarketStructure.Events;
using Xunit;

namespace TradingIntelligenceEngine.MarketStructure.Tests;

public class StructureEventDetectorTests
{
    private List<Candle> CreateCandles(params decimal[] closes)
    {
        var list = new List<Candle>();
        for (int i = 0; i < closes.Length; i++)
        {
            var p = closes[i];
            list.Add(new Candle(DateTimeOffset.UtcNow.AddMinutes(i), p, p, p, p, 100));
        }
        return list;
    }

    [Fact]
    public void Detect_BullishBOS_WhenCloseExceedsConfirmedHH()
    {
        // 0 to 10 candles
        var candles = CreateCandles(
            10, 11, 12, 11, 10,  // High at 2 (12)
            9, 10, 11, 12, 13    // Break of 12 at 9
        );

        // We manually create structure points
        var swings = new List<StructurePoint>
        {
            new StructurePoint(
                new SwingPoint(2, candles[2].Time, SwingType.High, 12, 2, SwingStatus.Confirmed),
                StructureLabel.HH
            ),
            new StructurePoint(
                new SwingPoint(5, candles[5].Time, SwingType.Low, 9, 2, SwingStatus.Confirmed),
                StructureLabel.HL
            )
        };

        var detector = new StructureEventDetector();
        var options = new TradingIntelligenceEngine.Domain.Configuration.BreakOptions();
        var events = detector.Detect(candles, swings, options);

        Assert.Single(events);
        Assert.Equal(StructureEventType.BOS, events[0].Type);
        Assert.Equal(EventDirection.Bullish, events[0].Direction);
        Assert.Equal(9, events[0].CandleIndex);
    }
}