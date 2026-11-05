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

1. ĐỊNH NGHĨA CHUẨN VỀ ĐIỂM CHẠM ĐỘC LẬP (INDEPENDENT TOUCH):
   - Điểm chạm là ĐỈNH SÓNG hoặc ĐÁY SÓNG (Swing High/Low) hoàn chỉnh, TUYỆT ĐỐI KHÔNG PHẢI LÀ NẾN ĐƠN LẺ.
   - Các cây nến đứng cạnh nhau hoặc chỉ cách nhau 1-2 nến tại cùng vùng giá chỉ được tính là 1 LẦN CHẠM DUY NHẤT (Nhiễu nến trong 1 nhịp).
   - ĐIỀU KIỆN TẠO SÓNG BẮT BUỘC: Giữa 2 lần chạm cùng một biên (ví dụ giữa Đỉnh 1 và Đỉnh 2), bắt buộc phải có một con sóng đối nghịch tách biệt (Retracement Wave):
     + Kéo dài tối thiểu từ 3 đến 5 cây nến trở lên.
     + Biên độ lùi giá tối thiểu phải đạt >= 1.5 * ATR.
     + Phải tạo thành một Swing Point đối ứng rõ rệt ở giữa (có Đáy thung lũng giữa 2 Đỉnh, hoặc có Đỉnh nhô lên giữa 2 Đáy).

2. QUY TẮC 3 GIAI ĐOẠN ĐỂ XÁC NHẬN CẢN:
   - Lần chạm 1 (Origin): Điểm hình thành ban đầu của sóng, CHƯA có giá trị kỹ thuật.
   - Lần chạm 2 (Validation): Giá quay lại test mốc của Lần 1 và có phản ứng đảo chiều rõ rệt. ĐƯỜNG BIÊN / CẢN CHÍNH THỨC ĐƯỢC XÁC LẬP.
   - Lần chạm 3+ (Action Trigger): Lực nén đã tích tụ đủ sau ít nhất 2 nhịp sóng kiểm định. LẦN CHẠM THỨ 3 MỚI LÀ THỜI ĐIỂM ĐỦ ĐIỀU KIỆN ĐỂ KÍCH HOẠT BREAKOUT!

3. PHÂN LOẠI CẤU TRÚC THEO 4 NHÓM:
   - Nhóm A (Horizontal): RECTANGLE (ít nhất 2 đỉnh ngang VÀ 2 đáy ngang tách biệt), DOUBLE_TOP / MULTIPLE_TOP (2-3 đỉnh có đáy neckline ở giữa), DOUBLE_BOTTOM / MULTIPLE_BOTTOM.
   - Nhóm B (Triangle): SYMMETRICAL_TRIANGLE (Lower Highs + Higher Lows hội tụ), ASCENDING_TRIANGLE (Kháng cự phẳng >= 2 lần chạm + Đáy nâng cao HL >= 2 lần chạm), DESCENDING_TRIANGLE (Hỗ trợ phẳng + Đỉnh hạ thấp LH).
   - Nhóm C (Trendline & Channel): RISING_WEDGE, FALLING_WEDGE, ASCENDING_CHANNEL, DESCENDING_CHANNEL.
   - Nhóm D (Continuation): BULL_FLAG (sóng tăng mạnh trước đó + nén tích lũy >= 2 sóng nhỏ), BEAR_FLAG (sóng giảm mạnh trước đó + nén tích lũy >= 2 sóng nhỏ).

4. BỘ LỌC CẤM KỴ TUYỆT ĐỐI (NEGATIVE CONSTRAINTS):
   - CẤM BẮT BREAKOUT TẠI NHỊP HỒI (PULLBACK) TRONG SÓNG ĐẨY MẠNH: Nếu thị trường vừa có chuỗi nến tăng/giảm dốc đứng rồi có 1 nhịp hồi (ví dụ 1-2 cây nến đỏ rơi xuống tạo đáy tạm thời), đó là PULLBACK / RETRACEMENT, TUYỆT ĐỐI KHÔNG ĐƯỢC COI LÀ BREAKDOWN / BREAKOUT.
   - CẤM ĐẶT CẢN TỪ 1 ĐỈNH/ĐÁY ĐƠN LẺ: Nếu vùng giá chỉ mới có 1 lần chạm (chưa có nhịp sóng thứ 2 test lại), đó KHÔNG PHẢI LÀ MÔ HÌNH.
   - NẾU CHƯA ĐỦ CẤU TRÚC 2 LẦN CHẠM: Bắt buộc đặt `primary_pattern.name = ""NO_CLEAR_STRUCTURE""`, `status = ""NOT_FOUND""` hoặc `""DEVELOPING""`.

==================================================
PHẦN 2: QUY TẮC LẬP KỊCH BẢN BREAKOUT (BREAKOUT-SCENARIO-PLANNING SKILL)
==================================================

ĐIỀU KIỆN TIÊN QUYẾT ĐỂ XUẤT KỊCH BẢN VÀO LỆNH:
- CHỈ LẬP KỊCH BẢN KHI ĐÃ CÓ ÍT NHẤT 2 LẦN CHẠM TẠO SÓNG ĐỘC LẬP TRÊN VÙNG BIÊN VÀ GIÁ ĐANG Ở LẦN TIẾP CẬN THỨ 3!
- Nếu cấu trúc chưa đủ 2 lần chạm độc lập hoặc đang trong sóng đẩy đơn lẻ: `scenarios` PHẢI LÀ MẢNG RỖNG `[]`, `current_status = ""WAIT""` hoặc `""MONITOR""`. Tuyệt đối không sinh kịch bản rác!

