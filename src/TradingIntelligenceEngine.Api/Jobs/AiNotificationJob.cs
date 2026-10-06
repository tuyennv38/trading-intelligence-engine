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
    Task GenerateAndNotifyPlanAsync(string symbol, Dictionary<string, MarketState> analysisResults);
}

public class AiNotificationJob : IAiNotificationJob
{
    private readonly ITradingStrategistEngine _aiStrategist;
    private readonly INotificationService _notifier;
    private readonly IMarketDataStore _marketDataStore;
    private readonly ILogger<AiNotificationJob> _logger;

    public AiNotificationJob(
        ITradingStrategistEngine aiStrategist,
        INotificationService notifier,
        IMarketDataStore marketDataStore,
        ILogger<AiNotificationJob> logger)
    {
        _aiStrategist = aiStrategist;
        _notifier = notifier;
        _marketDataStore = marketDataStore;
        _logger = logger;
    }

    public async Task GenerateAndNotifyPlanAsync(string symbol, Dictionary<string, MarketState> analysisResults)
    {
        try
        {
            _logger.LogInformation("Hangfire Job: Bắt đầu gọi AI Strategist cho {Symbol}...", symbol);
            
            // Hangfire doesn't pass a live CancellationToken out of the box unless injected, using CancellationToken.None
            var tradingPlan = await _aiStrategist.GeneratePlanAsync(symbol, analysisResults, CancellationToken.None);
            
            var planMsgBuilder = new System.Text.StringBuilder();
            planMsgBuilder.AppendLine($"🚨 *KẾ HOẠCH GIAO DỊCH {symbol} (AI STRATEGIST)* 🚨\n");
            
            planMsgBuilder.AppendLine($"*BIAS:* {tradingPlan.Bias}");
            planMsgBuilder.AppendLine($"*CONTEXT:* {tradingPlan.MarketContext}\n");

            if (tradingPlan.SellScenarios != null && tradingPlan.SellScenarios.Any())
            {
                planMsgBuilder.AppendLine("🔴 *KỊCH BẢN BÁN (SELL)*");
                foreach (var s in tradingPlan.SellScenarios)
                {
                    planMsgBuilder.AppendLine($"• {s.ZoneName}:");
                    planMsgBuilder.AppendLine($"  - Vùng giá: {s.EntryBottom} - {s.EntryTop}");
                    planMsgBuilder.AppendLine($"  - Stop Loss: {s.StopLoss} (R:R ~ {s.RiskRewardRatio})");
                    planMsgBuilder.AppendLine($"  - Logic: {s.Logic}");
                }
                planMsgBuilder.AppendLine();
            }

            if (tradingPlan.BuyScenarios != null && tradingPlan.BuyScenarios.Any())
            {
                planMsgBuilder.AppendLine("🟢 *KỊCH BẢN MUA (BUY)*");
                foreach (var b in tradingPlan.BuyScenarios)
                {
                    planMsgBuilder.AppendLine($"• {b.ZoneName}:");
                    planMsgBuilder.AppendLine($"  - Vùng giá: {b.EntryBottom} - {b.EntryTop}");
                    planMsgBuilder.AppendLine($"  - Stop Loss: {b.StopLoss} (R:R ~ {b.RiskRewardRatio})");
                    planMsgBuilder.AppendLine($"  - Logic: {b.Logic}");
                }
                planMsgBuilder.AppendLine();
            }

            if (tradingPlan.BreakoutScenarios != null && tradingPlan.BreakoutScenarios.Any())
            {
                planMsgBuilder.AppendLine("⚡️ *CẢNH BÁO BREAKOUT*");
                foreach (var br in tradingPlan.BreakoutScenarios)
                {
                    planMsgBuilder.AppendLine($"• {br.Type}:");
                    planMsgBuilder.AppendLine($"  - Điều kiện: {br.Condition}");
                    planMsgBuilder.AppendLine($"  - Kích hoạt tại: {br.TriggerPrice}");
                    planMsgBuilder.AppendLine($"  - Stop Loss: {br.StopLoss}");
                }
            }

            if ((tradingPlan.SellScenarios == null || !tradingPlan.SellScenarios.Any()) && 
                (tradingPlan.BuyScenarios == null || !tradingPlan.BuyScenarios.Any()) && 
                (tradingPlan.BreakoutScenarios == null || !tradingPlan.BreakoutScenarios.Any()))
            {
                planMsgBuilder.AppendLine("⛔ *KHUYẾN NGHỊ: ĐỨNG NGOÀI (NO TRADE)*");
                planMsgBuilder.AppendLine("Thị trường hiện tại không có setup an toàn. Vui lòng bảo vệ vốn.");
            }

            // LƯU TRẠNG THÁI VÀ KẾ HOẠCH VÀO RAM CHO STATEFUL ENGINE (Giai đoạn 3)
            await _marketDataStore.SetLatestStateAsync(symbol, analysisResults);
            await _marketDataStore.SetLatestPlanAsync(symbol, tradingPlan);

            await _notifier.SendMessageAsync(planMsgBuilder.ToString());
            _logger.LogInformation("Hangfire Job: Hoàn thành tạo và gửi Kế hoạch giao dịch cho {Symbol}.", symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi Hangfire Job khi gọi AI Strategist.");
            await _notifier.SendMessageAsync($"⚠️ Lỗi tạo Kế hoạch cho {symbol}: {ex.Message}");
            throw; // Rethrow to let Hangfire mark the job as Failed and retry if needed
        }
    }
}