using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.Application.Interfaces;

public interface IBreakoutPatternService
{
    /// <summary>
    /// Nhận diện mô hình cấu trúc giá (Group A: Horizontal, Group B: Triangles, Group C: Channels/Wedges, Group D: Flags)
    /// dựa trên bộ kỹ năng pattern-recognition skill.
    /// </summary>
    Task<PatternRecognitionResult> RecognizePatternAsync(
        string symbol,
        string timeframe,
        IReadOnlyList<Candle> candles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lập các kịch bản Breakout (Breakout lên, Breakdown xuống, Tiếp tục tích lũy, False Breakout)
    /// theo bộ kỹ năng breakout-scenario-planning skill và sinh TradingPlanResponse cho EA.
    /// </summary>
    Task<BreakoutScenarioResult> PlanBreakoutScenarioAsync(
        PatternRecognitionResult patternResult,
        IReadOnlyList<Candle> candles,
        decimal maxRiskUsd = 100m,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Luồng tự động: Lấy nến từ Mobius API, nhận diện cấu trúc, tạo kịch bản breakout, lưu vào Store và bắn thông báo.
    /// patternMode & scenarioMode: "AI" hoặc "Quant" (nếu null sẽ lấy theo cấu hình trong appsettings/BreakoutOptions).
    /// </summary>
    Task<BreakoutScenarioResult> AnalyzeAndExecuteBreakoutSyncAsync(
        string? symbol = null,
        string? interval = null,
        int? limit = null,
        decimal? maxRiskUsd = null,
        string? patternMode = null,
        string? scenarioMode = null,
        bool notify = true,
        CancellationToken cancellationToken = default);
}
