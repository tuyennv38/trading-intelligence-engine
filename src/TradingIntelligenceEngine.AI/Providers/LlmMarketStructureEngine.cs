using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketContext;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketState;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.AI.Providers;

public class LlmMarketStructureEngine : IAiMarketStructureEngine
{
    private readonly HttpClient _httpClient;
    private readonly AiProviderOptions _options;

    public LlmMarketStructureEngine(HttpClient httpClient, AiProviderOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<MarketState> AnalyzeStructureAsync(
        string symbol,
        string timeframe,
        IReadOnlyList<AiCandleInput> candles,
        CancellationToken cancellationToken)
    {
        if (candles == null || candles.Count == 0)
            throw new ArgumentException("Candles cannot be empty.", nameof(candles));

        if (!Enum.TryParse<Timeframe>(timeframe, true, out var timeframeEnum))
            throw new ArgumentException($"Invalid timeframe: {timeframe}", nameof(timeframe));

        var (baseUrl, apiKey, model) = _options.Snapshot();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("AI BaseUrl is not configured. Call PUT /api/v1/ai/config first.");

        var userPrompt = BuildPrompt(symbol, timeframe, candles);
        Exception? lastError = null;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var systemPrompt = attempt == 1 ? GetSystemPrompt() : GetRepairSystemPrompt();
            var prompt = attempt == 1
                ? userPrompt
                : userPrompt + "\n\nIMPORTANT: Previous reply was invalid. Return ONLY one raw JSON object. No markdown. No explanation. First character must be '{'.";

            try
            {
                // Lần 2 bỏ response_format vì một số proxy trả plain text lỗi khi nhận field này
                var dto = await CallAndParseAsync(
                    baseUrl, apiKey, model, systemPrompt, prompt,
                    useJsonObjectFormat: attempt == 1,
                    cancellationToken);

                return MapToMarketState(symbol, timeframeEnum, candles, dto);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new Exception(
            $"AI structure parse failed after retry for {symbol}/{timeframe}: {lastError?.Message}",
            lastError);
    }

    private async Task<AiStructureResponseDto> CallAndParseAsync(
        string baseUrl,
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        bool useJsonObjectFormat,
        CancellationToken cancellationToken)
    {
        object requestBody = useJsonObjectFormat
            ? new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.0,
                stream = false,
                response_format = new { type = "json_object" }
            }
            : new
            {
                model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.0,
                stream = false
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
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"AI structure API call failed: {response.StatusCode} - {AiJsonResponseHelper.Preview(responseBody)}");

        var assistantText = AiJsonResponseHelper.ExtractAssistantText(responseBody);
        if (string.IsNullOrWhiteSpace(assistantText))
            throw new Exception($"Empty AI structure response. Raw: {AiJsonResponseHelper.Preview(responseBody)}");

        var json = AiJsonResponseHelper.ExtractJsonObject(assistantText);
        if (string.IsNullOrWhiteSpace(json) || json[0] != '{')
        {
            throw new Exception(
                $"AI structure did not return JSON object. Preview: {AiJsonResponseHelper.Preview(assistantText)}");
        }

        try
        {
            var dto = JsonSerializer.Deserialize<AiStructureResponseDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto == null)
                throw new Exception("Deserialized AI structure DTO is null.");

            return dto;
        }
        catch (JsonException ex)
        {
            throw new Exception(
                $"Invalid AI structure JSON: {ex.Message}. Preview: {AiJsonResponseHelper.Preview(json)}",
                ex);
        }
    }

    private static MarketState MapToMarketState(
        string symbol,
        Timeframe timeframe,
        IReadOnlyList<AiCandleInput> candles,
        AiStructureResponseDto dto)
    {
        var last = candles[^1];
        var currentPrice = dto.CurrentPrice > 0 ? dto.CurrentPrice : last.Close;

        TrendState? trend = null;
        if (dto.Trend != null &&
            Enum.TryParse<EventDirection>(dto.Trend.Direction, true, out var trendDir))
        {
            trend = new TrendState(trendDir, dto.Trend.Strength);
        }

        StructureState? structure = null;
        if (dto.Structure != null &&
            Enum.TryParse<StructureLabel>(dto.Structure.LastLabel, true, out var lastLabel))
        {
            structure = new StructureState(
                lastLabel,
                dto.Structure.Sequence ?? new List<string>());
        }

        var events = new List<StructureEvent>();
        if (dto.Events != null)
        {
            foreach (var e in dto.Events)
            {
                if (!Enum.TryParse<StructureEventType>(e.Type, true, out var eventType))
                    continue;
                if (!Enum.TryParse<EventDirection>(e.Direction, true, out var eventDir))
                    continue;

                var confirmedAt = e.ConfirmedAtUnix > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(e.ConfirmedAtUnix)
                    : DateTimeOffset.UtcNow;

                events.Add(new StructureEvent(
                    eventType,
                    eventDir,
                    e.Price,
                    e.CandleIndex,
                    confirmedAt,
                    string.IsNullOrWhiteSpace(e.Status) ? "Confirmed" : e.Status));
            }
        }

        LiquidityState? liquidity = null;
        if (dto.Liquidity?.ActiveLevels != null)
        {
            var levels = new List<LiquidityLevel>();
            foreach (var level in dto.Liquidity.ActiveLevels)
            {
                if (!Enum.TryParse<LiquidityType>(level.Type, true, out var liqType))
                    continue;
                levels.Add(new LiquidityLevel(
                    liqType,
                    level.Price,
                    (IReadOnlyList<int>)(level.SourceCandleIndices ?? new List<int>())));
            }

            liquidity = new LiquidityState(levels, Array.Empty<LiquiditySweep>());
        }

        RegimeState? regime = null;
        if (dto.Regime != null &&
            Enum.TryParse<MarketRegimeType>(dto.Regime.Type, true, out var regimeType))
        {
            regime = new RegimeState(regimeType, dto.Regime.Confidence);
        }

        TechnicalState? technical = null;
        if (last.EmaFast.HasValue || last.EmaSlow.HasValue || last.Rsi.HasValue || last.Atr.HasValue || last.Adx.HasValue)
        {
            technical = new TechnicalState(
                last.EmaFast ?? 0,
                last.EmaSlow ?? 0,
                last.Rsi ?? 0,
                last.Atr ?? 0,
                last.Adx ?? 0,
                0);
        }

        return new MarketState
        {
            Symbol = symbol,
            Timeframe = timeframe,
            CurrentPrice = currentPrice,
            Trend = trend,
            Structure = structure,
            Events = events,
            Liquidity = liquidity,
            Technical = technical,
            Regime = regime
        };
    }

