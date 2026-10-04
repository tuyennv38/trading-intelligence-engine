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

namespace TradingIntelligenceEngine.AI.Providers;

public class LlmDecisionEngine : IAiDecisionEngine
{
    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;

    public LlmDecisionEngine(HttpClient httpClient, AiProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public async Task<AiDecisionResult> DecideAsync(MarketDecisionContext context, CancellationToken cancellationToken)
    {
        var prompt = BuildPrompt(context);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var requestBody = new
        {
            model = _options.Model,
            messages = new[]
            {
                new { role = "system", content = "You are an elite trading AI. Analyze the market context and suggested signals. Return a strict JSON response." },
                new { role = "user", content = prompt }
            },
            temperature = 0.2, // Low temperature for consistent deterministic responses
            stream = false, // Force non-streaming for standard JSON payload
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
            string? messageContent = null;

            if (responseJson.TrimStart().StartsWith("data:"))
            {
                // Parse SSE format (Server-Sent Events) which the proxy might force
                var sb = new StringBuilder();
                var lines = responseJson.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("data: ") && !line.Contains("[DONE]"))
                    {
                        var jsonPayload = line.Substring(6).Trim();
                        if (jsonPayload.StartsWith("{"))
                        {
                            using var chunkDoc = JsonDocument.Parse(jsonPayload);
                            if (chunkDoc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                            {
                                if (choices[0].TryGetProperty("delta", out var delta))
                                {
                                    if (delta.TryGetProperty("content", out var contentElement))
                                    {
                                        sb.Append(contentElement.GetString());
                                    }
                                }
                            }
                        }
                    }
                }
                messageContent = sb.ToString();
            }
            else
            {
                // Parse standard OpenAI format response
                using var document = JsonDocument.Parse(responseJson);
                messageContent = document.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();
            }

            if (string.IsNullOrWhiteSpace(messageContent))
        {
            sw.Stop();
            return new AiDecisionResult(
                new AiDecision(AiAction.WAIT, 0, null, null, null, null, new List<string> { "Empty AI response" }, new List<string>()),
                prompt,
                responseJson,
                sw.ElapsedMilliseconds
            );
        }

        // Clean up potential markdown formatting (```json ... ```)
        messageContent = messageContent.Trim();
        if (messageContent.StartsWith("```json"))
            messageContent = messageContent.Substring(7);
        if (messageContent.StartsWith("```"))
            messageContent = messageContent.Substring(3);
        if (messageContent.EndsWith("```"))
            messageContent = messageContent.Substring(0, messageContent.Length - 3);

        messageContent = messageContent.Trim();
        
        // If it doesn't start with '{', it's not valid JSON. Let's return raw string as reasoning
        if (!messageContent.StartsWith("{"))
        {
            sw.Stop();
            return new AiDecisionResult(
                new AiDecision(AiAction.WAIT, 0, null, null, null, null, new List<string> { "AI returned non-JSON:", messageContent }, new List<string>()),
                prompt,
                responseJson,
                sw.ElapsedMilliseconds
            );
        }

            var aiDecision = JsonSerializer.Deserialize<AiDecisionDto>(messageContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });

            sw.Stop();
            var finalDecision = new AiDecision(
                Decision: aiDecision?.Decision ?? AiAction.WAIT,
                Confidence: aiDecision?.Confidence ?? 0,
                SuggestedType: aiDecision?.OrderType,
                SuggestedEntry: aiDecision?.EntryPrice,
                SuggestedStopLoss: aiDecision?.StopLoss,
                SuggestedTakeProfit: aiDecision?.TakeProfit,
                Reasoning: aiDecision?.Reasoning ?? new List<string>(),
                Invalidations: aiDecision?.Invalidations ?? new List<string>()
            );

            return new AiDecisionResult(finalDecision, prompt, responseJson, sw.ElapsedMilliseconds);
        }
        catch (JsonException ex)
        {
            throw new Exception($"Failed to parse JSON response. Raw response: {responseJson}. Error: {ex.Message}");
        }
    }

    private string BuildPrompt(MarketDecisionContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyze the following market context and output ONLY a JSON object with this exact structure: ");
        sb.AppendLine("{ \"decision\": \"BUY|SELL|WAIT\", \"confidence\": 0.0-1.0, \"orderType\": \"MARKET|LIMIT|STOP\", \"entryPrice\": 2000.0, \"stopLoss\": 1990.0, \"takeProfit\": 2020.0, \"reasoning\": [\"reason1\"], \"invalidations\": [\"rule1\"] }");
        sb.AppendLine("If decision is WAIT, you can set orderType, entryPrice, sl, tp to null.");
        sb.AppendLine();
        
        sb.AppendLine("--- MARKET CONTEXT ---");
        sb.AppendLine($"Symbol: {context.MarketState.Symbol} | Timeframe: {context.MarketState.Timeframe}");
        sb.AppendLine($"Current Price: {context.MarketState.CurrentPrice}");
        
        if (context.MarketState.Trend != null)
            sb.AppendLine($"Trend: {context.MarketState.Trend.Direction} (Strength: {context.MarketState.Trend.Strength})");
        
        if (context.MarketState.Structure != null)
            sb.AppendLine($"Structure: {string.Join(" -> ", context.MarketState.Structure.Sequence)} (Last: {context.MarketState.Structure.LastLabel})");

        if (context.MarketState.Technical != null)
        {
            sb.AppendLine($"Technical: EMA Fast = {context.MarketState.Technical.EmaFast}, EMA Slow = {context.MarketState.Technical.EmaSlow}");
            sb.AppendLine($"Technical: RSI = {context.MarketState.Technical.Rsi}, ADX = {context.MarketState.Technical.Adx}, ATR = {context.MarketState.Technical.Atr}");
        }

        sb.AppendLine("--- SUGGESTED SIGNALS FROM STRATEGY ENGINE ---");
        if (context.SuggestedSignals.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            foreach (var sig in context.SuggestedSignals)
            {
                sb.AppendLine($"- [{sig.Direction}] Setup: {sig.Setup}, Score: {sig.Score}. Entry: {sig.EntryPrice}, SL: {sig.SuggestedStopLoss}, TP: {sig.SuggestedTakeProfit}");
                sb.AppendLine($"  Reasoning: {sig.Reasoning}");
            }
        }
        
        return sb.ToString();
    }

    // Helper DTO to match AI JSON output
    private class AiDecisionDto
    {
        public AiAction Decision { get; set; }
        public decimal Confidence { get; set; }
        public OrderType? OrderType { get; set; }
        public decimal? EntryPrice { get; set; }
        public decimal? StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        public List<string>? Reasoning { get; set; }
        public List<string>? Invalidations { get; set; }
    }
}