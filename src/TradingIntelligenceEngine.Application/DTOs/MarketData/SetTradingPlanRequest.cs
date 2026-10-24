using System.Collections.Generic;
using System.Text.Json.Serialization;
using TradingIntelligenceEngine.Domain.AI;

namespace TradingIntelligenceEngine.Application.DTOs.MarketData;

/// <summary>
/// Body để chủ động set kịch bản (thay plan AI). Schema giống EA / plan hiện tại.
/// </summary>
public class SetTradingPlanRequest
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = "XAUUSD";

    [JsonPropertyName("bias")]
    public string Bias { get; set; } = string.Empty;

    [JsonPropertyName("context")]
    public string Context { get; set; } = string.Empty;

    [JsonPropertyName("scenarios")]
    public List<EaTradeScenario> Scenarios { get; set; } = new();

    /// <summary>Để trống thì server tự gán.</summary>
    [JsonPropertyName("signal_id")]
    public string? SignalId { get; set; }

    /// <summary>Mặc định true — đẩy Google Chat sau khi set.</summary>
    [JsonPropertyName("notify")]
    public bool Notify { get; set; } = true;

    /// <summary>Trần lỗ chấp nhận mỗi lệnh (USD). 0 = lấy từ session / mặc định 100.</summary>
    [JsonPropertyName("max_risk_usd")]
    public decimal MaxRiskUsd { get; set; }

    public TradingPlanResponse ToPlan()
    {
        return new TradingPlanResponse
        {
            Symbol = Symbol,
            Bias = Bias ?? string.Empty,
            Context = Context ?? string.Empty,
            Scenarios = Scenarios ?? new List<EaTradeScenario>(),
            SignalId = SignalId ?? string.Empty,
            Source = TradingPlanSource.Manual,
            MaxRiskUsd = MaxRiskUsd
        };
    }
}
