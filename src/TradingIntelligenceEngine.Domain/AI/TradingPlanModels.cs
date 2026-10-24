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

            if (s.IsBreakout)
            {
                s.Retest ??= new ScenarioRetest();
                s.Retest.Timeframe = ScenarioRetest.OneStepSmaller(s.Confirmation?.Timeframe);
                if (string.IsNullOrWhiteSpace(s.Retest.Condition))
                    s.Retest.Condition = EaRetestCondition.RetestThenContinue;
                s.Retest.Patterns ??= new List<string>();
            }
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

    /// <summary>
    /// Bắt buộc với BREAKOUT_*: khung nhỏ hơn confirmation để chờ hồi về mức phá vỡ rồi xác nhận tiếp diễn.
    /// REVERSAL_* để null.
    /// </summary>
    [JsonPropertyName("retest")]
    public ScenarioRetest? Retest { get; set; }

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

    /// <summary>Khung nến xác nhận phá vỡ / vào vùng (ví dụ M15).</summary>
    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = "M15";

    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; set; } = new();
}

public static class EaRetestCondition
{
    /// <summary>Giá hồi về trigger, nến khung retest xác nhận tiếp diễn rồi mới vào lệnh.</summary>
    public const string RetestThenContinue = "RETEST_THEN_CONTINUE";
}

/// <summary>
/// BREAKOUT: confirm TF đóng cửa phá trigger, rồi chờ retest TF nhỏ hơn (ví dụ confirm M15, retest M5).
/// </summary>
public class ScenarioRetest
{
    /// <summary>Khung chờ hồi về mức phá vỡ (nhỏ hơn confirmation.timeframe, ví dụ M5).</summary>
    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = "M5";

    /// <summary>RETEST_THEN_CONTINUE</summary>
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = EaRetestCondition.RetestThenContinue;

    /// <summary>Mô hình nến trên khung retest xác nhận tiếp diễn sau khi chạm lại trigger.</summary>
    [JsonPropertyName("patterns")]
    public List<string> Patterns { get; set; } = new();

    /// <summary>M1 &lt; M5 &lt; M15 &lt; H1 &lt; H4 &lt; D1 (M30 map về M15).</summary>
    public static readonly string[] Ladder = { "M1", "M5", "M15", "H1", "H4", "D1" };

    public static string DefaultTimeframe(string? confirmTimeframe) => OneStepSmaller(confirmTimeframe);

    /// <summary>Đúng 1 bậc nhỏ hơn khung confirm. Confirm M1 thì giữ M1.</summary>
    public static string OneStepSmaller(string? confirmTimeframe)
    {
        var tf = (confirmTimeframe ?? "M15").Trim().ToUpperInvariant();
        if (tf == "M30")
            return "M15";
        int i = LadderIndex(tf);
        if (i <= 0)
            return "M1";
        return Ladder[i - 1];
    }

    public static bool IsStrictlySmaller(string? retestTimeframe, string? confirmTimeframe)
    {
        int retest = LadderIndex(retestTimeframe);
        int confirm = LadderIndex(confirmTimeframe);
        if (retest < 0 || confirm < 0)
            return false;
        if (confirm == 0)
            return retest == 0;
        return retest < confirm;
    }

    public static int LadderIndex(string? timeframe)
    {
        var tf = (timeframe ?? "").Trim().ToUpperInvariant();
        return Array.IndexOf(Ladder, tf);
    }
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
