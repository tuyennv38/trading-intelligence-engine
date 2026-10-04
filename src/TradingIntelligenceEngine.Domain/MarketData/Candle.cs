namespace TradingIntelligenceEngine.Domain.MarketData;

public sealed record Candle
{
    public DateTimeOffset Time { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }

    public Candle(DateTimeOffset time, decimal open, decimal high, decimal low, decimal close, decimal volume)
    {
        if (high < open || high < close || high < low)
        {
            throw new ArgumentException("High must be >= Open, Close, and Low.");
        }

        if (low > open || low > close)
        {
            throw new ArgumentException("Low must be <= Open and Close.");
        }

        if (volume < 0)
        {
            throw new ArgumentException("Volume cannot be negative.");
        }

        Time = time;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }
}