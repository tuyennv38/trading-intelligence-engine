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
using TradingIntelligenceEngine.Domain.MarketStructure;
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


    private sealed class StubSwingDetector : ISwingDetector
    {
        private readonly List<SwingPoint> _swings;
        public StubSwingDetector(params SwingPoint[] swings) => _swings = swings.ToList();
        public IReadOnlyList<SwingPoint> Detect(IReadOnlyList<Candle> candles, SwingOptions options) => _swings;
    }

    private static SwingPoint Sw(int index, SwingType type, decimal price, SwingStatus status = SwingStatus.Confirmed) =>
        new(index, DateTimeOffset.UtcNow, type, price, 3, status);

    private static BreakoutPatternService CreateService(ISwingDetector detector, BreakoutOptions? options = null) =>
        new(new MockKlinesClient(new List<RawCandleDto>()),
            new MockMarketDataStore(),
            options ?? new BreakoutOptions(),
            detector,
            new SwingOptions { FractalStrength = 3, MinAtrMultiplier = 0.5m },
            NullLogger<BreakoutPatternService>.Instance);

    // Nến phẳng quanh `price` với biên độ 2 (ATR ~ 2); nến cuối đóng cửa tại `lastClose`
    private static List<Candle> FlatCandles(int count, decimal price, decimal? lastClose = null)
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var list = new List<Candle>();
        for (int i = 0; i < count; i++)
        {
            var close = i == count - 1 && lastClose.HasValue ? lastClose.Value : price;
            var high = Math.Max(price + 1m, close);
            var low = Math.Min(price - 1m, close);
            list.Add(new Candle(start.AddMinutes(i * 5), price, high, low, close, 100));
        }
        return list;
    }

    private static Candle C(int i, decimal open, decimal close, decimal wick = 0.3m) =>
        new(DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(i * 5), open, Math.Max(open, close) + wick, Math.Min(open, close) - wick, close, 100);

    private static StubSwingDetector RangeSwings() => new(
        Sw(5, SwingType.High, 2010m), Sw(10, SwingType.Low, 2000m),
        Sw(15, SwingType.High, 2010.3m), Sw(20, SwingType.Low, 2000.2m),
        Sw(25, SwingType.High, 2009.8m), Sw(30, SwingType.Low, 1999.9m));

    [Fact]
    public async Task Recognize_IgnoresInvalidatedAndPotentialSwings()
    {
        var detector = new StubSwingDetector(
            Sw(5, SwingType.High, 2010m),
            Sw(6, SwingType.High, 2010.1m, SwingStatus.Invalidated),
            Sw(7, SwingType.High, 2010.2m, SwingStatus.Invalidated),
            Sw(8, SwingType.High, 2010.1m, SwingStatus.Potential),
            Sw(12, SwingType.Low, 2000m));
        var service = CreateService(detector);

        var result = await service.RecognizePatternAsync("XAUUSD", "M5", FlatCandles(40, 2005m));

        Assert.Equal(1, result.Zones.Resistance.TouchCount);
        Assert.Equal(1, result.Zones.Support.TouchCount);
        Assert.NotEqual("RECTANGLE", result.PrimaryPattern.Name);
    }

    [Fact]
    public async Task Recognize_CleanRange_IsConfirmedRectangle_AndPlansBothSides()
    {
        var service = CreateService(RangeSwings());

        var pattern = await service.RecognizePatternAsync("XAUUSD", "M5", FlatCandles(40, 2005m));
        Assert.Equal("RECTANGLE", pattern.PrimaryPattern.Name);
        Assert.Equal("CONFIRMED", pattern.PrimaryPattern.Status);
        Assert.Equal(3, pattern.Zones.Resistance.TouchCount);
        Assert.Equal(3, pattern.Zones.Support.TouchCount);

        var plan = await service.PlanBreakoutScenarioAsync(pattern, FlatCandles(40, 2005m));
        var buy = plan.TradingPlan.Scenarios.Single(s => s.IsBuy);
        var sell = plan.TradingPlan.Scenarios.Single(s => s.IsSell);

        Assert.Equal("SCENARIO_CONFIRMED", plan.CurrentStatus);
        Assert.True(buy.StopLoss < buy.TriggerPrice);
        Assert.True(buy.TakeProfits[0] > buy.TriggerPrice);
        Assert.True(buy.TakeProfits[1] > buy.TakeProfits[0]);
        Assert.True(sell.StopLoss > sell.TriggerPrice);
        Assert.True(sell.TakeProfits[0] < sell.TriggerPrice);
        Assert.True(sell.TakeProfits[1] < sell.TakeProfits[0]);
        // SL Vàng nằm trong khoảng 5.0 - 9.5 giá
        Assert.InRange(buy.TriggerPrice!.Value - buy.StopLoss, 5.0m, 9.5m);
    }

    [Fact]
    public async Task Recognize_PriceAlreadyAboveAllResistance_HasNoBreakoutBuy()
    {
        var service = CreateService(RangeSwings());
        var candles = FlatCandles(40, 2005m, lastClose: 2025m);

        var pattern = await service.RecognizePatternAsync("XAUUSD", "M5", candles);
        var plan = await service.PlanBreakoutScenarioAsync(pattern, candles);

        Assert.Null(pattern.Zones.Resistance.High);
        Assert.DoesNotContain(plan.TradingPlan.Scenarios, s => s.IsBuy);
    }

    [Fact]
    public async Task Plan_PriceTooFarBeyondTrigger_IsNoChase()
    {
        var service = CreateService(new StubSwingDetector());
        var pattern = new PatternRecognitionResult
        {
            Symbol = "XAUUSD",
            Timeframe = "M5",
            CurrentPrice = 2030m,
            Atr = 2m,
            MarketState = "RANGE",
            PrimaryPattern = new PatternDetail { Group = "A", Name = "RECTANGLE", Status = "CONFIRMED", Confidence = "HIGH" },
            Zones = new PatternZones
            {
                Resistance = new BoundaryZone { Low = 2009.8m, High = 2010.3m, TouchCount = 3 },
                Support = new BoundaryZone { Low = 1999.9m, High = 2000.2m, TouchCount = 3 }
            }
        };

        var plan = await service.PlanBreakoutScenarioAsync(pattern, new List<Candle>());

        Assert.DoesNotContain(plan.TradingPlan.Scenarios, s => s.IsBuy);
        Assert.Contains("NO_CHASE", plan.TradingPlan.Context);
    }

    [Fact]
    public async Task Plan_PotentialPattern_RespectsRequireConfirmedPattern()
    {
        var detector = new StubSwingDetector(Sw(5, SwingType.High, 2010m), Sw(15, SwingType.High, 2010.2m));

        var strict = CreateService(detector, new BreakoutOptions { RequireConfirmedPattern = true });
        var pattern = await strict.RecognizePatternAsync("XAUUSD", "M5", FlatCandles(40, 2005m));
        Assert.Equal("DOUBLE_TOP", pattern.PrimaryPattern.Name);
        Assert.Equal("POTENTIAL", pattern.PrimaryPattern.Status);

        var strictPlan = await strict.PlanBreakoutScenarioAsync(pattern, FlatCandles(40, 2005m));
        Assert.Empty(strictPlan.TradingPlan.Scenarios);
        Assert.Equal("MONITOR", strictPlan.CurrentStatus);

        // Mặc định: biên đã có >= 2 lần chạm thì vẫn lập kịch bản
        var relaxed = CreateService(detector);
        var relaxedPlan = await relaxed.PlanBreakoutScenarioAsync(pattern, FlatCandles(40, 2005m));
        Assert.Contains(relaxedPlan.TradingPlan.Scenarios, s => s.IsBuy);
        Assert.DoesNotContain(relaxedPlan.TradingPlan.Scenarios, s => s.IsSell);
    }

    [Fact]
    public async Task Plan_ResistanceThreeTouches_SupportTwoTouches_PriceAtSupport_HasBothScenarios()
    {
        // Kháng cự 3 lần chạm, hỗ trợ 2 lần chạm, giá vừa thủng xuống sát/dưới hỗ trợ
        var detector = new StubSwingDetector(
            Sw(5, SwingType.High, 4131.8m), Sw(10, SwingType.Low, 4116.3m),
            Sw(15, SwingType.High, 4125.6m), Sw(20, SwingType.Low, 4112.4m),
            Sw(25, SwingType.High, 4128.9m), Sw(30, SwingType.Low, 4108.5m));
        var service = CreateService(detector);
        var candles = new List<Candle>();
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        for (int i = 0; i < 40; i++)
        {
            // biên độ 7.8 -> ATR ~7.8
            var close = i == 39 ? 4110.41m : 4120m;
            candles.Add(new Candle(start.AddMinutes(i * 5), 4120m, Math.Max(4124m, close), Math.Min(4116.2m, close), close, 100));
        }

        var pattern = await service.RecognizePatternAsync("XAUUSD", "M5", candles);
        Assert.Equal(3, pattern.Zones.Resistance.TouchCount);
        Assert.True(pattern.Zones.Support.TouchCount >= 2);

        var plan = await service.PlanBreakoutScenarioAsync(pattern, candles);
        Assert.Contains(plan.TradingPlan.Scenarios, s => s.IsBuy);
        Assert.Contains(plan.TradingPlan.Scenarios, s => s.IsSell);
    }

    [Fact]
    public async Task Recognize_OngoingImpulse_IsNotBullFlag()
    {
        var candles = new List<Candle>();
        decimal price = 2000m;
        for (int i = 0; i < 40; i++)
        {
            candles.Add(C(i, price, price + 1.5m));
            price += 1.5m;
        }
        var service = CreateService(new StubSwingDetector());

        var result = await service.RecognizePatternAsync("XAUUSD", "M5", candles);

        Assert.NotEqual("BULL_FLAG", result.PrimaryPattern.Name);
    }

    [Fact]
    public async Task Recognize_ImpulseThenTightConsolidation_IsBullFlag()
    {
        var candles = new List<Candle>();
        int i = 0;
        for (; i < 30; i++) candles.Add(C(i, 2000m, 2000.3m)); // nền
        decimal price = 2000m;
        for (int k = 0; k < 10; k++, i++) { candles.Add(C(i, price, price + 2m)); price += 2m; } // pole -> 2020
        // vùng điều chỉnh hẹp, chạm biên trên/dưới nhiều lần
        decimal[] closes = { 2018m, 2019.8m, 2018m, 2017.8m, 2019.8m, 2018m, 2017.8m, 2019.5m };
        decimal prev = price;
        foreach (var close in closes) { candles.Add(C(i++, prev, close, 0.2m)); prev = close; }
        var service = CreateService(new StubSwingDetector());

        var result = await service.RecognizePatternAsync("XAUUSD", "M5", candles);

        Assert.Equal("BULL_FLAG", result.PrimaryPattern.Name);
        Assert.Equal("TREND_UP", result.MarketState);
        Assert.True(result.Zones.Resistance.High > result.Zones.Support.Low);
    }

    [Fact]
    public async Task Recognize_FlatHighsWithRisingLows_IsAscendingTriangle_WithSlopedSupport()
    {
        var detector = new StubSwingDetector(
            Sw(5, SwingType.High, 2010m), Sw(10, SwingType.Low, 1996m),
            Sw(15, SwingType.High, 2010.2m), Sw(20, SwingType.Low, 2000m),
            Sw(25, SwingType.High, 2009.9m), Sw(30, SwingType.Low, 2004m));
        var service = CreateService(detector);

        var result = await service.RecognizePatternAsync("XAUUSD", "M5", FlatCandles(40, 2006m));

        Assert.Equal("ASCENDING_TRIANGLE", result.PrimaryPattern.Name);
        Assert.Equal("CONFIRMED", result.PrimaryPattern.Status);
        // Hỗ trợ là đường dốc chiếu tại nến hiện tại, không phải một cụm đáy cũ
        Assert.InRange(result.Zones.Support.Center, 2004m, 2010m);
    }

    [Fact]
    public async Task Analyze_RealSwingDetector_ZigZagRange_ProducesBothScenarios()
    {
        decimal[] sequence = { 2005, 2008, 2010, 2008, 2005, 2002, 2000, 2002, 2005, 2008, 2010, 2008, 2005, 2002, 2000, 2002, 2005,
                               2008, 2010, 2008, 2005, 2002, 2000, 2002, 2005, 2008, 2010, 2008, 2005, 2002, 2000, 2002, 2005 };
        long baseTime = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
        var raw = sequence.Select((p, i) => new RawCandleDto
        {
            Time = baseTime + i * 300,
            Open = p - 0.5m,
            High = p + 0.4m,
            Low = p - 0.9m,
            Close = p,
            TickVolume = 500
        }).ToList();

        var store = new MockMarketDataStore();
        var service = new BreakoutPatternService(
            new MockKlinesClient(raw), store, new BreakoutOptions(),
            new SwingDetector(), new SwingOptions { FractalStrength = 2, MinAtrMultiplier = 0.1m },
            NullLogger<BreakoutPatternService>.Instance);

        var result = await service.AnalyzeAndExecuteBreakoutSyncAsync("XAUUSD", "5m", 40, notify: false);

        Assert.Equal("RECTANGLE", result.PatternRecognition.PrimaryPattern.Name);
        Assert.Contains(result.TradingPlan.Scenarios, s => s.IsBuy && s.IsBreakout && s.TriggerPrice > 0);
        Assert.Contains(result.TradingPlan.Scenarios, s => s.IsSell && s.IsBreakout && s.TriggerPrice > 0);
        Assert.NotNull(store.SavedPlan);
        Assert.Equal(result.TradingPlan.SignalId, store.SavedPlan!.SignalId);
    }
}
