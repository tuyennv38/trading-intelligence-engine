using System.Collections.Generic;

namespace TradingIntelligenceEngine.Domain.AI;

public record TradingZone(
    string ZoneName,
    string Logic,
    decimal EntryBottom,
    decimal EntryTop,
    decimal StopLoss,
    List<decimal>? TakeProfits,
    decimal RiskRewardRatio
)
{
    public bool IsActive { get; set; } = false;
}

public record BreakoutScenario(
    string Type,
    string Condition,
    decimal TriggerPrice,
    decimal StopLoss,
    List<decimal>? TakeProfits
)
{
    public bool IsActive { get; set; } = false;
}

public record TradingPlanResponse(
    string MarketContext,
    string Bias,
    List<TradingZone> BuyScenarios,
    List<TradingZone> SellScenarios,
    List<BreakoutScenario> BreakoutScenarios
)
{
    public string SignalId { get; set; } = string.Empty;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

public interface ITradingStrategistEngine
{
    Task<TradingPlanResponse> GeneratePlanAsync(
        string symbol,
        Dictionary<string, MarketState.MarketState> multiTimeframeStates,
        TradingPlanResponse? previousPlan,
        string triggerReason,
        System.Threading.CancellationToken cancellationToken);
}

/// <summary>
/// Nến OHLC đưa vào AI nhận dạng cấu trúc thị trường.
/// </summary>
public record AiCandleInput(
    long Time,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long TickVolume = 0,
    decimal? EmaFast = null,
    decimal? EmaSlow = null,
    decimal? Rsi = null,
    decimal? Atr = null,
    decimal? Adx = null
);

public interface IAiMarketStructureEngine
{
    Task<MarketState.MarketState> AnalyzeStructureAsync(
        string symbol,
        string timeframe,
        IReadOnlyList<AiCandleInput> candles,
        System.Threading.CancellationToken cancellationToken);
}
