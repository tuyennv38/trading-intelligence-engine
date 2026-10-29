using System.Text.Json;
using TradingIntelligenceEngine.Application.DTOs.Mobius;
using TradingIntelligenceEngine.Domain.AI;
using Xunit;

namespace TradingIntelligenceEngine.Domain.Tests;

public class MobiusScenarioBridgeTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void MobiusScenarioRequest_SerializesProperly()
    {
        var request = new MobiusScenarioRequest
        {
            Symbol = "XAUUSD",
            Interval = "1h",
            Schools = new() { "ICT", "SMC" },
            TopKCards = 3,
            MaxRiskUsd = 100m,
            UseLlm = true,
            Language = "vi"
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);

        Assert.Contains("\"symbol\":\"XAUUSD\"", json);
        Assert.Contains("\"interval\":\"1h\"", json);
        Assert.Contains("\"max_risk_usd\":100", json);
        Assert.Contains("\"ICT\"", json);
        Assert.Contains("\"SMC\"", json);
    }

    [Fact]
    public void MobiusScenarioResponse_DeserializesExactUserSample()
    {
        string sampleJson = """
        {
          "symbol": "XAUUSD",
          "bias": "Bearish",
          "confidence": "high",
          "context": "Giá vàng (XAUUSD) trên khung 1H hiện giao dịch ở mức 4029.93...",
          "max_risk_usd": 100,
          "tp_buffer_applied": 1,
          "notify": true,
          "scenarios": [
            {
              "id": "scenario_1",
              "type": "REVERSAL_SELL",
              "name": "Bán thuận xu hướng HTF tại Bearish Internal Order Block",
              "logic": "Căn cứ nguyên lý ICT Mitigation...",
              "entry_zone": {
                "min": 4051.04,
                "max": 4065
              },
              "trigger_price": null,
              "sl": 4072,
              "tps": [
                4021.09,
                3978.58
              ],
              "max_risk_usd": 100,
              "confirmation": {
                "condition": "PRICE_IN_ZONE",
                "timeframe": "M15",
                "patterns": [
                  "PINBAR",
                  "ENGULFING"
                ]
              },
              "retest": {
                "timeframe": "M5",
                "condition": "RETEST_THEN_CONTINUE",
                "patterns": [
                  "PINBAR",
                  "ENGULFING"
                ]
              },
              "status": "PENDING",
              "tp_buffer_applied": 1
            },
            {
              "id": "scenario_2",
              "type": "REVERSAL_BUY",
              "name": "Mua phản ứng từ Bullish FVG hướng lên đỉnh Weak High",
              "logic": "Theo cấu trúc SMC...",
              "entry_zone": {
                "min": 4014.13,
                "max": 4020.09
              },
              "trigger_price": null,
              "sl": 4009.5,
              "tps": [
                4050.04,
                4080.97
              ],
              "max_risk_usd": 100,
              "confirmation": {
                "condition": "PRICE_IN_ZONE",
                "timeframe": "M15",
                "patterns": [
                  "PINBAR",
                  "ENGULFING"
                ]
              },
              "retest": {
                "timeframe": "M5",
                "condition": "RETEST_THEN_CONTINUE",
                "patterns": [
                  "PINBAR",
                  "ENGULFING"
                ]
              },
              "status": "PENDING",
              "tp_buffer_applied": 1
            }
          ],
          "analysis_summary": "1. Bối cảnh Cấu trúc & Thanh khoản (HTF/LTF)...",
          "retrieved_cards": [
            {
              "card_id": "sp_ac9840a76ff9da7745d71aad",
              "term": "EURUSD 15-Minute Dual Entry Comparison",
              "school": "SMC",
              "content": "Term: EURUSD 15-Minute..."
            }
          ],
          "engine": "llm_rag"
        }
        """;

        var response = JsonSerializer.Deserialize<MobiusScenarioResponse>(sampleJson, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("XAUUSD", response.Symbol);
        Assert.Equal("Bearish", response.Bias);
        Assert.Equal("high", response.Confidence);
        Assert.Equal(100m, response.MaxRiskUsd);
        Assert.True(response.Notify);
        Assert.NotNull(response.Scenarios);
        Assert.Equal(2, response.Scenarios.Count);

        var s1 = response.Scenarios[0];
        Assert.Equal("scenario_1", s1.Id);
        Assert.Equal(EaScenarioType.ReversalSell, s1.Type);
        Assert.NotNull(s1.EntryZone);
        Assert.Equal(4051.04m, s1.EntryZone.Min);
        Assert.Equal(4065m, s1.EntryZone.Max);
        Assert.Equal(4072m, s1.StopLoss);
        Assert.Equal(2, s1.TakeProfits.Count);
        Assert.Equal(4021.09m, s1.TakeProfits[0]);
        Assert.Equal(3978.58m, s1.TakeProfits[1]);
        Assert.NotNull(s1.Confirmation);
        Assert.Equal("M15", s1.Confirmation.Timeframe);
        Assert.NotNull(s1.Retest);
        Assert.Equal("M5", s1.Retest.Timeframe);

        Assert.NotNull(response.RetrievedCards);
        Assert.Single(response.RetrievedCards);
        Assert.Equal("sp_ac9840a76ff9da7745d71aad", response.RetrievedCards[0].CardId);
        Assert.Equal("llm_rag", response.Engine);
    }
}
