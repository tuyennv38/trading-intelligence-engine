using System.Collections.Generic;
using System.Text.Json.Serialization;
using TradingIntelligenceEngine.Domain.AI;

namespace TradingIntelligenceEngine.Application.DTOs.Mobius;

/// <summary>
/// Tham số gửi sang Mobius Strategy Scenarios API (ICT/SMC).
/// </summary>
public class MobiusScenarioRequest
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "XAUUSD";

    [JsonPropertyName("interval")]
    public string Interval { get; set; } = "1h";

    [JsonPropertyName("schools")]
    public List<string> Schools { get; set; } = new() { "ICT", "SMC" };

    [JsonPropertyName("top_k_cards")]
    public int TopKCards { get; set; } = 3;

    [JsonPropertyName("max_risk_usd")]
    public decimal MaxRiskUsd { get; set; } = 100m;

    [JsonPropertyName("use_llm")]
    public bool UseLlm { get; set; } = true;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "vi";
}

/// <summary>
/// Dữ liệu phản hồi từ Mobius Strategy Scenarios API.
/// </summary>
public class MobiusScenarioResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("bias")]
    public string Bias { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public string? Confidence { get; set; }

    [JsonPropertyName("context")]
    public string Context { get; set; } = string.Empty;

    [JsonPropertyName("max_risk_usd")]
    public decimal MaxRiskUsd { get; set; }

    [JsonPropertyName("tp_buffer_applied")]
    public decimal? TpBufferApplied { get; set; }

    [JsonPropertyName("notify")]
    public bool Notify { get; set; }

    [JsonPropertyName("scenarios")]
    public List<EaTradeScenario> Scenarios { get; set; } = new();

    [JsonPropertyName("analysis_summary")]
    public string? AnalysisSummary { get; set; }

    [JsonPropertyName("retrieved_cards")]
    public List<MobiusRetrievedCard>? RetrievedCards { get; set; }

    [JsonPropertyName("engine")]
    public string? Engine { get; set; }

    /// <summary>Signal ID do bridge sinh để EA theo dõi tương thích.</summary>
    [JsonPropertyName("signal_id")]
    public string SignalId { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; }

    [JsonPropertyName("has_scenarios")]
    public bool HasScenarios => Scenarios != null && Scenarios.Count > 0;

    [JsonPropertyName("no_trade")]
    public bool NoTrade => Scenarios == null || Scenarios.Count == 0;

    [JsonPropertyName("plan")]
    public TradingPlanResponse? Plan { get; set; }
}

public class MobiusRetrievedCard
{
    [JsonPropertyName("card_id")]
    public string CardId { get; set; } = string.Empty;

    [JsonPropertyName("term")]
    public string Term { get; set; } = string.Empty;

    [JsonPropertyName("school")]
    public string School { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}
