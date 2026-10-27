using System.Text;
using System.Text.Json;
using TradingIntelligenceEngine.Domain.MarketData;

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
- type / confirmation.condition / retest.condition / patterns / status giữ token tiếng Anh như schema.
- JSON này copy được thẳng vào PUT /api/v1/scenarios (chỉ symbol, bias, context, scenarios).
- Không gửi signal_id, source, created_at, notify (server/API gán).

SCHEMA:
{
  ""symbol"": ""XAUUSD"",
  ""bias"": ""Bullish|Bearish|Neutral"",
  ""context"": ""Mô tả tiếng Việt: đa khung, BOS/CHOCH, thanh khoản, vì sao ưu tiên hướng này..."",
  ""max_risk_usd"": 100,
  ""scenarios"": [
    {
      ""id"": ""scenario_sell_reversal_4170"",
      ""type"": ""REVERSAL_SELL"",
      ""name"": ""Vùng cản cấu trúc H4 BOS & CHOCH (4168.409 - 4171.409)"",
      ""logic"": ""Giá đang quanh vùng phá vỡ 4170.267. Chờ nến Bearish Engulfing hoặc Pinbar rút chân trên M5/M15 tại 4170.267-4171.409 rồi Bán thuận xu hướng lớn."",
      ""entry_zone"": { ""min"": 4168.409, ""max"": 4171.409 },
      ""sl"": 4177.409,
      ""max_risk_usd"": 100,
      ""tps"": [4150.666, 4110.677],
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
      ""logic"": ""Chờ nến M15 đóng cửa dứt khoát dưới 4110.677. Không đuổi. Chờ giá hồi retest M5 về 4110.677 rồi nến M5 Bearish Engulfing/Pinbar tiếp diễn mới Bán."",
      ""trigger_price"": 4110.677,
      ""sl"": 4120.677,
      ""max_risk_usd"": 100,
      ""tps"": [4090.0, 4070.0],
      ""confirmation"": {
        ""condition"": ""CLOSE_BELOW_TRIGGER"",
        ""timeframe"": ""M15"",
        ""patterns"": [""STRONG_BEARISH_MARUBOZU""]
      },
      ""retest"": {
        ""timeframe"": ""M5"",
        ""condition"": ""RETEST_THEN_CONTINUE"",
        ""patterns"": [""BEARISH_PINBAR"", ""BEARISH_ENGULFING""]
      },
      ""status"": ""PENDING""
    }
  ]
}

TYPE:
- REVERSAL_BUY / REVERSAL_SELL: bắt buộc entry_zone; không cần trigger_price; retest = null.
- BREAKOUT_BUY / BREAKOUT_SELL: bắt buộc trigger_price; không cần entry_zone; BẮT BUỘC object retest.
- confirmation.timeframe = nến xác nhận phá vỡ. Không vào lệnh ngay khi phá.
- retest.timeframe = ĐÚNG 1 BẬC NHỎ HƠN confirmation.timeframe theo thang M1 < M5 < M15 < H1 < H4 < D1 (chờ hồi về trigger rồi nến tiếp diễn trên khung đó mới vào). Ví dụ: M15 => M5; M5 => M1; H1 => M15; H4 => H1. Confirm M1 thì retest M1. Không dùng cùng khung (trừ M1).
- retest.condition luôn RETEST_THEN_CONTINUE.
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
4. Mỗi kịch bản: 1 sl + ĐÚNG 2 tps (TP1 và TP2; BỎ HẲN TP3 vì target quá xa rất khó đạt). SL/TP/zone theo ĐƠN VỊ GIÁ của đúng cặp (point/tick) — không copy khoảng 5–10 của XAUUSD sang EURUSD/GBP. SL sát invalidation, tối thiểu stops_level. TP1 RR >= 1:1, TP2 tối ưu R:R hợp lý (thường 1:1.5 đến 1:2.5).
5. Không gửi signal_id (server gán).
6. RỦI RO + LOT THEO CẶP (max_risk_usd) — bắt buộc trên plan và từng kịch bản:
   - Trần lỗ 1 lệnh do EA gửi. EA tính lot từ SL × tick_value × contract của ĐÚNG symbol. $100 trên XAUUSD ≠ cùng lot trên EURUSD. KHÔNG gửi trường lot.
   - Thuận khung lớn: dùng hết trần, SL sát invalidation.
   - Ngược khung lớn: dễ dính SL. Chỉ ra kịch bản nếu SL vẫn sát cấu trúc, TP1 RR ≥ 1:1, và hạ max_risk_usd còn khoảng 30–50% trần. Ghi rõ trong logic là đánh ngược + rủi ro đã giảm.
   - Nếu SL quá xa khiến min lot của cặp đã lỗ hơn trần thì BỎ kịch bản — ưu tiên NO TRADE.
   - Không tạo kịch bản ngược khung lớn chỉ vì M15 đẹp khi SL không vừa ngân sách của cặp đó.
