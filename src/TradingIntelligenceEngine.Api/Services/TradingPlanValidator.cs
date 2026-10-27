using TradingIntelligenceEngine.Domain.AI;

namespace TradingIntelligenceEngine.Api.Services;

public static class TradingPlanValidator
{
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        EaScenarioType.ReversalBuy,
        EaScenarioType.ReversalSell,
        EaScenarioType.BreakoutBuy,
        EaScenarioType.BreakoutSell
    };

    public static List<string> Validate(TradingPlanResponse plan)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(plan.Symbol))
            errors.Add("symbol is required.");

        plan.Scenarios ??= new List<EaTradeScenario>();
        for (int i = 0; i < plan.Scenarios.Count; i++)
        {
            var s = plan.Scenarios[i];
            var prefix = $"scenarios[{i}]";

            if (string.IsNullOrWhiteSpace(s.Type) || !AllowedTypes.Contains(s.Type))
            {
                errors.Add($"{prefix}.type phải là REVERSAL_BUY | REVERSAL_SELL | BREAKOUT_BUY | BREAKOUT_SELL.");
                continue;
            }

            if (s.StopLoss <= 0)
                errors.Add($"{prefix}.sl phải > 0.");

            if (s.TakeProfits == null || s.TakeProfits.Count == 0)
                errors.Add($"{prefix}.tps phải có ít nhất 1 mức.");
            else if (s.TakeProfits.Count > 2)
                errors.Add($"{prefix}.tps tối đa 2 mức (TP1, TP2). Bỏ hẳn TP3.");

            if (s.IsReversal)
            {
                if (s.EntryZone == null)
                    errors.Add($"{prefix}.entry_zone bắt buộc với REVERSAL_*.");
                else if (s.EntryZone.Min > s.EntryZone.Max)
                    errors.Add($"{prefix}.entry_zone.min phải <= max.");
            }

            if (s.IsBreakout)
            {
                if (!s.TriggerPrice.HasValue || s.TriggerPrice.Value <= 0)
                    errors.Add($"{prefix}.trigger_price bắt buộc với BREAKOUT_*.");
                if (s.Retest == null || string.IsNullOrWhiteSpace(s.Retest.Timeframe))
                    errors.Add($"{prefix}.retest.timeframe bắt buộc với BREAKOUT_* (khung chờ hồi về mức phá vỡ, ví dụ M5).");
            }
        }

        return errors;
    }
}
