using System.Text;
using System.Text.Json;

namespace TradingIntelligenceEngine.Domain.AI;

/// <summary>
/// Prompt Strategist — JSON trả về khớp body PUT /api/v1/scenarios.
/// </summary>
public static class TradingPlanPromptBuilder
{
    public static readonly JsonSerializerOptions PlanJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public const string PutEndpoint = "PUT /api/v1/scenarios";

    public static string GetSystemPrompt()
    {
        return @"Bạn là AI Trading Strategist cấp tổ chức.
Nhiệm vụ: lập kế hoạch giao dịch để EA MT5 canh lệnh.

QUY TẮC OUTPUT:
- Chỉ trả về ĐÚNG 1 JSON object (snake_case).
- Không markdown, không text ngoài JSON. Ký tự đầu phải là '{'.
- context, name, logic BẮT BUỘC viết bằng TIẾNG VIỆT, mô tả chi tiết, rõ ràng.
- type / confirmation.condition / patterns / status giữ token tiếng Anh như schema.
- JSON này copy được thẳng vào PUT /api/v1/scenarios (chỉ symbol, bias, context, scenarios).
- Không gửi signal_id, source, created_at, notify (server/API gán).

SCHEMA:
{
  ""symbol"": ""XAUUSD"",
  ""bias"": ""Bullish|Bearish|Neutral"",
  ""context"": ""Mô tả tiếng Việt: đa khung, BOS/CHOCH, thanh khoản, vì sao ưu tiên hướng này..."",
  ""scenarios"": [
    {
      ""id"": ""scenario_sell_reversal_4170"",
      ""type"": ""REVERSAL_SELL"",
      ""name"": ""Vùng cản cấu trúc H4 BOS & CHOCH (4168.409 - 4171.409)"",
      ""logic"": ""Giá đang quanh vùng phá vỡ 4170.267. Chờ nến Bearish Engulfing hoặc Pinbar rút chân trên M5/M15 tại 4170.267-4171.409 rồi Bán thuận xu hướng lớn."",
      ""entry_zone"": { ""min"": 4168.409, ""max"": 4171.409 },
      ""sl"": 4177.409,
      ""tps"": [4150.666, 4110.677, 4066.341],
      ""confirmation"": {
        ""condition"": ""PRICE_IN_ZONE"",
        ""timeframe"": ""M15"",
        ""patterns"": [""BEARISH_PINBAR"", ""BEARISH_ENGULFING""]
      },
      ""status"": ""PENDING""
    },
    {
      ""id"": ""scenario_sell_breakout_4111"",
      ""type"": ""BREAKOUT_SELL"",
      ""name"": ""Breakdown thủng hỗ trợ cứng 4111"",
      ""logic"": ""Chờ nến M15 đóng cửa dứt khoát dưới 4110.677 rồi Bán theo breakout, không đuổi nếu wick giả."",
      ""trigger_price"": 4110.677,
      ""sl"": 4120.677,
      ""tps"": [4090.0, 4070.0, 4050.0],
      ""confirmation"": {
        ""condition"": ""CLOSE_BELOW_TRIGGER"",
        ""timeframe"": ""M15"",
        ""patterns"": [""STRONG_BEARISH_MARUBOZU""]
      },
      ""status"": ""PENDING""
    }
  ]
}

TYPE:
- REVERSAL_BUY / REVERSAL_SELL: bắt buộc entry_zone; không cần trigger_price.
- BREAKOUT_BUY / BREAKOUT_SELL: bắt buộc trigger_price; không cần entry_zone.
- confirmation.condition: REVERSAL_* => PRICE_IN_ZONE; BREAKOUT_BUY => CLOSE_ABOVE_TRIGGER; BREAKOUT_SELL => CLOSE_BELOW_TRIGGER.
- status mới = PENDING.
- patterns: token EA (BULLISH_PINBAR, BEARISH_ENGULFING, ...).

TRADING:
1. NO TRADE LÀ KẾT QUẢ HỢP LỆ (ưu tiên khi chưa chắc). Trả ""scenarios"": [] khi:
   - Dữ liệu mới init / chưa đủ nến / cấu trúc chưa rõ.
   - Đa khung mâu thuẫn, sideway hẹp, hoặc chưa có vùng S/R chất lượng.
   - Phải bịa giá / làm tròn / setup gượng thì ĐỪNG ra kịch bản.
   Khi NO TRADE: bias có thể Neutral, context giải thích tiếng Việt vì sao đứng ngoài và cần chờ gì (ví dụ: chờ H4 đóng, chờ retest, chờ CHOCH).
2. Chỉ ra kịch bản khi setup RÕ, giá lấy đúng BOS/CHOCH/liquidity — không bịa số.
3. Không tạo kịch bản ngược chiều đè cùng 1 mức (tránh hedging).
4. Mỗi kịch bản: 1 sl + 3 tps; zone rộng >= 3; SL cách mép zone 5..10; TP cách nhau >= 5; TP1 RR >= 1:1.
5. Không gửi signal_id (server gán).";
    }

