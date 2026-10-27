using System;
using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.Domain.AI;

/// <summary>
/// Dịch các mức TP về phía entry tối thiểu 1 giá (với XAUUSD là 1.0 USD, hoặc 10 pips với Forex)
/// để tránh hụt TP do đặt sát mép kháng cự/hỗ trợ hoặc do spread sàn.
/// </summary>
public static class TakeProfitPlanner
{
    public static void PullEarlier(TradingPlanResponse plan, SymbolContractSpec? contract)
    {
        if (plan?.Scenarios == null || plan.Scenarios.Count == 0)
            return;

        var symbol = plan.Symbol ?? "";
        var isGold = symbol.Contains("XAU", StringComparison.OrdinalIgnoreCase) ||
                     symbol.Contains("GOLD", StringComparison.OrdinalIgnoreCase);

        foreach (var s in plan.Scenarios)
        {
            if (s.TakeProfits == null || s.TakeProfits.Count == 0)
                continue;

            var entry = EntryRef(s);
            if (entry <= 0)
                continue;

            var buy = s.IsBuy;
            for (int i = 0; i < s.TakeProfits.Count; i++)
            {
                var tp = s.TakeProfits[i];
                var dist = Math.Abs(tp - entry);
                if (dist <= 0)
                    continue;
                if (buy && tp <= entry)
                    continue;
                if (!buy && tp >= entry)
                    continue;

                decimal pull;
                if (contract != null)
                {
                    pull = contract.TpEarlyOffset(dist, symbol);
                }
                else
                {
                    var fallbackOffset = isGold ? 1.0m : 0.0010m;
                    pull = Math.Min(dist * 0.25m, fallbackOffset);
                }

                if (pull <= 0)
                    continue;

                var shifted = buy ? tp - pull : tp + pull;
                if (buy && shifted <= entry)
                    continue;
                if (!buy && shifted >= entry)
                    continue;

                s.TakeProfits[i] = RoundPrice(shifted, contract);
            }
        }
    }

    private static decimal EntryRef(EaTradeScenario s)
    {
        if (s.IsBreakout && s.TriggerPrice.HasValue && s.TriggerPrice.Value > 0)
            return s.TriggerPrice.Value;
        if (s.EntryZone != null)
            return (s.EntryZone.Min + s.EntryZone.Max) / 2m;
        return 0;
    }

    private static decimal RoundPrice(decimal price, SymbolContractSpec? contract)
    {
        var digits = contract != null && contract.Digits > 0 ? contract.Digits : 2;
        return decimal.Round(price, digits);
    }
}
