using System;
using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.Signal;

namespace TradingIntelligenceEngine.Domain.Backtesting;

public sealed record BacktestTrade(
    SignalDirection Direction,
    string Setup,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal? Profit,
    DateTimeOffset EntryTime,
    DateTimeOffset? ExitTime,
    string Status // Open, Closed
);

public sealed record BacktestReport(
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    decimal WinRate,
    decimal TotalProfit,
    IReadOnlyList<BacktestTrade> Trades
);

public sealed class BacktestRequest
{
    public string Symbol { get; init; } = string.Empty;
    public Timeframe Timeframe { get; init; }
    public IReadOnlyList<Candle> HistoricalCandles { get; init; } = new List<Candle>();
    public int WindowSize { get; init; } = 100; // Lookback candles for analysis at each step
}

public interface IBacktestEngine
{
    BacktestReport Run(BacktestRequest request);
}