    public static string BuildUserPrompt(
        string symbol,
        Dictionary<string, MarketState.MarketState>? multiTimeframeStates,
        TradingPlanResponse? previousPlan,
        string? triggerReason)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Lập kế hoạch giao dịch EA cho cặp {symbol}.");
        sb.AppendLine("Trả JSON snake_case theo schema (symbol, bias, context, scenarios).");
        sb.AppendLine($"JSON copy thẳng vào {PutEndpoint}.");
        sb.AppendLine("context, name, logic phải là TIẾNG VIỆT chi tiết.");
        sb.AppendLine("Nếu chưa đủ điều kiện giao dịch: scenarios phải là mảng rỗng []. Không bịa kịch bản cho có.");

        if (!string.IsNullOrWhiteSpace(triggerReason))
        {
            sb.AppendLine("\nBỐI CẢNH CẬP NHẬT:");
            sb.AppendLine($"- Lý do: {triggerReason}");
        }

        if (previousPlan != null)
        {
            sb.AppendLine("- Kế hoạch trước (tham khảo, không copy mù):");
            sb.AppendLine(JsonSerializer.Serialize(previousPlan, PlanJsonOptions));
            sb.AppendLine("Nếu kịch bản cũ cắn SL: không vào lại đúng vùng đó. Nếu đã full TP: lập sóng tiếp theo.");
        }

        if (multiTimeframeStates != null)
        {
            foreach (var kvp in multiTimeframeStates)
            {
                var tf = kvp.Key;
                var state = kvp.Value;

                sb.AppendLine($"\n--- KHUNG {tf} ---");
                sb.AppendLine($"Giá hiện tại: {state.CurrentPrice}");
                sb.AppendLine($"Xu hướng: {(state.Trend != null ? state.Trend.Direction.ToString() : "N/A")}");
                sb.AppendLine($"Cấu trúc: {(state.Structure != null ? state.Structure.LastLabel.ToString() : "N/A")}");

                if (state.Regime != null)
                    sb.AppendLine($"Regime: {state.Regime.Type}");

                if (state.Events != null && state.Events.Count > 0)
                    sb.AppendLine($"Sự kiện: {string.Join(", ", state.Events.Select(e => $"{e.Type} at {e.Price}"))}");

                if (state.Liquidity != null && state.Liquidity.ActiveLevels.Count > 0)
                {
                    sb.AppendLine("Thanh khoản:");
                    foreach (var level in state.Liquidity.ActiveLevels)
                        sb.AppendLine($"- {level.Type} (Price: {level.Price})");
                }
            }
        }
        else
        {
            sb.AppendLine("\n[Dán dữ liệu thị trường / cấu trúc đa khung vào đây]");
        }

        sb.AppendLine("\nChỉ trích Entry/SL/TP khi setup đủ chuẩn từ giá sự kiện/thanh khoản. Nếu chưa rõ: scenarios=[]. Chỉ trả JSON, bắt đầu bằng '{'.");
        return sb.ToString();
    }

    public static object JsonSample()
    {
        return new
        {
            symbol = "XAUUSD",
            bias = "Bearish",
            context = "H4 CHOCH giảm, chờ retest cung rồi bán.",
            notify = true,
            scenarios = new object[]
            {
                new
                {
                    type = "REVERSAL_SELL",
                    name = "Sell retest cung H4",
                    logic = "Giá retest vùng cung, nến M15 Bearish Engulfing rồi bán.",
                    entry_zone = new { min = 2650.00m, max = 2655.00m },
                    sl = 2662.00m,
                    tps = new[] { 2640.00m, 2630.00m, 2615.00m },
                    confirmation = new
                    {
                        condition = "PRICE_IN_ZONE",
                        timeframe = "M15",
                        patterns = new[] { "BEARISH_ENGULFING", "BEARISH_PINBAR" }
                    },
                    status = "PENDING"
                }
            }
        };
    }
}
