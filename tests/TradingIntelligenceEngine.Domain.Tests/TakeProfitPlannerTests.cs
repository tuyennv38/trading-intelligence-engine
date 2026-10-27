using System.Collections.Generic;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.MarketData;
using Xunit;

namespace TradingIntelligenceEngine.Domain.Tests;

public class TakeProfitPlannerTests
{
    [Fact]
    public void PullEarlier_XauusdBuy_PullsEarlyByAtLeastOnePrice()
    {
        var plan = new TradingPlanResponse
        {
            Symbol = "XAUUSD",
            Scenarios = new List<EaTradeScenario>
            {
                new()
                {
                    Id = "SC1",
                    Type = "REVERSAL_BUY",
                    EntryZone = new EntryZone { Min = 2730.00m, Max = 2732.00m }, // Entry mid = 2731.00
                    StopLoss = 2725.00m,
                    TakeProfits = new List<decimal> { 2740.00m, 2750.00m } // TP1 distance = 9.0
                }
            }
        };

        var contract = new SymbolContractSpec
        {
            Digits = 2,
            Point = 0.01m,
            TickSize = 0.01m,
            TickValue = 1.0m,
            Spread = 0.25m
        };

        TakeProfitPlanner.PullEarlier(plan, contract);

        // Kháng cự ban đầu ở 2740.00, phải được dịch sớm 1 giá về 2739.00
        Assert.Equal(2739.00m, plan.Scenarios[0].TakeProfits[0]);
        // TP2 từ 2750.00 dịch sớm 1 giá về 2749.00
        Assert.Equal(2749.00m, plan.Scenarios[0].TakeProfits[1]);
    }

    [Fact]
    public void PullEarlier_XauusdSell_PullsEarlyByAtLeastOnePrice()
    {
        var plan = new TradingPlanResponse
        {
            Symbol = "XAUUSD",
            Scenarios = new List<EaTradeScenario>
            {
                new()
                {
                    Id = "SC2",
                    Type = "REVERSAL_SELL",
                    EntryZone = new EntryZone { Min = 2740.00m, Max = 2742.00m }, // Entry mid = 2741.00
                    StopLoss = 2746.00m,
                    TakeProfits = new List<decimal> { 2730.00m } // TP1 distance = 11.0
                }
            }
        };

        var contract = new SymbolContractSpec
        {
            Digits = 2,
            Point = 0.01m,
            TickSize = 0.01m,
            TickValue = 1.0m,
            Spread = 0.20m
        };

        TakeProfitPlanner.PullEarlier(plan, contract);

        // Hỗ trợ ban đầu ở 2730.00, SELL dịch lên 1 giá về 2731.00
        Assert.Equal(2731.00m, plan.Scenarios[0].TakeProfits[0]);
    }

    [Fact]
    public void PullEarlier_ForexPair_PullsEarlyByTenPips()
    {
        var plan = new TradingPlanResponse
        {
            Symbol = "EURUSD",
            Scenarios = new List<EaTradeScenario>
            {
                new()
                {
                    Id = "SC3",
                    Type = "BREAKOUT_BUY",
                    TriggerPrice = 1.10000m,
                    StopLoss = 1.09600m,
                    TakeProfits = new List<decimal> { 1.11000m } // 100 pips distance
                }
            }
        };

        var contract = new SymbolContractSpec
        {
            Digits = 5,
            Point = 0.00001m,
            TickSize = 0.00001m,
            TickValue = 1.0m,
            Spread = 0.00015m
        };

        TakeProfitPlanner.PullEarlier(plan, contract);

        // 10 pips = 0.00100. TP1 dịch sớm từ 1.11000 về 1.10900
        Assert.Equal(1.10900m, plan.Scenarios[0].TakeProfits[0]);
    }

    [Fact]
    public void EnsureDefaults_TruncatesToMaxTwoTakeProfits()
    {
        var plan = new TradingPlanResponse
        {
            Symbol = "XAUUSD",
            Scenarios = new List<EaTradeScenario>
            {
                new()
                {
                    Id = "SC4",
                    Type = "REVERSAL_BUY",
                    TakeProfits = new List<decimal> { 2730.00m, 2740.00m, 2750.00m, 2760.00m }
                }
            }
        };

        plan.EnsureDefaults("XAUUSD", "SIG_1", 100);

        Assert.Equal(2, plan.Scenarios[0].TakeProfits.Count);
        Assert.Equal(2730.00m, plan.Scenarios[0].TakeProfits[0]);
        Assert.Equal(2740.00m, plan.Scenarios[0].TakeProfits[1]);
    }
}
