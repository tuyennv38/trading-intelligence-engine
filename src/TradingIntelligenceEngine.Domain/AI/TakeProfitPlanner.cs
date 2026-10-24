using TradingIntelligenceEngine.Domain.MarketData;

namespace TradingIntelligenceEngine.Domain.AI;

/// <summary>
/// Giữ nguyên cách chọn TP, chỉ dịch mỗi mức về phía entry một chút để trừ spread.
/// </summary>
public static class TakeProfitPlanner
{
    public static void PullEarlier(TradingPlanResponse plan, SymbolContractSpec? contract)
    {
        if (plan?.Scenarios == null || plan.Scenarios.Count == 0)
            return;

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

                var pull = contract?.TpEarlyOffset(dist) ?? Math.Min(dist * 0.10m, 0.01m * 8m);
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
