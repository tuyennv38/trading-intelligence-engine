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

            tradingPlan.SignalId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{symbol.ToUpperInvariant()}";
            tradingPlan.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var planMsgBuilder = new System.Text.StringBuilder();
            planMsgBuilder.AppendLine($"🚨 *KẾ HOẠCH GIAO DỊCH {symbol} (AI STRATEGIST)* 🚨\n");
            if (useAiStructure)
                planMsgBuilder.AppendLine("_Cấu trúc thị trường: AI_\n");

            planMsgBuilder.AppendLine($"*BIAS:* {tradingPlan.Bias}");
            planMsgBuilder.AppendLine($"*CONTEXT:* {tradingPlan.MarketContext}\n");

            if (tradingPlan.SellScenarios != null && tradingPlan.SellScenarios.Any())
            {
                planMsgBuilder.AppendLine("🔴 *KỊCH BẢN BÁN (SELL)*");
                foreach (var s in tradingPlan.SellScenarios)
                {
                    planMsgBuilder.AppendLine($"• {s.ZoneName}:");
                    planMsgBuilder.AppendLine($"  🔴 Entry : {s.EntryBottom:0.00} - {s.EntryTop:0.00}");

                    decimal avgEntry = (s.EntryBottom + s.EntryTop) / 2;
                    decimal slPts = Math.Abs(avgEntry - s.StopLoss);
                    planMsgBuilder.AppendLine($"  🛡  SL    : {s.StopLoss:0.00} (-{slPts:0.00} pts)");

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
