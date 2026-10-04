using System;
using TradingIntelligenceEngine.Domain.MarketData;
using Xunit;

namespace TradingIntelligenceEngine.Domain.Tests;

public class CandleTests
{
    [Fact]
    public void Candle_ValidData_CreatesSuccessfully()
    {
        var time = DateTimeOffset.UtcNow;
        var candle = new Candle(time, 1.1000m, 1.1050m, 1.0950m, 1.1020m, 1000);

        Assert.Equal(time, candle.Time);
        Assert.Equal(1.1000m, candle.Open);
        Assert.Equal(1.1050m, candle.High);
        Assert.Equal(1.0950m, candle.Low);
        Assert.Equal(1.1020m, candle.Close);
        Assert.Equal(1000m, candle.Volume);
    }

    [Fact]
    public void Candle_HighLessThanOpen_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => new Candle(DateTimeOffset.UtcNow, 1.1000m, 1.0900m, 1.0950m, 1.1020m, 1000));
    }

    [Fact]
    public void Candle_LowGreaterThanClose_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => new Candle(DateTimeOffset.UtcNow, 1.1000m, 1.1050m, 1.1030m, 1.1020m, 1000));
    }

    [Fact]
    public void Candle_NegativeVolume_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => new Candle(DateTimeOffset.UtcNow, 1.1000m, 1.1050m, 1.0950m, 1.1020m, -1));
    }
}