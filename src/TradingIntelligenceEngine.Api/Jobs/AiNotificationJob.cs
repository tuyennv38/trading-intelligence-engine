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
    Task GenerateAndNotifyPlanAsync(string symbol, Dictionary<string, MarketState> analysisResults, string triggerReason = "");
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

    public async Task GenerateAndNotifyPlanAsync(string symbol, Dictionary<string, MarketState> analysisResults, string triggerReason = "")
    {
        try
        {
            _logger.LogInformation("Hangfire Job: Bắt đầu gọi AI Strategist cho {Symbol}...", symbol);
            
            // Lấy kế hoạch cũ để làm Context cho AI (tránh lặp lại lỗi)
            var previousPlan = await _marketDataStore.GetLatestPlanAsync(symbol);

            // Hangfire doesn't pass a live CancellationToken out of the box unless injected, using CancellationToken.None
            var tradingPlan = await _aiStrategist.GeneratePlanAsync(symbol, analysisResults, previousPlan, triggerReason, CancellationToken.None);
            
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
                    planMsgBuilder.AppendLine($"  🔴 Entry : {s.EntryBottom:0.00} - {s.EntryTop:0.00}");
                    
                    // Tính toán khoảng cách (pts) và R:R cho Stop Loss
                        decimal avgEntry = (s.EntryBottom + s.EntryTop) / 2;
                        decimal slPts = Math.Abs(avgEntry - s.StopLoss);
                        planMsgBuilder.AppendLine($"  🛡  SL    : {s.StopLoss:0.00} (-{slPts:0.00} pts)");

                        // Xử lý hiển thị từng mức Take Profit
                        if (s.TakeProfits != null && s.TakeProfits.Any())
                        {
                            for (int i = 0; i < s.TakeProfits.Count; i++)
                            {
                                decimal tpValue = s.TakeProfits[i];
                                decimal tpPts = Math.Abs(tpValue - avgEntry);
                                decimal rr = slPts > 0 ? tpPts / slPts : 0;
                                planMsgBuilder.AppendLine($"  🎯 TP{i + 1}   : {tpValue:0.00} (+{tpPts:0.00} pts) R:R 1:{rr:0.0}");
                            }
                        }
                        else
                        {
                            planMsgBuilder.AppendLine($"  🎯 TP    : N/A");
                        }
                        
                        planMsgBuilder.AppendLine($"  💡 Logic : {s.Logic}");
                    }
                    planMsgBuilder.AppendLine();
                }

            if (tradingPlan.BuyScenarios != null && tradingPlan.BuyScenarios.Any())
            {
                planMsgBuilder.AppendLine("🟢 *KỊCH BẢN MUA (BUY)*");
                foreach (var b in tradingPlan.BuyScenarios)
                {
                    planMsgBuilder.AppendLine($"• {b.ZoneName}:");
                    planMsgBuilder.AppendLine($"  🟢 Entry : {b.EntryBottom:0.00} - {b.EntryTop:0.00}");
                    
                    decimal avgEntry = (b.EntryBottom + b.EntryTop) / 2;
                        decimal slPts = Math.Abs(avgEntry - b.StopLoss);
                        planMsgBuilder.AppendLine($"  🛡  SL    : {b.StopLoss:0.00} (-{slPts:0.00} pts)");

                        if (b.TakeProfits != null && b.TakeProfits.Any())
                        {
                            for (int i = 0; i < b.TakeProfits.Count; i++)
                            {
                                decimal tpValue = b.TakeProfits[i];
                                decimal tpPts = Math.Abs(tpValue - avgEntry);
                                decimal rr = slPts > 0 ? tpPts / slPts : 0;
                                planMsgBuilder.AppendLine($"  🎯 TP{i + 1}   : {tpValue:0.00} (+{tpPts:0.00} pts) R:R 1:{rr:0.0}");
                            }
                        }
                        else
                        {
                            planMsgBuilder.AppendLine($"  🎯 TP    : N/A");
                        }
                        
                        planMsgBuilder.AppendLine($"  💡 Logic : {b.Logic}");
                    }
                    planMsgBuilder.AppendLine();
                }

            if (tradingPlan.BreakoutScenarios != null && tradingPlan.BreakoutScenarios.Any())
            {
                planMsgBuilder.AppendLine("⚡️ *CẢNH BÁO BREAKOUT*");
                foreach (var br in tradingPlan.BreakoutScenarios)
                {
                    string brIcon = br.Type.Contains("BUY", StringComparison.OrdinalIgnoreCase) ? "🟢" : "🔴";
                    planMsgBuilder.AppendLine($"• {br.Type}:");
                    planMsgBuilder.AppendLine($"  {brIcon} Kích hoạt : {br.TriggerPrice:0.00}");
                    
                    decimal slPts = Math.Abs(br.TriggerPrice - br.StopLoss);
                        planMsgBuilder.AppendLine($"  🛡  SL        : {br.StopLoss:0.00} (-{slPts:0.00} pts)");

                        if (br.TakeProfits != null && br.TakeProfits.Any())
                        {
                            for (int i = 0; i < br.TakeProfits.Count; i++)
                            {
                                decimal tpValue = br.TakeProfits[i];
                                decimal tpPts = Math.Abs(tpValue - br.TriggerPrice);
                                decimal rr = slPts > 0 ? tpPts / slPts : 0;
                                planMsgBuilder.AppendLine($"  🎯 TP{i + 1}       : {tpValue:0.00} (+{tpPts:0.00} pts) R:R 1:{rr:0.0}");
                            }
                        }
                        
                        planMsgBuilder.AppendLine($"  💡 Điều kiện : {br.Condition}");
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