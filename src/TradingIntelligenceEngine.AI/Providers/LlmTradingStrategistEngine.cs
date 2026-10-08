using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketState;

namespace TradingIntelligenceEngine.AI.Providers;

public class LlmTradingStrategistEngine : ITradingStrategistEngine
{
    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;

    public LlmTradingStrategistEngine(HttpClient httpClient, AiProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<TradingPlanResponse> GeneratePlanAsync(
        string symbol,
        Dictionary<string, MarketState> multiTimeframeStates,
        TradingPlanResponse? previousPlan,
        string triggerReason,
        CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(symbol, multiTimeframeStates, previousPlan, triggerReason);
        
        var requestBody = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = GetSystemPrompt() },
                new { role = "user", content = prompt }
            },
            temperature = 0.2,
            stream = false,
            response_format = new { type = "json_object" }
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");

        string url = _options.BaseUrl;
        if (!url.EndsWith("/")) url += "/";
        url += "chat/completions";

        var response = await _httpClient.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"AI API call failed: {response.StatusCode} - {error}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        try
        {
            using var document = JsonDocument.Parse(responseJson);
            var messageContent = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(messageContent))
                throw new Exception("Empty AI response.");

            // Clean up Markdown JSON block
            messageContent = messageContent.Trim();
            if (messageContent.StartsWith("```json"))
                messageContent = messageContent.Substring(7);
            if (messageContent.StartsWith("```"))
                messageContent = messageContent.Substring(3);
            if (messageContent.EndsWith("```"))
                messageContent = messageContent.Substring(0, messageContent.Length - 3);

            var plan = JsonSerializer.Deserialize<TradingPlanResponse>(messageContent.Trim(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (plan == null) throw new Exception("Failed to deserialize AI Trading Plan.");

            // TODO: Validate AI hallucination (Compare Plan Zones against original MarketState Prices here)

            return plan;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse AI JSON response: {ex.Message}");
        }
    }

    private string GetSystemPrompt()
    {
        return @"Bạn là một AI Trading Strategist (Chuyên gia Cố vấn Giao dịch) cấp tổ chức.
Nhiệm vụ của bạn là nhận dữ liệu Cấu trúc thị trường và Volume Profile từ thuật toán Quant, sau đó viết ra một Kế hoạch Giao dịch (Trading Plan) hoàn chỉnh.

LUẬT CỐT LÕI (TUYỆT ĐỐI TUÂN THỦ):
1. ZERO HALLUCINATION: Tuyệt đối KHÔNG tự làm tròn (round) hay bịa ra các mốc giá. Bắt buộc phải dùng chính xác các con số (có phần thập phân) lấy từ các mức 'Sự kiện phá vỡ' và 'Mức thanh khoản' do hệ thống cung cấp để thiết lập Entry, Stop Loss và toàn bộ các mốc Take Profit (TP1, TP2, TP3). Không được dùng các số chẵn tự chế cho TP.
2. NO TRADE RULE: Nếu thị trường đi ngang biên độ hẹp hoặc không có xu hướng rõ ràng, trả về kịch bản KHÔNG GIAO DỊCH (Danh sách Buy/Sell/Breakout rỗng).
3. MULTI-TIMEFRAME LOGIC: Nhận diện khung lớn nhất làm 'Trend Chính', và khung nhỏ nhất làm 'Khung canh Entry'.
4. CONTRADICTION AVOIDANCE (TRÁNH HEDGING/XUNG ĐỘT): Cấm tuyệt đối tạo kịch bản Limit và Breakout ngược chiều đè lên nhau. Ví dụ: Nếu có lệnh BÁN vùng 4123-4127 (SL 4131), thì TUYỆT ĐỐI KHÔNG cài Breakout MUA ở 4127. (Vì nếu giá lên 4127.5, User sẽ vừa kích hoạt Mua vừa bị gồng lỗ Bán, cực kỳ vô lý). Hệ thống sẽ TỰ ĐỘNG gọi lại bạn nếu lệnh Limit bị cắn SL, do đó KHÔNG CẦN TẠO kịch bản Breakout dự phòng tại cùng 1 cản. Hãy chọn 1 hướng duy nhất cho mỗi vùng giá.
5. BREAKOUT LOGIC (RÕ RÀNG): Không viết chung chung. Phải nêu rõ cần nến khung nào đóng cửa dứt khoát qua mức giá cụ thể nào. Ghi rõ có cần chờ Pullback (test lại) hay vào lệnh Market ngay.
6. REJECTION LOGIC (RÕ RÀNG): Không viết chung chung 'giá bị từ chối'. Bắt buộc ghi rõ yêu cầu mô hình nến xác nhận (Ví dụ: 'Chờ xuất hiện nến Pinbar rút chân hoặc Bearish Engulfing trên khung M5 tại mốc giá X').
7. STOP LOSS & TAKE PROFIT: Bắt buộc mỗi kịch bản có 1 Stop Loss và 3 mức Take Profit (TP1, TP2, TP3) cấu trúc dạng mảng số.
8. ZONE & SL SPACING (QUY TẮC KHOẢNG CÁCH CƠ BẢN): BẮT BUỘC tuân thủ các quy tắc toán học sau:
   - Vùng Entry (EntryTop - EntryBottom) phải rộng ÍT NHẤT 3 giá (Ví dụ: 4100 - 4103). KHÔNG đưa ra vùng quá hẹp.
   - Stop Loss phải cách rìa vùng Entry ÍT NHẤT 5 giá (Ví dụ: Buy 4100-4103, SL tối đa ở 4095).
   - Khoảng cách giữa các mốc Take Profit (TP1 -> TP2 -> TP3) phải cách nhau ÍT NHẤT 5 giá (point). KHÔNG ĐƯỢC để 2 mốc TP quá sát nhau (Ví dụ sai: TP2 4142 và TP3 4143).
   - Tuyệt đối KHÔNG xếp chồng mốc giá: Stop Loss của kịch bản này KHÔNG ĐƯỢC trùng với điểm Entry của kịch bản khác. Mỗi vùng cản phải có không gian thở (Breathing room) riêng rẽ.

OUTPUT YÊU CẦU DUY NHẤT LÀ JSON (KHÔNG KÈM TEXT):
{
  ""marketContext"": ""..."",
  ""bias"": ""Bullish / Bearish / Neutral"",
  ""buyScenarios"": [ { ""zoneName"": ""..."", ""logic"": ""..."", ""entryBottom"": 0, ""entryTop"": 0, ""stopLoss"": 0, ""takeProfits"": [0, 0, 0], ""riskRewardRatio"": 0 } ],
  ""sellScenarios"": [ ... ],
  ""breakoutScenarios"": [ { ""type"": ""Breakdown SELL"", ""condition"": ""..."", ""triggerPrice"": 0, ""stopLoss"": 0, ""takeProfits"": [0, 0, 0] } ]
}";
    }