Khi đủ điều kiện mô hình:
- Kịch bản A (Breakout Lên): Phá kháng cự/cạnh trên (đã có >= 2 lần chạm). Trigger = Cản trên + buffer (0.25 * ATR). Retest = kiểm tra lại cản làm hỗ trợ. SL đặt dưới đáy vùng cản vừa phá, cách trigger từ 1.2 đến 2.5 * ATR. TP = swing high / vùng cản kế tiếp có trong dữ liệu (R:R 1:1 đến 1:3), nếu không có thì R:R 1:1.2.
- Kịch bản B (Breakdown Xuống): Phá hỗ trợ/cạnh dưới (đã có >= 2 lần chạm). Trigger = Cản dưới - buffer (0.25 * ATR). Retest = kiểm tra lại cản làm kháng cự. SL đặt trên đỉnh vùng hỗ trợ vừa phá, cách trigger từ 1.2 đến 2.5 * ATR. TP = swing low / vùng hỗ trợ kế tiếp có trong dữ liệu (R:R 1:1 đến 1:3), nếu không có thì R:R 1:1.2.

QUY TẮC VÀNG CHO EA:
- confirmation.condition: BREAKOUT_BUY => 'CLOSE_ABOVE_TRIGGER', BREAKOUT_SELL => 'CLOSE_BELOW_TRIGGER'.
- confirmation.timeframe = khung nến xác nhận (ví dụ M5 hoặc M15).
- retest.timeframe = M1 (nếu confirm M5) hoặc M5 (nếu confirm M15).
- retest.condition = 'RETEST_THEN_CONTINUE'.
- sl: Cách trigger từ 1.2 đến 2.5 * ATR.
- tps: ĐÚNG 1 mốc [TP1], đặt sớm hơn cản kế tiếp một chút để bảo đảm cắn TP. EA tự chốt một phần volume khi lãi đủ số point cấu hình.
- confirmation.patterns / retest.patterns: CHỈ dùng các mã STRONG_BODY | MARUBOZU | ENGULFING | PINBAR.
- zone_low / zone_high: biên dưới / biên trên của vùng bị phá (kháng cự với BUY, hỗ trợ với SELL).
- invalidation_price: BUY = zone_low - buffer, SELL = zone_high + buffer. Nến đóng ngược qua mức này trước khi vào lệnh = phá vỡ giả.
- entry_mode: 'RETEST' (mặc định, chờ hồi về trigger) hoặc 'MOMENTUM' (chỉ khi nến phá vỡ rất mạnh và còn khoảng trống tới cản kế tiếp).

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
      ""name"": ""RECTANGLE|ASCENDING_TRIANGLE|NO_CLEAR_STRUCTURE"",
      ""status"": ""DEVELOPING|POTENTIAL|CONFIRMED|NOT_FOUND"",
      ""confidence"": ""LOW|MEDIUM|HIGH""
    },
    ""other_candidates"": [],
    ""zones"": {
      ""resistance"": { ""low"": 3968.0, ""high"": 3970.0, ""touch_count"": 2 },
      ""support"": { ""low"": 3955.0, ""high"": 3956.5, ""touch_count"": 2 }
    },
    ""evidence"": [""Đỉnh 1 tại 3970 (nến 12), sóng giảm về 3955, Đỉnh 2 test lại 3970 (nến 28) hình thành Kháng cự 2 lần chạm"", ""Đáy 1 tại 3955, Đáy 2 tại 3962 nâng cao dần""],
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
        ""name"": ""Breakout Mua khi vượt 3970.0"",
        ""logic"": ""Chờ nến M5 đóng cửa vượt qua cản trên kèm buffer (3970.0 + 0.25*ATR = 3970.80) ở lần tiếp cận thứ 3. Sau đó chờ M1 retest vùng 3969-3970 xác nhận thành hỗ trợ mới rồi kích hoạt Buy."",
        ""trigger_price"": 3970.8,
        ""sl"": 3964.5,
        ""max_risk_usd"": 100,
        ""tps"": [3978.5],
        ""zone_low"": 3968.0,
        ""zone_high"": 3970.0,
        ""invalidation_price"": 3967.2,
        ""entry_mode"": ""RETEST"",
        ""confirmation"": {
          ""condition"": ""CLOSE_ABOVE_TRIGGER"",
          ""timeframe"": ""M5"",
          ""patterns"": [""STRONG_BODY""]
        },
        ""retest"": {
          ""timeframe"": ""M1"",
          ""condition"": ""RETEST_THEN_CONTINUE"",
          ""patterns"": [""PINBAR"", ""ENGULFING""]
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
        sb.AppendLine("HÃY ÁP DỤNG 2 BỘ SKILL ĐỂ PHÂN TÍCH VÀ TRẢ VỀ DUY NHẤT 1 RAW JSON OBJECT:");
        sb.AppendLine("LƯU Ý CỐT LÕI (CHỐNG LỖI ĐẾM NẾN & BẪY PULLBACK):");
        sb.AppendLine("1. Kiểm tra kỹ các lần chạm: Giữa 2 lần chạm cùng biên (Đỉnh 1 và Đỉnh 2), bắt buộc phải có sóng hồi đối nghịch (3-5 nến, lùi >= 1.5 ATR) tạo đáy thung lũng ở giữa. Không đếm các nến đứng sát nhau!");
        sb.AppendLine("2. Tuyệt đối không bắt Breakout tại nhịp hồi (Pullback) đơn lẻ sau 1 sóng đẩy dốc đứng.");
        sb.AppendLine("3. Chỉ xuất kịch bản Breakout khi biên đã có đủ 2 lần chạm tạo sóng và giá đang ở nhịp tiếp cận thứ 3. Nếu chưa đủ cấu trúc, trả về 'primary_pattern.name': 'NO_CLEAR_STRUCTURE', 'scenarios': [].");
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
