using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Api.Services;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.MarketState;

namespace TradingIntelligenceEngine.Api.Jobs;

public interface IAiNotificationJob
{
    /// <param name="analysisResults">Kết quả Quant (khi useAiStructure=false). Có thể null nếu useAiStructure=true.</param>
    /// <param name="useAiStructure">true = AI nhận dạng cấu trúc từ candlesByTimeframe trước, rồi mới lập kế hoạch.</param>
    /// <param name="candlesByTimeframe">Nến thô theo khung (bắt buộc khi useAiStructure=true).</param>
    Task GenerateAndNotifyPlanAsync(
        string symbol,
        Dictionary<string, MarketState>? analysisResults,
        string triggerReason,
        bool useAiStructure,
        Dictionary<string, List<AiCandleInput>>? candlesByTimeframe);
}

public class AiNotificationJob : IAiNotificationJob
{
    private readonly ITradingStrategistEngine _aiStrategist;
    private readonly IAiMarketStructureEngine _aiStructure;
    private readonly INotificationService _notifier;
    private readonly IMarketDataStore _marketDataStore;
    private readonly ILogger<AiNotificationJob> _logger;

    public AiNotificationJob(
        ITradingStrategistEngine aiStrategist,
        IAiMarketStructureEngine aiStructure,
        INotificationService notifier,
        IMarketDataStore marketDataStore,
        ILogger<AiNotificationJob> logger)
    {
        _aiStrategist = aiStrategist;
        _aiStructure = aiStructure;
        _notifier = notifier;
        _marketDataStore = marketDataStore;
        _logger = logger;
    }

    public async Task GenerateAndNotifyPlanAsync(
        string symbol,
        Dictionary<string, MarketState>? analysisResults,
        string triggerReason,
        bool useAiStructure,
        Dictionary<string, List<AiCandleInput>>? candlesByTimeframe)
    {
        try
        {
            var jobStartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Dictionary<string, MarketState> states;

            if (useAiStructure)
            {
                if (candlesByTimeframe == null || candlesByTimeframe.Count == 0)
                    throw new InvalidOperationException("useAiStructure=true nhưng không có candlesByTimeframe.");

                _logger.LogInformation("Hangfire Job: AI nhận dạng cấu trúc cho {Symbol}...", symbol);
                states = new Dictionary<string, MarketState>();

                foreach (var kvp in candlesByTimeframe)
                {
                    if (kvp.Value == null || kvp.Value.Count == 0)
                        continue;

                    var state = await _aiStructure.AnalyzeStructureAsync(
                        symbol, kvp.Key, kvp.Value, CancellationToken.None);
                    states[kvp.Key] = state;
                }

                if (states.Count == 0)
                    throw new InvalidOperationException("AI structure không tạo được MarketState nào.");

                await NotifyStructureSummaryAsync(symbol, states, fromAi: true);
            }
            else
            {
                if (analysisResults == null || analysisResults.Count == 0)
                    throw new InvalidOperationException("analysisResults trống khi useAiStructure=false.");

                states = analysisResults;
                _logger.LogInformation("Hangfire Job: Dùng cấu trúc Quant cho {Symbol}...", symbol);
            }

            _logger.LogInformation("Hangfire Job: Bắt đầu gọi AI Strategist cho {Symbol}...", symbol);

            await _notifier.SendMessageAsync(
                $"⏳ *{symbol}*\nĐang chờ AI lên kịch bản chi tiết.");

            var previousPlan = await _marketDataStore.GetLatestPlanAsync(symbol);
            var session = await _marketDataStore.GetSymbolSessionAsync(symbol);
            var maxRiskUsd = TradingPlanResponse.NormalizeMaxRiskUsd(
                session?.MaxRiskUsd ?? TradingPlanResponse.DefaultMaxRiskUsd);
            var tradingPlan = await _aiStrategist.GeneratePlanAsync(
                symbol, states, previousPlan, triggerReason, maxRiskUsd, session?.Contract, CancellationToken.None);

            var existing = await _marketDataStore.GetLatestPlanAsync(symbol);
            if (existing != null && existing.IsManual && existing.CreatedAt >= jobStartedAt)
            {
                _logger.LogInformation(
                    "Giữ kịch bản thủ công của {Symbol}, không ghi đè bằng AI.", symbol);
                return;
            }

            var signalId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{symbol.ToUpperInvariant()}";
            tradingPlan.EnsureDefaults(symbol, signalId, maxRiskUsd);
            TakeProfitPlanner.PullEarlier(tradingPlan, session?.Contract);
            tradingPlan.Source = TradingPlanSource.Ai;

            var origin = useAiStructure
                ? "AI STRATEGIST — cấu trúc AI"
                : "AI STRATEGIST — cấu trúc Quant";
            var planMsg = TradingPlanChatFormatter.BuildPlanMessage(symbol, tradingPlan, origin);

            await _marketDataStore.SetLatestStateAsync(symbol, states);
            await _marketDataStore.SetLatestPlanAsync(symbol, tradingPlan);

            await _notifier.SendMessageAsync(planMsg);
            _logger.LogInformation("Hangfire Job: Hoàn thành tạo và gửi Kế hoạch giao dịch cho {Symbol}.", symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi Hangfire Job khi gọi AI Strategist.");
            await _notifier.SendMessageAsync($"⚠️ Lỗi tạo Kế hoạch cho {symbol}: {ex.Message}");
            throw;
        }
        finally
        {
            var session = await _marketDataStore.GetSymbolSessionAsync(symbol);
            if (session != null)
            {
                session.PlanRefreshInProgress = false;
                await _marketDataStore.SetSymbolSessionAsync(symbol, session);
            }
        }
    }

    private async Task NotifyStructureSummaryAsync(string symbol, Dictionary<string, MarketState> states, bool fromAi)
    {
        var msgBuilder = new System.Text.StringBuilder();
        msgBuilder.AppendLine("🚀 *[AI TRADING STRATEGIST]*");
        msgBuilder.AppendLine(fromAi
            ? "AI đã nhận dạng Cấu Trúc Thị Trường (Giai đoạn 1)!\n"
            : "Đã hoàn thành quét Cấu Trúc Thị Trường (Giai đoạn 1)!\n");
        msgBuilder.AppendLine($"📊 *Cặp giao dịch:* {symbol}\n");

        foreach (var kvp in states)
        {
            var tf = kvp.Key;
            var state = kvp.Value;

            string trend = state.Trend?.Direction.ToString() == "Bullish" ? "🟢 Tăng (Bullish)" : "🔴 Giảm (Bearish)";
            string structureStr = state.Structure?.LastLabel.ToString() switch
            {
                "HH" => "Tạo Đỉnh cao dần (Higher High)",
                "HL" => "Tạo Đáy cao dần (Higher Low)",
                "LH" => "Tạo Đỉnh thấp dần (Lower High)",
                "LL" => "Tạo Đáy thấp dần (Lower Low)",
                _ => "Đi ngang (Ranging/Sideway)"
            };

            msgBuilder.AppendLine($"📍 *Khung {tf}:*");
            msgBuilder.AppendLine($"- Giá hiện tại: {state.CurrentPrice}");
            msgBuilder.AppendLine($"- Xu hướng chính: {trend}");
            msgBuilder.AppendLine($"- Hành vi giá: {structureStr}");
            msgBuilder.AppendLine();
        }

        await _notifier.SendMessageAsync(msgBuilder.ToString());
    }
}
