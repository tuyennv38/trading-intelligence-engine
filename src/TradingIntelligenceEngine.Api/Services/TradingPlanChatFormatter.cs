using System.Text;
using TradingIntelligenceEngine.Domain.AI;

namespace TradingIntelligenceEngine.Api.Services;

public static class TradingPlanChatFormatter
{
    public static string BuildPlanMessage(string symbol, TradingPlanResponse plan, string originLine, string? noTradeHint = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🚨 *KẾ HOẠCH GIAO DỊCH {symbol} ({originLine})* 🚨\n");

        sb.AppendLine($"*BIAS:* {plan.Bias}");
        if (plan.MaxRiskUsd > 0)
            sb.AppendLine($"*LỖ CHẤP NHẬN:* ${plan.MaxRiskUsd:0.##} / lệnh (EA tính lot từ SL)");
        sb.AppendLine($"*CONTEXT:* {plan.Context}\n");

        var sellScenarios = (plan.Scenarios ?? new List<EaTradeScenario>()).Where(s => s.IsSell).ToList();
        var buyScenarios = (plan.Scenarios ?? new List<EaTradeScenario>()).Where(s => s.IsBuy).ToList();

        if (sellScenarios.Count > 0)
        {
            sb.AppendLine("🔴 *KỊCH BẢN BÁN (SELL)*");
            foreach (var s in sellScenarios)
                AppendScenarioChatLines(sb, s, isBuy: false);
            sb.AppendLine();
        }

        if (buyScenarios.Count > 0)
        {
            sb.AppendLine("🟢 *KỊCH BẢN MUA (BUY)*");
            foreach (var s in buyScenarios)
                AppendScenarioChatLines(sb, s, isBuy: true);
            sb.AppendLine();
        }

        if (!plan.HasScenarios)
        {
            sb.AppendLine("⛔ *KHUYẾN NGHỊ: ĐỨNG NGOÀI (NO TRADE)*");
            sb.AppendLine(noTradeHint
                ?? "Đa khung chưa đồng pha / setup chưa rõ — không ép lệnh.");
        }

        return sb.ToString();
    }

    public static void AppendScenarioChatLines(StringBuilder sb, EaTradeScenario s, bool isBuy)
    {
        string bulletIcon = isBuy ? "🟢" : "🔴";
        string displayName = string.IsNullOrWhiteSpace(s.Name) ? s.Id : s.Name;
        sb.AppendLine($"• {displayName}:");

        decimal refPrice;
        if (s.IsBreakout)
        {
            refPrice = s.TriggerPrice ?? 0;
            sb.AppendLine($"  {bulletIcon} Kích hoạt : {refPrice:0.00}");
            var confirmTf = s.Confirmation?.Timeframe ?? "M15";
            var retestTf = s.Retest?.Timeframe ?? ScenarioRetest.DefaultTimeframe(confirmTf);
            sb.AppendLine($"  ⏱  Confirm : nến {confirmTf} đóng phá trigger");
            sb.AppendLine($"  🔁 Retest  : khung {retestTf} hồi về trigger rồi xác nhận tiếp diễn mới vào");
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
        if (s.MaxRiskUsd > 0)
            sb.AppendLine($"  💰 Rủi ro : ${s.MaxRiskUsd:0.##} (lot theo SL, không vượt mức này)");

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

    public static string BuildFallbackLogic(EaTradeScenario s)
    {
        var patterns = s.Confirmation?.Patterns != null && s.Confirmation.Patterns.Count > 0
            ? string.Join(" hoặc ", s.Confirmation.Patterns)
            : "mô hình nến xác nhận";
        var tf = s.Confirmation?.Timeframe ?? "M15";

        if (s.IsBreakout)
        {
            string side = s.IsBuy ? "Mua" : "Bán";
            string cond = s.IsBuy ? "đóng cửa trên" : "đóng cửa dưới";
            var retestTf = s.Retest?.Timeframe ?? ScenarioRetest.DefaultTimeframe(tf);
            var retestPatterns = s.Retest?.Patterns != null && s.Retest.Patterns.Count > 0
                ? string.Join(" hoặc ", s.Retest.Patterns)
                : "nến tiếp diễn";
            return $"Chờ nến {tf} {cond} {s.TriggerPrice:0.00} ({patterns}). Không đuổi. Chờ retest {retestTf} về mức phá vỡ, xác nhận {retestPatterns} rồi {side}.";
        }

        if (s.EntryZone != null)
        {
            string side = s.IsBuy ? "Mua" : "Bán";
            return $"Chờ giá vào vùng {s.EntryZone.Min:0.00}-{s.EntryZone.Max:0.00} và xuất hiện {patterns} trên {tf} rồi {side}.";
        }

        return s.Name;
    }
}
