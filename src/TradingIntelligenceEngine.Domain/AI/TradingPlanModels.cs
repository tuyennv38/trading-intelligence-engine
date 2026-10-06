using System.Collections.Generic;

namespace TradingIntelligenceEngine.Domain.AI;

public record TradingZone(
    string ZoneName,
    string Logic,
    decimal EntryBottom,
    decimal EntryTop,
    decimal StopLoss,
    decimal RiskRewardRatio
);

public record BreakoutScenario(
    string Type,
    string Condition,
    decimal TriggerPrice,
    decimal StopLoss,
    decimal Target
);

public record TradingPlanResponse(
    string MarketContext,
    string Bias,
    List<TradingZone> BuyScenarios,
    List<TradingZone> SellScenarios,
    List<BreakoutScenario> BreakoutScenarios
);

public interface ITradingStrategistEngine
{
    Task<TradingPlanResponse> GeneratePlanAsync(
        string symbol,
        Dictionary<string, MarketState.MarketState> multiTimeframeStates,
        System.Threading.CancellationToken cancellationToken);
}