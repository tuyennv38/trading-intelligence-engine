using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

            var previousPlan = await _marketDataStore.GetLatestPlanAsync(symbol);
            var tradingPlan = await _aiStrategist.GeneratePlanAsync(
                symbol, states, previousPlan, triggerReason, CancellationToken.None);

            var signalId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{symbol.ToUpperInvariant()}";
            tradingPlan.EnsureDefaults(symbol, signalId);

            var planMsgBuilder = new System.Text.StringBuilder();
            planMsgBuilder.AppendLine($"🚨 *KẾ HOẠCH GIAO DỊCH {symbol} (AI STRATEGIST)* 🚨\n");
            planMsgBuilder.AppendLine(useAiStructure
                ? "Cấu trúc thị trường: AI\n"
                : "Cấu trúc thị trường: Quant\n");

            planMsgBuilder.AppendLine($"*BIAS:* {tradingPlan.Bias}");
            planMsgBuilder.AppendLine($"*CONTEXT:* {tradingPlan.Context}\n");

            var sellScenarios = tradingPlan.Scenarios.Where(s => s.IsSell).ToList();
            var buyScenarios = tradingPlan.Scenarios.Where(s => s.IsBuy).ToList();

            if (sellScenarios.Count > 0)
            {
                planMsgBuilder.AppendLine("🔴 *KỊCH BẢN BÁN (SELL)*");
                foreach (var s in sellScenarios)
                    AppendScenarioChatLines(planMsgBuilder, s, isBuy: false);
                planMsgBuilder.AppendLine();
            }

            if (buyScenarios.Count > 0)
            {
                planMsgBuilder.AppendLine("🟢 *KỊCH BẢN MUA (BUY)*");
                foreach (var s in buyScenarios)
                    AppendScenarioChatLines(planMsgBuilder, s, isBuy: true);
                planMsgBuilder.AppendLine();
            }

            if (!tradingPlan.HasScenarios)
            {
                planMsgBuilder.AppendLine("⛔ *KHUYẾN NGHỊ: ĐỨNG NGOÀI (NO TRADE)*");
                planMsgBuilder.AppendLine("Thị trường hiện tại không có setup an toàn. Vui lòng bảo vệ vốn.");
            }

            await _marketDataStore.SetLatestStateAsync(symbol, states);
            await _marketDataStore.SetLatestPlanAsync(symbol, tradingPlan);

            await _notifier.SendMessageAsync(planMsgBuilder.ToString());
            _logger.LogInformation("Hangfire Job: Hoàn thành tạo và gửi Kế hoạch giao dịch cho {Symbol}.", symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi Hangfire Job khi gọi AI Strategist.");
            await _notifier.SendMessageAsync($"⚠️ Lỗi tạo Kế hoạch cho {symbol}: {ex.Message}");
            throw;
        }
    }

    private static void AppendScenarioChatLines(System.Text.StringBuilder sb, EaTradeScenario s, bool isBuy)
    {
        string bulletIcon = isBuy ? "🟢" : "🔴";
        string displayName = string.IsNullOrWhiteSpace(s.Name) ? s.Id : s.Name;
        sb.AppendLine($"• {displayName}:");

        decimal refPrice;
        if (s.IsBreakout)
        {
            refPrice = s.TriggerPrice ?? 0;
            sb.AppendLine($"  {bulletIcon} Kích hoạt : {refPrice:0.00}");
        }
        else if (s.EntryZone != null)
        {
            refPrice = (s.EntryZone.Min + s.EntryZone.Max) / 2;
            sb.AppendLine($"  {bulletIcon} Entry : {s.EntryZone.Min:0.00} - {s.EntryZone.Max:0.00}");
        }
        else
        {
            refPrice = 0;
            sb.AppendLine($"  {bulletIcon} Entry : N/A");
        }

        decimal slPts = Math.Abs(refPrice - s.StopLoss);
        sb.AppendLine($"  🛡  SL    : {s.StopLoss:0.00} (-{slPts:0.00} pts)");

        if (s.TakeProfits != null && s.TakeProfits.Count > 0)
        {
            for (int i = 0; i < s.TakeProfits.Count; i++)
            {
                decimal tpValue = s.TakeProfits[i];
                decimal tpPts = Math.Abs(tpValue - refPrice);
                decimal rr = slPts > 0 ? tpPts / slPts : 0;
                sb.AppendLine($"  🎯 TP{i + 1}   : {tpValue:0.00} (+{tpPts:0.00} pts) R:R 1:{rr:0.0}");
            }
        }
        else
        {
            sb.AppendLine("  🎯 TP    : N/A");
        }

        string logic = !string.IsNullOrWhiteSpace(s.Logic)
            ? s.Logic
            : BuildFallbackLogic(s);
        sb.AppendLine($"  💡 Logic : {logic}");
    }

    private static string BuildFallbackLogic(EaTradeScenario s)
    {
        var patterns = s.Confirmation?.Patterns != null && s.Confirmation.Patterns.Count > 0
            ? string.Join(" hoặc ", s.Confirmation.Patterns)
            : "mô hình nến xác nhận";
        var tf = s.Confirmation?.Timeframe ?? "M15";

        if (s.IsBreakout)
        {
            string side = s.IsBuy ? "Mua" : "Bán";
            string cond = s.IsBuy ? "đóng cửa trên" : "đóng cửa dưới";
            return $"Chờ nến {tf} {cond} mức kích hoạt {s.TriggerPrice:0.00}, xác nhận bằng {patterns} rồi {side} theo breakout.";
        }

        if (s.EntryZone != null)
        {
            string side = s.IsBuy ? "Mua" : "Bán";
            return $"Chờ giá vào vùng {s.EntryZone.Min:0.00}-{s.EntryZone.Max:0.00} và xuất hiện {patterns} trên {tf} rồi {side}.";
        }

        return s.Name;
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

        msgBuilder.AppendLine("⏳ *Đang chuyển dữ liệu cho AI (Giai đoạn 2) để lên Kịch bản chi tiết...*");
        await _notifier.SendMessageAsync(msgBuilder.ToString());
    }
}