7. TAKE PROFIT — ĐẶT SỚM HƠN VÙNG KHÁNG CỰ / HỖ TRỢ ÍT NHẤT 1 GIÁ (KHÔNG ĐẶT SÁT CẢN):
   - Khi chọn target TP dựa trên đỉnh/đáy cũ, kháng cự hoặc hỗ trợ: TUYỆT ĐỐI KHÔNG đặt TP nằm sát sạt ngay mép hoặc đỉnh vùng cản (vì giá rất thường đảo chiều trước khi chạm cản hoặc bị spread cản trở, dẫn đến hụt TP trong gang tấc).
   - Sau khi có 2 mức (TP1 và TP2): luôn đặt TP sớm hơn (thụt lùi về phía entry) ít nhất 1 giá (1.0 USD đối với XAUUSD/GOLD, hoặc 10 pips/100 points với Forex) trước vùng cản để đảm bảo lệnh dễ cắn TP.
   - BUY: TP phải nằm dưới mức kháng cự / đỉnh cũ ít nhất 1 giá. Ví dụ kháng cự 2750.00 thì TP tối đa 2749.00 hoặc thấp hơn.
   - SELL: TP phải nằm trên mức hỗ trợ / đáy cũ ít nhất 1 giá. Ví dụ hỗ trợ 2720.00 thì TP tối thiểu 2721.00 hoặc cao hơn.
   - Đảm bảo tỷ lệ R:R của TP1 vẫn >= 1:1 sau khi trừ hao.";
    }

    public static string BuildUserPrompt(
        string symbol,
        Dictionary<string, MarketState.MarketState>? multiTimeframeStates,
        TradingPlanResponse? previousPlan,
        string? triggerReason,
        decimal maxRiskUsd,
        SymbolContractSpec? contract)
    {
        var risk = TradingPlanResponse.NormalizeMaxRiskUsd(maxRiskUsd);
        var sb = new StringBuilder();
        sb.AppendLine($"Lập kế hoạch giao dịch EA cho cặp {symbol}.");
        sb.AppendLine("Trả JSON snake_case theo schema (symbol, bias, context, max_risk_usd, scenarios).");
        sb.AppendLine($"JSON copy thẳng vào {PutEndpoint}.");
        sb.AppendLine("context, name, logic phải là TIẾNG VIỆT chi tiết.");
        sb.AppendLine("Nếu chưa đủ điều kiện giao dịch: scenarios phải là mảng rỗng []. Không bịa kịch bản cho có.");
        sb.AppendLine($"\nRỦI RO:");
        sb.AppendLine($"- Trần lỗ chấp nhận mỗi lệnh: {risk} USD.");
        sb.AppendLine("- EA tính lot theo hợp đồng của đúng cặp; bạn KHÔNG gửi lot. Ghi max_risk_usd trên plan (= trần) và trên từng kịch bản (<= trần).");
        sb.AppendLine("- Thuận khung lớn: max_risk_usd = trần. Ngược khung lớn: chỉ setup SL sát cấu trúc + RR đủ, hạ max_risk_usd còn 30–50% trần; nếu SL quá xa với cặp này thì bỏ kịch bản / NO TRADE.");
        sb.Append(contract != null
            ? contract.ToPromptBlock(symbol, risk)
            : new SymbolContractSpec().ToPromptBlock(symbol, risk));
        sb.AppendLine("TAKE PROFIT (ĐÚNG 2 MỨC: TP1 & TP2, BỎ TP3):");
        sb.AppendLine("- Mỗi kịch bản chỉ tạo ĐÚNG 2 mức Take Profit: TP1 và TP2 (không gửi TP3 vì quá xa khó đạt).");
        sb.AppendLine("- ĐẶT TP SỚM HƠN VÙNG KHÁNG CỰ / HỖ TRỢ ÍT NHẤT 1 GIÁ (1.0 USD với XAUUSD, hoặc 10 pips với Forex) thay vì đặt sát cản, để trừ hao đảo chiều sớm và spread, giúp lệnh dễ cắn TP.");
        sb.AppendLine("- BUY: TP nằm dưới kháng cự ít nhất 1 giá. SELL: TP nằm trên hỗ trợ ít nhất 1 giá.");
        if (contract != null && (contract.SpreadPrice > 0 || contract.TickSize > 0))
            sb.AppendLine($"- Spread ≈ {contract.SpreadPrice.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)} → mức trừ hao dịch sớm tối thiểu ≈ {contract.TpEarlyOffset(0, symbol).ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}.");

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
            max_risk_usd = 100,
            notify = true,
            scenarios = new object[]
            {
                new
                {
                    type = "REVERSAL_SELL",
                    name = "Sell retest cung H4",
                    logic = "Giá retest vùng cung, nến M15 Bearish Engulfing rồi bán thuận H4.",
                    entry_zone = new { min = 2650.00m, max = 2655.00m },
                    sl = 2662.00m,
                    max_risk_usd = 100,
                    tps = new[] { 2640.00m, 2630.00m },
                    confirmation = new
                    {
                        condition = "PRICE_IN_ZONE",
                        timeframe = "M15",
                        patterns = new[] { "BEARISH_ENGULFING", "BEARISH_PINBAR" }
                    },
                    status = "PENDING"
                },
                new
                {
                    type = "BREAKOUT_SELL",
                    name = "Bán phá vỡ đáy M15, chờ retest M5",
                    logic = "Chờ M15 đóng dưới trigger. Không đuổi. Chờ M5 hồi về trigger rồi nến tiếp diễn mới bán.",
                    trigger_price = 4148.00m,
                    sl = 4158.00m,
                    max_risk_usd = 100,
                    tps = new[] { 4135.00m, 4120.00m },
                    confirmation = new
                    {
                        condition = "CLOSE_BELOW_TRIGGER",
                        timeframe = "M15",
                        patterns = new[] { "STRONG_BEARISH_MARUBOZU", "BEARISH_ENGULFING" }
                    },
                    retest = new
                    {
                        timeframe = "M5",
                        condition = "RETEST_THEN_CONTINUE",
                        patterns = new[] { "BEARISH_PINBAR", "BEARISH_ENGULFING" }
                    },
                    status = "PENDING"
                }
            }
        };
    }
}
