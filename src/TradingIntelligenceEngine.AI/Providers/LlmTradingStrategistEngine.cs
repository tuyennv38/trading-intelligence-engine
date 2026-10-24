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
        var prompt = TradingPlanPromptBuilder.BuildUserPrompt(symbol, multiTimeframeStates, previousPlan, triggerReason);
        var (baseUrl, apiKey, model) = _options.Snapshot();

        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("AI BaseUrl is not configured. Call PUT /api/v1/ai/config first.");

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = TradingPlanPromptBuilder.GetSystemPrompt() },
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

            var plan = JsonSerializer.Deserialize<TradingPlanResponse>(json, TradingPlanPromptBuilder.PlanJsonOptions);
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
                if (s.Retest != null)
                    s.Retest.Patterns ??= new List<string>();
            }

            return plan;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse AI JSON response: {ex.Message}. Raw: {AiJsonResponseHelper.Preview(responseJson)}");
        }
    }
}
