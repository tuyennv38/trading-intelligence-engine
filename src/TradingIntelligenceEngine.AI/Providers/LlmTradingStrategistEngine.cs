using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly JsonSerializerOptions PlanJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;

    public LlmTradingStrategistEngine(HttpClient httpClient, AiProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<TradingPlanResponse> GeneratePlanAsync(
        string symbol,
        Dictionary<string, MarketState> multiTimeframeStates,
        TradingPlanResponse? previousPlan,
        string triggerReason,
        CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(symbol, multiTimeframeStates, previousPlan, triggerReason);
        var (baseUrl, apiKey, model) = _options.Snapshot();

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("AI BaseUrl is not configured. Call PUT /api/v1/ai/config first.");

        var requestBody = new
        {
            model,
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

        string url = baseUrl;
        if (!url.EndsWith("/")) url += "/";
        url += "chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new Exception($"AI API call failed: {response.StatusCode} - {error}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            var messageContent = AiJsonResponseHelper.ExtractAssistantText(responseJson);
            if (string.IsNullOrWhiteSpace(messageContent))
                throw new Exception($"Empty AI response. Raw: {AiJsonResponseHelper.Preview(responseJson)}");

            var json = AiJsonResponseHelper.ExtractJsonObject(messageContent);
            if (string.IsNullOrWhiteSpace(json) || json[0] != '{')
                throw new Exception($"AI strategist did not return JSON. Preview: {AiJsonResponseHelper.Preview(messageContent)}");

            var plan = JsonSerializer.Deserialize<TradingPlanResponse>(json, PlanJsonOptions);
            if (plan == null)
                throw new Exception("Failed to deserialize AI Trading Plan.");

            if (string.IsNullOrWhiteSpace(plan.Symbol))
                plan.Symbol = symbol;

            plan.Scenarios ??= new List<EaTradeScenario>();
            foreach (var s in plan.Scenarios)
            {
                if (string.IsNullOrWhiteSpace(s.Status))
                    s.Status = EaScenarioStatus.Pending;
                s.TakeProfits ??= new List<decimal>();
                if (s.Confirmation != null)
                    s.Confirmation.Patterns ??= new List<string>();
            }

            return plan;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse AI JSON response: {ex.Message}. Raw: {AiJsonResponseHelper.Preview(responseJson)}");
        }
    }

    private string GetSystemPrompt()
    {
        return @"Bạn là AI Trading Strategist cấp tổ chức.
Nhiệm vụ: lập kế hoạch giao dịch để EA MT5 canh lệnh.

QUY TẮC OUTPUT:
- Chỉ trả về ĐÚNG 1 JSON object (snake_case).
- Không markdown, không text ngoài JSON. Ký tự đầu phải là '{'.
- context, name, logic BẮT BUỘC viết bằng TIẾNG VIỆT, mô tả chi tiết, rõ ràng.
- type / confirmation.condition / patterns / status giữ token tiếng Anh như schema.

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
1. Không bịa giá — dùng đúng BOS/CHOCH/liquidity.
2. Không setup rõ => scenarios = [].
3. Không tạo kịch bản ngược chiều đè cùng 1 mức (tránh hedging).
4. Mỗi kịch bản: 1 sl + 3 tps; zone rộng >= 3; SL cách mép zone 5..10; TP cách nhau >= 5; TP1 RR >= 1:1.
5. Không gửi signal_id (server gán).";
    }

    private string BuildPrompt(
        string symbol,
        Dictionary<string, MarketState> multiTimeframeStates,
        TradingPlanResponse? previousPlan,
        string triggerReason)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Lập kế hoạch giao dịch EA cho cặp {symbol}.");
        sb.AppendLine("Trả JSON snake_case theo schema (symbol, bias, context, scenarios).");
        sb.AppendLine("context, name, logic phải là TIẾNG VIỆT chi tiết.");

        if (!string.IsNullOrWhiteSpace(triggerReason))
        {
            sb.AppendLine("\nBỐI CẢNH CẬP NHẬT:");
            sb.AppendLine($"- Lý do: {triggerReason}");
            if (previousPlan != null)
            {
                sb.AppendLine("- Kế hoạch trước (tham khảo, không copy mù):");
                sb.AppendLine(JsonSerializer.Serialize(previousPlan, PlanJsonOptions));
                sb.AppendLine("Nếu kịch bản cũ cắn SL: không vào lại đúng vùng đó. Nếu đã full TP: lập sóng tiếp theo.");
            }
        }

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

        sb.AppendLine("\nTrích Entry/SL/TP từ giá sự kiện và thanh khoản ở trên. Chỉ trả JSON, bắt đầu bằng '{'.");
        return sb.ToString();
    }
}
