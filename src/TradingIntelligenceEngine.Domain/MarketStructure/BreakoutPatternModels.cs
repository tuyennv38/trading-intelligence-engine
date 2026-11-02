using System.Collections.Generic;
using System.Text.Json.Serialization;
using TradingIntelligenceEngine.Domain.AI;

namespace TradingIntelligenceEngine.Domain.MarketStructure;

public class BoundaryZone
{
    [JsonPropertyName("low")]
    public decimal? Low { get; set; }

    [JsonPropertyName("high")]
    public decimal? High { get; set; }

    [JsonPropertyName("touch_count")]
    public int TouchCount { get; set; }

    [JsonIgnore]
    public decimal Center => (Low.HasValue && High.HasValue) ? (Low.Value + High.Value) / 2m : (Low ?? High ?? 0m);

    [JsonIgnore]
    public decimal Width => (Low.HasValue && High.HasValue) ? (High.Value - Low.Value) : 0m;
}

public class PatternDetail
{
    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty; // A, B, C, D

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "DEVELOPING"; // NOT_FOUND, DEVELOPING, POTENTIAL, CONFIRMED, INVALIDATED

    [JsonPropertyName("confidence")]
    public string Confidence { get; set; } = "MEDIUM"; // LOW, MEDIUM, HIGH
}

public class PatternZones
{
    [JsonPropertyName("resistance")]
    public BoundaryZone Resistance { get; set; } = new();

    [JsonPropertyName("support")]
    public BoundaryZone Support { get; set; } = new();
}

public class PatternRecognitionResult
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "XAUUSD";

    [JsonPropertyName("timeframe")]
    public string Timeframe { get; set; } = "M5";

    [JsonPropertyName("market_state")]
    public string MarketState { get; set; } = "RANGE"; // RANGE, TREND_UP, TREND_DOWN

    [JsonPropertyName("primary_pattern")]
    public PatternDetail PrimaryPattern { get; set; } = new();

    [JsonPropertyName("other_candidates")]
    public List<PatternDetail> OtherCandidates { get; set; } = new();

    [JsonPropertyName("zones")]
    public PatternZones Zones { get; set; } = new();

    [JsonPropertyName("evidence")]
    public List<string> Evidence { get; set; } = new();

    [JsonPropertyName("missing_evidence")]
    public List<string> MissingEvidence { get; set; } = new();

    [JsonPropertyName("current_price")]
    public decimal CurrentPrice { get; set; }

    [JsonPropertyName("atr")]
    public decimal Atr { get; set; }

    [JsonPropertyName("candle_count")]
    public int CandleCount { get; set; }
}

public class BreakoutScenarioResult
{
    [JsonPropertyName("pattern_recognition")]
    public PatternRecognitionResult PatternRecognition { get; set; } = new();

    [JsonPropertyName("trading_plan")]
    public TradingPlanResponse TradingPlan { get; set; } = new();

    [JsonPropertyName("current_status")]
    public string CurrentStatus { get; set; } = "MONITOR"; // WAIT, MONITOR, SCENARIO_CONFIRMED

    [JsonPropertyName("summary_report")]
    public string SummaryReport { get; set; } = string.Empty;
}
