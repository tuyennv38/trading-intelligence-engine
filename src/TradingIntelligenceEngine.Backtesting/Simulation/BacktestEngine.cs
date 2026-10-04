using System;
using System.Collections.Generic;
using System.Linq;
using TradingIntelligenceEngine.Domain.Backtesting;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketState;
using TradingIntelligenceEngine.Domain.Signal;

namespace TradingIntelligenceEngine.Backtesting.Simulation;

public class BacktestEngine : IBacktestEngine
{
    private readonly IMarketAnalyzer _analyzer;
    private readonly IStrategyEngine _strategyEngine;

    public BacktestEngine(IMarketAnalyzer analyzer, IStrategyEngine strategyEngine)
    {
        _analyzer = analyzer;
        _strategyEngine = strategyEngine;
    }

    public BacktestReport Run(BacktestRequest request)
    {
        if (request.HistoricalCandles == null || request.HistoricalCandles.Count <= request.WindowSize)
        {
            throw new ArgumentException("Not enough historical data for the given window size.");
        }

        var trades = new List<BacktestTrade>();
        
        // Active trade simulation state
        BacktestTrade? activeTrade = null;
        decimal? currentSL = null;
        decimal? currentTP = null;

        for (int i = request.WindowSize; i < request.HistoricalCandles.Count; i++)
        {
            var window = request.HistoricalCandles.Skip(i - request.WindowSize).Take(request.WindowSize).ToList();
            var currentCandle = window.Last();

            // 1. Manage Active Trade
            if (activeTrade != null)
            {
                bool closed = false;
                decimal exitPrice = 0m;
                decimal profit = 0m;

                if (activeTrade.Direction == SignalDirection.Long)
                {
                    if (currentSL.HasValue && currentCandle.Low <= currentSL.Value)
                    {
                        exitPrice = currentSL.Value;
                        profit = exitPrice - activeTrade.EntryPrice;
                        closed = true;
                    }
                    else if (currentTP.HasValue && currentCandle.High >= currentTP.Value)
                    {
                        exitPrice = currentTP.Value;
                        profit = exitPrice - activeTrade.EntryPrice;
                        closed = true;
                    }
                }
                else if (activeTrade.Direction == SignalDirection.Short)
                {
                    if (currentSL.HasValue && currentCandle.High >= currentSL.Value)
                    {
                        exitPrice = currentSL.Value;
                        profit = activeTrade.EntryPrice - exitPrice;
                        closed = true;
                    }
                    else if (currentTP.HasValue && currentCandle.Low <= currentTP.Value)
                    {
                        exitPrice = currentTP.Value;
                        profit = activeTrade.EntryPrice - exitPrice;
                        closed = true;
                    }
                }

                if (closed)
                {
                    var finishedTrade = activeTrade with
                    {
                        ExitPrice = exitPrice,
                        Profit = profit,
                        ExitTime = currentCandle.Time,
                        Status = "Closed"
                    };
                    trades.Add(finishedTrade);
                    
                    activeTrade = null;
                    currentSL = null;
                    currentTP = null;
                }
            }

            // 2. Scan for New Signals (only if no active trade)
            if (activeTrade == null)
            {
                var analysisRequest = new MarketAnalysisRequest(request.Symbol, request.Timeframe, window);
                var state = _analyzer.Analyze(analysisRequest);
                
                var signals = _strategyEngine.EvaluateAll(state);
                var bestSignal = signals.OrderByDescending(s => s.Score).FirstOrDefault();

                if (bestSignal != null && bestSignal.SuggestedStopLoss.HasValue && bestSignal.SuggestedTakeProfit.HasValue)
                {
                    // Open Virtual Trade
                    activeTrade = new BacktestTrade(
                        Direction: bestSignal.Direction,
                        Setup: bestSignal.Setup,
                        EntryPrice: currentCandle.Close, // Simulate entry at close
                        ExitPrice: null,
                        Profit: null,
                        EntryTime: currentCandle.Time,
                        ExitTime: null,
                        Status: "Open"
                    );

                    currentSL = bestSignal.SuggestedStopLoss;
                    currentTP = bestSignal.SuggestedTakeProfit;
                }
            }
        }

        // Close any remaining active trade at the end of the simulation
        if (activeTrade != null)
        {
            var lastCandle = request.HistoricalCandles.Last();
            var profit = activeTrade.Direction == SignalDirection.Long
                ? lastCandle.Close - activeTrade.EntryPrice
                : activeTrade.EntryPrice - lastCandle.Close;

            trades.Add(activeTrade with
            {
                ExitPrice = lastCandle.Close,
                Profit = profit,
                ExitTime = lastCandle.Time,
                Status = "Closed_EOD"
            });
        }

        return GenerateReport(trades);
    }

    private BacktestReport GenerateReport(IReadOnlyList<BacktestTrade> trades)
    {
        int winning = trades.Count(t => t.Profit > 0);
        int losing = trades.Count(t => t.Profit <= 0);
        decimal winRate = trades.Count > 0 ? (decimal)winning / trades.Count * 100m : 0m;
        decimal totalProfit = trades.Sum(t => t.Profit ?? 0m);

        return new BacktestReport(
            TotalTrades: trades.Count,
            WinningTrades: winning,
            LosingTrades: losing,
            WinRate: Math.Round(winRate, 2),
            TotalProfit: Math.Round(totalProfit, 4),
            Trades: trades
        );
    }
}