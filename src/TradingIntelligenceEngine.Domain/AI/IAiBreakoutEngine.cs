using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.Domain.AI;

public interface IAiBreakoutEngine
{
    /// <summary>
    /// Sử dụng LLM và cả 2 bộ Skill (pattern-recognition & breakout-scenario-planning)
    /// để đọc chuỗi nến, nhận diện cấu trúc mô hình và lập kịch bản Breakout.
    /// </summary>
    Task<BreakoutScenarioResult> AnalyzeBreakoutWithSkillsAsync(
        string symbol,
        string timeframe,
        IReadOnlyList<AiCandleInput> candles,
        decimal maxRiskUsd = 100m,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AI chỉ lập kịch bản Breakout (theo Skill breakout-scenario-planning)
    /// dựa trên kết quả cấu trúc mô hình đã được Thuật toán Định lượng (Quant) nhận diện trước đó.
    /// </summary>
    Task<BreakoutScenarioResult> PlanScenarioWithAiAsync(
        PatternRecognitionResult patternResult,
        IReadOnlyList<AiCandleInput> candles,
        decimal maxRiskUsd = 100m,
        CancellationToken cancellationToken = default);
}
