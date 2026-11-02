using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TradingIntelligenceEngine.Api.Services;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.MarketStructure.Swings;
using Xunit;

namespace TradingIntelligenceEngine.Domain.Tests;

public class BreakoutPatternServiceTests
{
    private class MockMarketDataStore : IMarketDataStore
    {
        public TradingPlanResponse? SavedPlan { get; private set; }

        public Task InitializeCandlesAsync(string symbol, string timeframe, List<RawCandleDto> candles) => Task.CompletedTask;
        public Task AppendCandleAsync(string symbol, string timeframe, RawCandleDto newCandle, int maxCapacity = 100) => Task.CompletedTask;
        public Task<List<RawCandleDto>> GetCandlesAsync(string symbol, string timeframe) => Task.FromResult(new List<RawCandleDto>());
        public Task SetLatestStateAsync(string symbol, Dictionary<string, MarketState.MarketState> state) => Task.CompletedTask;
        public Task<Dictionary<string, MarketState.MarketState>?> GetLatestStateAsync(string symbol) => Task.FromResult<Dictionary<string, MarketState.MarketState>?>(null);
        public Task SetLatestPlanAsync(string symbol, TradingPlanResponse plan)
        {
            SavedPlan = plan;
            return Task.CompletedTask;
        }
        public Task<TradingPlanResponse?> GetLatestPlanAsync(string symbol) => Task.FromResult(SavedPlan);
        public Task SetSymbolSessionAsync(string symbol, SymbolTradingSession session) => Task.CompletedTask;
        public Task<SymbolTradingSession?> GetSymbolSessionAsync(string symbol) => Task.FromResult<SymbolTradingSession?>(null);
        public Task<List<SymbolTradingSession>> ListSymbolSessionsAsync() => Task.FromResult(new List<SymbolTradingSession>());
    }

    private class MockKlinesClient : IMobiusKlinesClient
    {
        private readonly List<RawCandleDto> _candles;

        public MockKlinesClient(List<RawCandleDto> candles)
        {
            _candles = candles;
        }

        public Task<List<Candle>> FetchKlinesAsync(string symbol = "XAUUSD", string interval = "5m", int limit = 60, string exchange = "commodity", string market = "spot", System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_candles.Select(c => new Candle(
                DateTimeOffset.FromUnixTimeSeconds(c.Time),
                c.Open, c.High, c.Low, c.Close, c.TickVolume,
                c.EmaFast, c.EmaSlow, c.Rsi, c.Adx, c.Atr
            )).ToList());
        }

        public Task<List<RawCandleDto>> FetchRawCandlesAsync(string symbol = "XAUUSD", string interval = "5m", int limit = 60, string exchange = "commodity", string market = "spot", System.Threading.CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_candles);
        }
    }

    [Fact]
    public async Task RecognizePattern_And_PlanBreakout_GeneratesValidScenarios()
    {
        // Tạo chuỗi nến dạng Range tích lũy giữa 2000 và 2010
        var rawCandles = new List<RawCandleDto>();
        decimal[] sequence = new decimal[]
        {
            2005, 2008, 2010, 2007, 2002, 2000, 2004, 2008, 2010, 2006, 2001, 2000, 2005, 2008, 2009, 2006, 2003, 2001, 2004, 2007
        };

        long baseTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < sequence.Length; i++)
        {
            var p = sequence[i];
            rawCandles.Add(new RawCandleDto
            {
                Time = baseTime + (i * 300),
                Open = p - 0.5m,
                High = p + 0.8m,
                Low = p - 0.8m,
                Close = p,
                TickVolume = 500,
                Atr = 2.0m
            });
        }

        var store = new MockMarketDataStore();
        var client = new MockKlinesClient(rawCandles);
        var options = new BreakoutOptions { Symbol = "XAUUSD", Interval = "5m", MaxRiskUsd = 100m };
        var swingDetector = new SwingDetector();
        var swingOptions = new SwingOptions { FractalStrength = 2, MinAtrMultiplier = 0.1m };

        var service = new BreakoutPatternService(
            client, store, options, swingDetector, swingOptions,
            NullLogger<BreakoutPatternService>.Instance);

        var result = await service.AnalyzeAndExecuteBreakoutSyncAsync(
            symbol: "XAUUSD",
            interval: "5m",
            limit: 20,
            notify: false);

        Assert.NotNull(result);
        Assert.NotNull(result.PatternRecognition);
        Assert.NotNull(result.TradingPlan);
        Assert.NotEmpty(result.TradingPlan.Scenarios);

        // Phải có kịch bản Breakout Mua hoặc Bán
        var buyScenario = result.TradingPlan.Scenarios.FirstOrDefault(s => s.IsBuy && s.IsBreakout);
        var sellScenario = result.TradingPlan.Scenarios.FirstOrDefault(s => s.IsSell && s.IsBreakout);

        Assert.NotNull(buyScenario);
        Assert.True(buyScenario.TriggerPrice > 0);
        Assert.True(buyScenario.StopLoss < buyScenario.TriggerPrice);
        Assert.NotEmpty(buyScenario.TakeProfits);

        Assert.NotNull(sellScenario);
        Assert.True(sellScenario.TriggerPrice > 0);
        Assert.True(sellScenario.StopLoss > sellScenario.TriggerPrice);
        Assert.NotEmpty(sellScenario.TakeProfits);

        // Store phải lưu được kế hoạch
        Assert.NotNull(store.SavedPlan);
        Assert.Equal(result.TradingPlan.SignalId, store.SavedPlan.SignalId);
    }
}
