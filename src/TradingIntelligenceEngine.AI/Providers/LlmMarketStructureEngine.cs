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

        var prompt = BuildPrompt(symbol, timeframe, candles);
        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = GetSystemPrompt() },
                new { role = "user", content = prompt }
            },
            temperature = 0.1,
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
            throw new Exception($"AI structure API call failed: {response.StatusCode} - {error}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var messageContent = ExtractMessageContent(responseJson);
        if (string.IsNullOrWhiteSpace(messageContent))
            throw new Exception("Empty AI structure response.");

        messageContent = StripMarkdownFence(messageContent);

        var dto = JsonSerializer.Deserialize<AiStructureResponseDto>(messageContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new Exception("Failed to deserialize AI structure response.");

        return MapToMarketState(symbol, timeframeEnum, candles, dto);
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
        sb.AppendLine($"Phân tích cấu trúc thị trường cho {symbol} khung {timeframe}.");
        sb.AppendLine($"Số nến: {candles.Count}. Giá đóng cửa gần nhất: {candles[^1].Close.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine("Dữ liệu OHLC (time,open,high,low,close,tickVolume):");
        foreach (var c in candles)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5}",
                c.Time, c.Open, c.High, c.Low, c.Close, c.TickVolume));
        }

        sb.AppendLine();
        sb.AppendLine("Trả về đúng JSON theo schema đã mô tả trong system prompt. Dùng chính xác giá từ nến, không làm tròn.");
        return sb.ToString();
    }

    private static string GetSystemPrompt()
    {
        return @"Bạn là chuyên gia Market Structure (SMC/Price Action).
Nhiệm vụ: đọc chuỗi nến OHLC và nhận dạng cấu trúc thị trường.

OUTPUT DUY NHẤT LÀ JSON (không markdown, không text kèm):
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

QUY TẮC:
1. currentPrice = close của nến cuối.
2. Xác định swing HH/HL/LH/LL, BOS/CHOCH và mức thanh khoản từ OHLC thực tế.
3. Mọi mức giá phải lấy từ high/low/close của nến — không bịa số tròn.
4. sequence là chuỗi nhãn cấu trúc gần đây (mới nhất ở cuối).
5. BuySide = equal highs / kháng cự thanh khoản; SellSide = equal lows / hỗ trợ thanh khoản.";
    }

    private static string ExtractMessageContent(string responseJson)
    {
        using var document = JsonDocument.Parse(responseJson);
        return document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }

    private static string StripMarkdownFence(string content)
    {
        content = content.Trim();
        if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            content = content.Substring(7);
        else if (content.StartsWith("```", StringComparison.Ordinal))
            content = content.Substring(3);
        if (content.EndsWith("```", StringComparison.Ordinal))
            content = content.Substring(0, content.Length - 3);
        return content.Trim();
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