    private string BuildPrompt(string symbol, Dictionary<string, MarketState> multiTimeframeStates, TradingPlanResponse? previousPlan, string triggerReason)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hãy lập kế hoạch giao dịch cho cặp {symbol}. Dưới đây là dữ liệu từ hệ thống Quant:");

        if (!string.IsNullOrWhiteSpace(triggerReason))
        {
            sb.AppendLine($"\n⚠️ BỐI CẢNH LẤY KẾ HOẠCH LẦN NÀY:");
            sb.AppendLine($"- Lý do cập nhật: {triggerReason}");
            if (previousPlan != null)
            {
                sb.AppendLine("- Kế hoạch trước đó (THAM KHẢO để tránh lặp lại lỗi, KHÔNG BÊ NGUYÊN VÀO):");
                sb.AppendLine(JsonSerializer.Serialize(previousPlan, new JsonSerializerOptions { WriteIndented = true }));
                sb.AppendLine("\n>>> LƯU Ý QUAN TRỌNG: Nếu kịch bản cũ vừa bị cắn Stop Loss, TUYỆT ĐỐI KHÔNG xúi giục vào lại đúng vùng giá đó! Hãy tìm setup mới an toàn hơn. Nếu kịch bản cũ đã chốt lời xong toàn phần, hãy tính toán nhịp sóng tiếp theo.");
            }
        }

        foreach (var kvp in multiTimeframeStates)
        {
            var tf = kvp.Key;
            var state = kvp.Value;
            
            sb.AppendLine($"\n--- KHUNG THỜI GIAN: {tf} ---");
            sb.AppendLine($"Giá hiện tại: {state.CurrentPrice}");
            sb.AppendLine($"Xu hướng chính: {(state.Trend != null ? state.Trend.Direction.ToString() : "N/A")}");
            sb.AppendLine($"Cấu trúc gần nhất: {(state.Structure != null ? state.Structure.LastLabel.ToString() : "N/A")}");
            
            if (state.Regime != null)
            {
                sb.AppendLine($"Trạng thái thị trường (Regime): {state.Regime.Type}");
            }

            if (state.Events != null && state.Events.Count > 0)
            {
                sb.AppendLine($"Sự kiện phá vỡ (Breakout/CHOCH): {string.Join(", ", state.Events.Select(e => $"{e.Type} at {e.Price}"))}");
            }

            if (state.Liquidity != null && state.Liquidity.ActiveLevels.Count > 0)
            {
                sb.AppendLine("Các mức Thanh khoản (Hỗ trợ/Kháng cự tiềm năng):");
                foreach (var level in state.Liquidity.ActiveLevels)
                {
                    sb.AppendLine($"- {level.Type} (Price: {level.Price})");
                }
            }
        }

        sb.AppendLine("\nDựa vào dữ liệu trên, hãy sinh JSON TradingPlanResponse. Trích xuất Entry Price, Stop Loss và các mức Take Profit (TP1, TP2, TP3) từ các mức 'Sự kiện phá vỡ' và 'Mức thanh khoản' (các vùng kháng cự / hỗ trợ tiếp theo) ở trên. TUYỆT ĐỐI KHÔNG làm tròn số cho Take Profit, hãy dùng chính xác giá trị thập phân hệ thống cấp. Nếu có mức giá để làm cản, bắt buộc phải trả về kịch bản.");
        return sb.ToString();
    }
}