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
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.AI.Providers;

public class LlmBreakoutEngine : IAiBreakoutEngine
{
    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;

    public LlmBreakoutEngine(
        HttpClient httpClient,
        AiProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<BreakoutScenarioResult> AnalyzeBreakoutWithSkillsAsync(
        string symbol,
        string timeframe,
        IReadOnlyList<AiCandleInput> candles,
        decimal maxRiskUsd = 100m,
        CancellationToken cancellationToken = default)
    {
        var (baseUrl, apiKey, model) = _options.Snapshot();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("AI BaseUrl chưa được cấu hình. Vui lòng cấu hình AiProvider trong appsettings.json hoặc gọi /api/v1/ai/config.");
        }

        var systemPrompt = BreakoutSkillPromptBuilder.GetSystemPrompt();
        var userPrompt = BreakoutSkillPromptBuilder.BuildUserPrompt(symbol, timeframe, candles, maxRiskUsd);

        string responseJson = string.Empty;
        Exception? lastError = null;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                object requestBody = attempt == 1
                    ? new
                    {
                        model = string.IsNullOrWhiteSpace(model) ? "ag/gemini-4-argon-high" : model,
                        messages = new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt }
                        },
                        temperature = 0.2,
                        stream = false,
                        response_format = new { type = "json_object" }
                    }
                    : new
                    {
                        model = string.IsNullOrWhiteSpace(model) ? "ag/gemini-4-argon-high" : model,
                        messages = new[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = userPrompt + "\n\nQUAN TRỌNG: Trả về DUY NHẤT 1 RAW JSON object, không có markdown hoặc text giải thích, ký tự đầu tiên phải là '{'." }
                        },
                        temperature = 0.2,
                        stream = false
                    };

                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");

                string url = baseUrl.TrimEnd('/') + "/chat/completions";

                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"AI API failed ({response.StatusCode}): {responseJson}");
                }

                var assistantText = AiJsonResponseHelper.ExtractAssistantText(responseJson);
                var cleanJson = AiJsonResponseHelper.ExtractJsonObject(assistantText);

                if (string.IsNullOrWhiteSpace(cleanJson) || cleanJson[0] != '{')
                {
                    throw new InvalidOperationException($"AI không trả về JSON hợp lệ: {assistantText}");
                }

                var result = JsonSerializer.Deserialize<BreakoutScenarioResult>(cleanJson, BreakoutSkillPromptBuilder.JsonOptions)
                    ?? throw new InvalidOperationException("Không thể giải mã JSON từ kết quả AI Breakout.");

                var signalId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{symbol}_AI_BO";
                result.TradingPlan.EnsureDefaults(symbol, signalId, maxRiskUsd);

                if (result.PatternRecognition != null)
                {
                    result.PatternRecognition.Symbol = symbol;
                    result.PatternRecognition.Timeframe = timeframe;
                    if (candles.Count > 0)
                    {
                        result.PatternRecognition.CurrentPrice = candles[^1].Close;
                        if (result.PatternRecognition.Atr <= 0 && candles[^1].Atr.HasValue)
                        {
                            result.PatternRecognition.Atr = candles[^1].Atr.Value;
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException("Không thể gọi AI Breakout sau 2 lần thử.");
    }

    public async Task<BreakoutScenarioResult> PlanScenarioWithAiAsync(
        PatternRecognitionResult patternResult,
        IReadOnlyList<AiCandleInput> candles,
        decimal maxRiskUsd = 100m,
        CancellationToken cancellationToken = default)
    {
        var (baseUrl, apiKey, model) = _options.Snapshot();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("AI BaseUrl chưa được cấu hình. Vui lòng cấu hình AiProvider trong appsettings.json hoặc gọi /api/v1/ai/config.");
        }

        var systemPrompt = BreakoutSkillPromptBuilder.GetSystemPrompt();
        var userPrompt = BreakoutSkillPromptBuilder.BuildScenarioPromptWithQuantPattern(patternResult, candles, maxRiskUsd);

        var requestBody = new
        {
            model = string.IsNullOrWhiteSpace(model) ? "ag/gemini-4-argon-high" : model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.2,
            stream = false,
            response_format = new { type = "json_object" }
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");

        string url = baseUrl.TrimEnd('/') + "/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"AI API failed ({response.StatusCode}): {responseJson}");
        }

        var assistantText = AiJsonResponseHelper.ExtractAssistantText(responseJson);
        var cleanJson = AiJsonResponseHelper.ExtractJsonObject(assistantText);

        if (string.IsNullOrWhiteSpace(cleanJson) || cleanJson[0] != '{')
        {
            throw new InvalidOperationException($"AI không trả về JSON hợp lệ: {assistantText}");
        }

        var result = JsonSerializer.Deserialize<BreakoutScenarioResult>(cleanJson, BreakoutSkillPromptBuilder.JsonOptions)
            ?? throw new InvalidOperationException("Không thể giải mã JSON từ kết quả AI Breakout.");

        var signalId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{patternResult.Symbol}_AI_BO";
        result.TradingPlan.EnsureDefaults(patternResult.Symbol, signalId, maxRiskUsd);

        // Giữ lại kết quả nhận diện định lượng chính xác
        result.PatternRecognition = patternResult;

        return result;
    }
}
