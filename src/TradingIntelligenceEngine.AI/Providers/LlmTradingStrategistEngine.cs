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
        CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(symbol, multiTimeframeStates);
        
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
Nhiệm vụ của bạn là nhận dữ liệu Cấu trúc thị trường và Volume Profile từ thuật toán Quant (đã tính toán sẵn dựa trên các khung thời gian tùy ý người dùng đưa vào), sau đó viết ra một Kế hoạch Giao dịch (Trading Plan) hoàn chỉnh.

LUẬT CỐT LÕI (TUYỆT ĐỐI TUÂN THỦ):
1. ZERO HALLUCINATION: Tuyệt đối KHÔNG tự bịa ra các mốc giá. Chỉ được dùng các mốc giá dựa trên dữ liệu hệ thống cung cấp (Ví dụ: Giá hiện tại, Các mốc Đỉnh/Đáy, Support/Resistance).
2. NO TRADE RULE: Nếu dữ liệu cho thấy thị trường đi ngang (Ranging/Choppy), biên độ hẹp hoặc không có xu hướng rõ ràng, hãy mạnh dạn trả về kịch bản KHÔNG GIAO DỊCH (Ghi rõ vào phần Bias và MarketContext, các danh sách kịch bản trả về rỗng).
3. MULTI-TIMEFRAME LOGIC: Hãy tự động nhận diện khung thời gian lớn nhất mà hệ thống gửi sang làm 'Trend Chính', và khung nhỏ nhất làm 'Khung canh Entry/Breakout'. (Ví dụ: Nếu nhận H1, M15, M5 thì H1 là Trend chính, M5 là Entry. Nếu nhận H4, H1, M15 thì H4 là Trend chính).
4. STOP LOSS: Bắt buộc kịch bản nào cũng phải có Stop Loss (Thường đặt sau đỉnh/đáy gần nhất hoặc ngoài vùng kháng cự/hỗ trợ).

OUTPUT YÊU CẦU:
Trả về DUY NHẤT một cục JSON theo đúng cấu trúc sau, không kèm bất kỳ text nào khác:
{
  ""marketContext"": ""Nhận định tổng quan ngắn gọn về thị trường"",
  ""bias"": ""Bullish / Bearish / Neutral"",
  ""buyScenarios"": [ { ""zoneName"": ""..."", ""logic"": ""..."", ""entryBottom"": 0, ""entryTop"": 0, ""stopLoss"": 0, ""riskRewardRatio"": 0 } ],
  ""sellScenarios"": [ ... ],
  ""breakoutScenarios"": [ { ""type"": ""Breakdown SELL"", ""condition"": ""..."", ""triggerPrice"": 0, ""stopLoss"": 0, ""target"": 0 } ]
}";
    }

    private string BuildPrompt(string symbol, Dictionary<string, MarketState> multiTimeframeStates)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hãy lập kế hoạch giao dịch cho cặp {symbol}. Dưới đây là dữ liệu từ hệ thống Quant:");

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

        sb.AppendLine("\nDựa vào dữ liệu trên, hãy sinh JSON TradingPlanResponse. Trích xuất Entry Price và Stop Loss từ các mức 'Sự kiện phá vỡ' và 'Mức thanh khoản' ở trên. Nếu có mức giá để làm cản, bắt buộc phải trả về kịch bản.");
        return sb.ToString();
    }
}