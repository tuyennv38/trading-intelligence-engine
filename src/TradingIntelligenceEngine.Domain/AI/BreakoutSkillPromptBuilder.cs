using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.Domain.AI;

public static class BreakoutSkillPromptBuilder
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static string GetSystemPrompt()
    {
        return @"Bạn là Chuyên gia Cấp cao về Nhận diện Mô hình Hành động giá & Chiến lược Breakout (Senior Price Action & Breakout Strategist).
Nhiệm vụ của bạn là thực thi tuần tự 2 bộ Kỹ năng chuyên sâu:
1. [SKILL: PATTERN-RECOGNITION] Đọc dữ liệu nến OHLCV để nhận diện cấu trúc thị trường.
2. [SKILL: BREAKOUT-SCENARIO-PLANNING] Lập kịch bản Breakout hoàn chỉnh cho hệ thống EA MT5.

==================================================
PHẦN 1: QUY TẮC NHẬN DIỆN MÔ HÌNH (PATTERN-RECOGNITION SKILL)
==================================================
1. Dò tìm điểm xoay (Swing Highs & Swing Lows) và đếm số lần chạm độc lập:
   - 1 lần chạm: chỉ là điểm quan sát ban đầu.
   - 2 lần chạm độc lập: cấu trúc tiềm năng (POTENTIAL).
   - Từ 3 lần chạm trở lên: cấu trúc được xác nhận (CONFIRMED).
2. Phân loại cấu trúc theo 4 nhóm bắt buộc:
   - Nhóm A (Horizontal): RECTANGLE (biên trên/dưới ngang), DOUBLE_TOP / MULTIPLE_TOP, DOUBLE_BOTTOM / MULTIPLE_BOTTOM, HORIZONTAL_LEVELS.
   - Nhóm B (Triangle): SYMMETRICAL_TRIANGLE (Lower Highs + Higher Lows), ASCENDING_TRIANGLE (Kháng cự phẳng + Đáy nâng cao), DESCENDING_TRIANGLE (Hỗ trợ phẳng + Đỉnh hạ thấp).
   - Nhóm C (Trendline & Channel): RISING_WEDGE, FALLING_WEDGE, ASCENDING_CHANNEL, DESCENDING_CHANNEL.
   - Nhóm D (Continuation): BULL_FLAG (sóng tăng mạnh trước đó + nén tích lũy), BEAR_FLAG (sóng giảm mạnh trước đó + nén tích lũy).
3. Quy tắc chống nhận diện sai:
   - Không kết luận cản mạnh từ 1 đỉnh/đáy đơn lẻ.
   - Không gọi Range là Triangle nếu 2 cạnh không hội tụ.
   - Không gọi Flag nếu không có impulse leg mạnh trước đó.
   - Nếu chưa rõ ràng, đặt trạng thái 'DEVELOPING' hoặc 'NOT_FOUND'.

==================================================
PHẦN 2: QUY TẮC LẬP KỊCH BẢN BREAKOUT (BREAKOUT-SCENARIO-PLANNING SKILL)
==================================================
Bắt buộc xem xét:
- Kịch bản A (Breakout Lên): Phá kháng cự/cạnh trên. Trigger = Cản trên + buffer (0.2 * ATR). Retest = kiểm tra lại cản làm hỗ trợ. SL dưới nến breakout. TP1 = 1:1.2 RR, TP2 = cản/thanh khoản tiếp theo.
- Kịch bản B (Breakdown Xuống): Phá hỗ trợ/cạnh dưới. Trigger = Cản dưới - buffer (0.2 * ATR). Retest = kiểm tra lại cản làm kháng cự. SL trên nến breakdown. TP1 = 1:1.2 RR, TP2 = hỗ trợ/thanh khoản tiếp theo.
- Kịch bản C (Range Continues): Giá nằm trong biên, chờ phá vỡ.
- Kịch bản D (False Breakout): Cảnh báo nếu nến rút chân tạo râu dài khi vượt cản.

QUY TẮC VÀNG CHO EA:
- confirmation.condition: BREAKOUT_BUY => 'CLOSE_ABOVE_TRIGGER', BREAKOUT_SELL => 'CLOSE_BELOW_TRIGGER'.
- confirmation.timeframe = khung nến xác nhận (ví dụ M5 hoặc M15).
- retest.timeframe = M1 (nếu confirm M5) hoặc M5 (nếu confirm M15).
- retest.condition = 'RETEST_THEN_CONTINUE'.
- sl: Cách trigger từ 5 đến 9.5 giá (đối với XAUUSD).
- tps: ĐÚNG 2 mốc [TP1, TP2], đặt sớm hơn cản 1 giá để dễ cắn TP.

==================================================
ĐỊNH DẠNG ĐẦU RA BẮT BUỘC:
==================================================
Trả về ĐÚNG 1 JSON object, không có markdown, không có text ngoài JSON. Ký tự đầu phải là '{'.
Schema JSON:
{
  ""pattern_recognition"": {
    ""symbol"": ""XAUUSD"",
    ""timeframe"": ""M5"",
    ""market_state"": ""RANGE|TREND_UP|TREND_DOWN"",
    ""primary_pattern"": {
      ""group"": ""A|B|C|D"",
      ""name"": ""RECTANGLE|ASCENDING_TRIANGLE|..."",
      ""status"": ""DEVELOPING|POTENTIAL|CONFIRMED"",
      ""confidence"": ""LOW|MEDIUM|HIGH""
    },
    ""other_candidates"": [],
    ""zones"": {
      ""resistance"": { ""low"": 3965.5, ""high"": 3967.6, ""touch_count"": 3 },
      ""support"": { ""low"": 3954.8, ""high"": 3956.5, ""touch_count"": 3 }
    },
    ""evidence"": [""Bằng chứng 1 tiếng Việt"", ""Bằng chứng 2""],
    ""missing_evidence"": []
  },
  ""trading_plan"": {
    ""symbol"": ""XAUUSD"",
    ""bias"": ""BULLISH|BEARISH|NEUTRAL_RANGE"",
    ""context"": ""Mô tả tiếng Việt tổng quan cấu trúc và mô hình"",
    ""max_risk_usd"": 100,
    ""scenarios"": [
      {
        ""id"": ""scenario_1_breakout_buy"",
        ""type"": ""BREAKOUT_BUY"",
        ""name"": ""Breakout Mua khi vượt ..."",
        ""logic"": ""Mô tả tiếng Việt chi tiết điều kiện nến phá vỡ và retest..."",
        ""trigger_price"": 3968.5,
        ""sl"": 3960.0,
        ""max_risk_usd"": 100,
        ""tps"": [3978.0, 3986.0],
        ""confirmation"": {
          ""condition"": ""CLOSE_ABOVE_TRIGGER"",
          ""timeframe"": ""M5"",
          ""patterns"": [""Bullish Marubozu""]
        },
        ""retest"": {
          ""timeframe"": ""M1"",
          ""condition"": ""RETEST_THEN_CONTINUE"",
          ""patterns"": [""Pinbar"", ""Bullish Engulfing""]
        },
        ""status"": ""PENDING""
      },
      {
        ""id"": ""scenario_2_breakout_sell"",
        ""type"": ""BREAKOUT_SELL"",
        ""name"": ""Breakdown Bán khi thủng ..."",
        ""logic"": ""Mô tả tiếng Việt chi tiết điều kiện nến phá vỡ và retest..."",
        ""trigger_price"": 3953.5,
        ""sl"": 3961.5,
        ""max_risk_usd"": 100,
        ""tps"": [3943.0, 3935.0],
        ""confirmation"": {
          ""condition"": ""CLOSE_BELOW_TRIGGER"",
          ""timeframe"": ""M5"",
          ""patterns"": [""Bearish Marubozu""]
        },
        ""retest"": {
          ""timeframe"": ""M1"",
          ""condition"": ""RETEST_THEN_CONTINUE"",
          ""patterns"": [""Shooting Star"", ""Bearish Engulfing""]
        },
        ""status"": ""PENDING""
      }
    ]
  },
  ""current_status"": ""WAIT|MONITOR|SCENARIO_CONFIRMED""
}";
    }

    public static string BuildUserPrompt(
        string symbol,
        string timeframe,
        IReadOnlyList<AiCandleInput> candles,
        decimal maxRiskUsd)
    {
        var sb = new StringBuilder();
        var currentPrice = candles.Count > 0 ? candles[^1].Close : 0;
        var lastAtr = candles.Count > 0 && candles[^1].Atr.HasValue ? candles[^1].Atr!.Value : 2.5m;

        sb.AppendLine($"PHÂN TÍCH MÔ HÌNH VÀ LẬP KỊCH BẢN BREAKOUT CHO {symbol} ({timeframe}):");
        sb.AppendLine($"- Giá hiện tại: {currentPrice:F2}");
        sb.AppendLine($"- ATR(14) ước tính: {lastAtr:F2}");
        sb.AppendLine($"- Trần rủi ro (max_risk_usd): ${maxRiskUsd:F0}");
        sb.AppendLine($"- Số nến cung cấp: {candles.Count}");
        sb.AppendLine();
        sb.AppendLine("DANH SÁCH NẾN THEO THỨ TỰ THỜI GIAN (Time, Open, High, Low, Close, Vol, EMA20, EMA50, RSI, ATR):");

        var takeCandles = candles.TakeLast(60).ToList();
        for (int i = 0; i < takeCandles.Count; i++)
        {
            var c = takeCandles[i];
            var dt = DateTimeOffset.FromUnixTimeSeconds(c.Time).ToOffset(TimeSpan.FromHours(7));
            sb.AppendFormat(
                CultureInfo.InvariantCulture,
                "[{0}] {1:yyyy-MM-dd HH:mm} | O:{2:F2} H:{3:F2} L:{4:F2} C:{5:F2} | V:{6} | EMA_F:{7:F2} EMA_S:{8:F2} RSI:{9:F1} ATR:{10:F2}\n",
                i + 1,
                dt,
                c.Open, c.High, c.Low, c.Close,
                c.TickVolume,
                c.EmaFast ?? 0m,
                c.EmaSlow ?? 0m,
                c.Rsi ?? 50m,
                c.Atr ?? 0m
            );
        }

        sb.AppendLine();
        sb.AppendLine("HÃY ÁP DỤNG SKILL ĐỂ PHÂN TÍCH VÀ TRẢ VỀ DUY NHẤT 1 RAW JSON OBJECT:");
        return sb.ToString();
    }

    public static string BuildScenarioPromptWithQuantPattern(
        PatternRecognitionResult pattern,
        IReadOnlyList<AiCandleInput> candles,
        decimal maxRiskUsd)
    {
        var sb = new StringBuilder();
        var currentPrice = pattern.CurrentPrice > 0 ? pattern.CurrentPrice : (candles.Count > 0 ? candles[^1].Close : 0);
        var atr = pattern.Atr > 0 ? pattern.Atr : 2.5m;

        sb.AppendLine($"LẬP KỊCH BẢN BREAKOUT CHO {pattern.Symbol} ({pattern.Timeframe}) DỰA TRÊN KẾT QUẢ ĐỊNH LƯỢNG:");
        sb.AppendLine($"- Mô hình đã nhận diện: {pattern.PrimaryPattern.Name} (Nhóm {pattern.PrimaryPattern.Group})");
        sb.AppendLine($"- Trạng thái mô hình: {pattern.PrimaryPattern.Status} | Độ tin cậy: {pattern.PrimaryPattern.Confidence}");
        sb.AppendLine($"- Vùng Kháng cự: {pattern.Zones.Resistance.Low:F2} - {pattern.Zones.Resistance.High:F2} ({pattern.Zones.Resistance.TouchCount} lần chạm)");
        sb.AppendLine($"- Vùng Hỗ trợ: {pattern.Zones.Support.Low:F2} - {pattern.Zones.Support.High:F2} ({pattern.Zones.Support.TouchCount} lần chạm)");
        sb.AppendLine($"- Giá hiện tại: {currentPrice:F2} | ATR: {atr:F2} | Max Risk: ${maxRiskUsd:F0}");
        if (pattern.Evidence.Count > 0)
        {
            sb.AppendLine($"- Bằng chứng cấu trúc: {string.Join("; ", pattern.Evidence)}");
        }
        sb.AppendLine();
        sb.AppendLine("HÃY ÁP DỤNG [SKILL: BREAKOUT-SCENARIO-PLANNING] ĐỂ LẬP CÁC KỊCH BẢN BREAKOUT CHO EA MT5.");
        sb.AppendLine("TRẢ VỀ DUY NHẤT 1 RAW JSON OBJECT THEO ĐÚNG SCHEMA.");
        return sb.ToString();
    }
}