    private static string BuildPrompt(string symbol, string timeframe, IReadOnlyList<AiCandleInput> candles)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Analyze market structure for {symbol} on {timeframe}.");
        sb.AppendLine($"Candle count: {candles.Count}. Last close: {candles[^1].Close.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine("OHLC rows (time,open,high,low,close,tickVolume):");
        foreach (var c in candles)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5}",
                c.Time, c.Open, c.High, c.Low, c.Close, c.TickVolume));
        }

        sb.AppendLine();
        sb.AppendLine("Return ONLY the JSON object defined in the system prompt. No prose. First character must be '{'.");
        return sb.ToString();
    }

    private static string GetSystemPrompt()
    {
        return @"You are a market-structure analyst (SMC / price action).
Read OHLC candles and output market structure.

CRITICAL OUTPUT RULES:
- Reply with ONE JSON object only.
- No markdown fences.
- No Vietnamese/English explanation before or after JSON.
- The first non-whitespace character MUST be '{'.
- The last non-whitespace character MUST be '}'.

Schema:
{
  ""currentPrice"": 0,
  ""trend"": { ""direction"": ""Bullish|Bearish"", ""strength"": 0.0 },
  ""structure"": { ""lastLabel"": ""HH|HL|LH|LL"", ""sequence"": [""HL"",""HH""] },
  ""events"": [
    { ""type"": ""BOS|CHOCH"", ""direction"": ""Bullish|Bearish"", ""price"": 0, ""candleIndex"": 0, ""confirmedAtUnix"": 0, ""status"": ""Confirmed"" }
  ],
  ""liquidity"": {
    ""activeLevels"": [
      { ""type"": ""BuySide|SellSide"", ""price"": 0, ""sourceCandleIndices"": [0] }
    ]
  },
  ""regime"": { ""type"": ""TrendingUp|TrendingDown|Ranging|Transition|HighVolatility|LowVolatility"", ""confidence"": 0.0 }
}

Rules:
1. currentPrice = last candle close.
2. Detect swings HH/HL/LH/LL, BOS/CHOCH and liquidity from real OHLC.
3. Use exact prices from candles — do not invent round numbers.
4. sequence = recent structure labels (newest last).
5. BuySide = equal highs / resistance liquidity; SellSide = equal lows / support liquidity.";
    }

    private static string GetRepairSystemPrompt()
    {
        return @"You must output ONLY a valid JSON object for market structure.
No text, no markdown, no code fences. Start with '{' and end with '}'.
Use this schema exactly:
{""currentPrice"":0,""trend"":{""direction"":""Bullish"",""strength"":0.5},""structure"":{""lastLabel"":""HL"",""sequence"":[""HL""]},""events"":[],""liquidity"":{""activeLevels"":[]},""regime"":{""type"":""Ranging"",""confidence"":0.5}}";
    }

    private sealed class AiStructureResponseDto
    {
        public decimal CurrentPrice { get; set; }
        public AiTrendDto? Trend { get; set; }
        public AiStructureDto? Structure { get; set; }
        public List<AiEventDto>? Events { get; set; }
        public AiLiquidityDto? Liquidity { get; set; }
        public AiRegimeDto? Regime { get; set; }
    }

    private sealed class AiTrendDto
    {
        public string Direction { get; set; } = string.Empty;
        public decimal Strength { get; set; }
    }

    private sealed class AiStructureDto
    {
        public string LastLabel { get; set; } = string.Empty;
        public List<string>? Sequence { get; set; }
    }

    private sealed class AiEventDto
    {
        public string Type { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int CandleIndex { get; set; }
        public long ConfirmedAtUnix { get; set; }
        public string Status { get; set; } = "Confirmed";
    }

    private sealed class AiLiquidityDto
    {
        public List<AiLiquidityLevelDto>? ActiveLevels { get; set; }
    }

    private sealed class AiLiquidityLevelDto
    {
        public string Type { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public List<int>? SourceCandleIndices { get; set; }
    }

    private sealed class AiRegimeDto
    {
        public string Type { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
    }
}
