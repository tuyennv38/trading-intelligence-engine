using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TradingIntelligenceEngine.Domain.AI;

/// <summary>
/// Kế hoạch giao dịch theo schema EA (snake_case) để MT5 poll và canh lệnh.
/// </summary>
public class TradingPlanResponse
{
    [JsonPropertyName("signal_id")]
    public string SignalId { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("bias")]
    public string Bias { get; set; } = string.Empty;

    [JsonPropertyName("context")]
    public string Context { get; set; } = string.Empty;

    [JsonPropertyName("scenarios")]
    public List<EaTradeScenario> Scenarios { get; set; } = new();

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>ai | manual — kịch bản do AI sinh hoặc người dùng set qua API.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = TradingPlanSource.Ai;

    [JsonIgnore]
    public bool HasScenarios => Scenarios != null && Scenarios.Count > 0;

    [JsonIgnore]
    public bool IsManual =>
        string.Equals(Source, TradingPlanSource.Manual, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool HasActiveScenario => Scenarios != null && Scenarios.Any(s => s.IsActive);

    /// <summary>
    /// Plan rỗng (NO TRADE) vẫn là plan hợp lệ — EA tiếp tục gửi nến, sau đó tự refresh khi muốn hỏi lại.
    /// </summary>
    [JsonIgnore]
    public bool HasNoTrade => Scenarios == null || Scenarios.Count == 0;

    public void EnsureDefaults(string symbol, string signalId)
    {
        Symbol = string.IsNullOrWhiteSpace(Symbol) ? symbol : Symbol;
        SignalId = signalId;
        CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Scenarios ??= new List<EaTradeScenario>();
        if (string.IsNullOrWhiteSpace(Source))
            Source = TradingPlanSource.Ai;

        for (int i = 0; i < Scenarios.Count; i++)
        {
            var s = Scenarios[i];
            if (string.IsNullOrWhiteSpace(s.Id))
                s.Id = $"scenario_{i + 1}_{s.Type}".ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(s.Status))
                s.Status = EaScenarioStatus.Pending;
            s.TakeProfits ??= new List<decimal>();
            if (s.Confirmation != null)
                s.Confirmation.Patterns ??= new List<string>();
        }
    }
}

public static class TradingPlanSource
{
    public const string Ai = "ai";
    public const string Manual = "manual";
}

public static class EaScenarioStatus
{
    public const string Pending = "PENDING";
    public const string Active = "ACTIVE";
    public const string HitSl = "HIT_SL";
    public const string HitTp = "HIT_TP";
    public const string Cancelled = "CANCELLED";
}

public static class EaScenarioType
{
    public const string ReversalBuy = "REVERSAL_BUY";
    public const string ReversalSell = "REVERSAL_SELL";
    public const string BreakoutBuy = "BREAKOUT_BUY";
    public const string BreakoutSell = "BREAKOUT_SELL";
}

public class EaTradeScenario
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>REVERSAL_BUY | REVERSAL_SELL | BREAKOUT_BUY | BREAKOUT_SELL</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Mô tả logic vào lệnh (tiếng Việt) để hiển thị Chat / EA.</summary>
    [JsonPropertyName("logic")]
    public string Logic { get; set; } = string.Empty;

    /// <summary>Bắt buộc với REVERSAL_*; null với BREAKOUT_*.</summary>
    [JsonPropertyName("entry_zone")]
    public EntryZone? EntryZone { get; set; }

    /// <summary>Bắt buộc với BREAKOUT_*; null với REVERSAL_*.</summary>
    [JsonPropertyName("trigger_price")]
    public decimal? TriggerPrice { get; set; }

    [JsonPropertyName("sl")]
    public decimal StopLoss { get; set; }

    [JsonPropertyName("tps")]
    public List<decimal> TakeProfits { get; set; } = new();

    [JsonPropertyName("confirmation")]
    public ScenarioConfirmation? Confirmation { get; set; }

    /// <summary>PENDING | ACTIVE | HIT_SL | HIT_TP | CANCELLED</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = EaScenarioStatus.Pending;

    [JsonIgnore]
    public bool IsActive => string.Equals(Status, EaScenarioStatus.Active, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsBuy =>
        Type.Contains("BUY", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsSell =>
        Type.Contains("SELL", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsBreakout =>
        Type.StartsWith("BREAKOUT", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsReversal =>
        Type.StartsWith("REVERSAL", StringComparison.OrdinalIgnoreCase);
}

public class EntryZone
{
    [JsonPropertyName("min")]
    public decimal Min { get; set; }

    [JsonPropertyName("max")]
    public decimal Max { get; set; }
}

public class ScenarioConfirmation
{
    /// <summary>PRICE_IN_ZONE | CLOSE_BELOW_TRIGGER | CLOSE_ABOVE_TRIGGER</summary>
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = "M15";

    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; set; } = new();
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
