using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.Configuration;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.TechnicalAnalysis.Indicators;

namespace TradingIntelligenceEngine.Api.Services;

public class MobiusKlinesClient : IMobiusKlinesClient
{
    private readonly HttpClient _httpClient;
    private readonly BreakoutOptions _options;
    private readonly ILogger<MobiusKlinesClient> _logger;

    public MobiusKlinesClient(
        HttpClient httpClient,
        BreakoutOptions options,
        ILogger<MobiusKlinesClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<List<Candle>> FetchKlinesAsync(
        string symbol = "XAUUSD",
        string interval = "5m",
        int limit = 60,
        string exchange = "commodity",
        string market = "spot",
        CancellationToken cancellationToken = default)
    {
        var rawCandles = await FetchRawCandlesAsync(symbol, interval, limit, exchange, market, cancellationToken);
        if (rawCandles.Count == 0)
            return new List<Candle>();

        return rawCandles.Select(c => new Candle(
            time: DateTimeOffset.FromUnixTimeSeconds(c.Time),
            open: c.Open,
            high: c.High,
            low: c.Low,
            close: c.Close,
            volume: c.TickVolume,
            emaFast: c.EmaFast,
            emaSlow: c.EmaSlow,
            rsi: c.Rsi,
            adx: c.Adx,
            atr: c.Atr
        )).ToList();
    }

    public async Task<List<RawCandleDto>> FetchRawCandlesAsync(
        string symbol = "XAUUSD",
        string interval = "5m",
        int limit = 60,
        string exchange = "commodity",
        string market = "spot",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol)) throw new ArgumentException("Thiếu symbol khi lấy nến Mobius.", nameof(symbol));
        if (string.IsNullOrWhiteSpace(interval)) throw new ArgumentException("Thiếu interval khi lấy nến Mobius.", nameof(interval));
        if (limit <= 0) throw new ArgumentException("limit phải > 0 khi lấy nến Mobius.", nameof(limit));
        symbol = symbol.Trim().ToUpperInvariant();
        interval = interval.Trim();
        exchange = string.IsNullOrWhiteSpace(exchange) ? _options.Exchange : exchange.Trim();
        market = string.IsNullOrWhiteSpace(market) ? _options.Market : market.Trim();

        var baseUrl = _options.KlinesApiUrl.TrimEnd('/');
        var url = $"{baseUrl}?exchange={Uri.EscapeDataString(exchange)}&market={Uri.EscapeDataString(market)}&symbol={Uri.EscapeDataString(symbol)}&interval={Uri.EscapeDataString(interval)}&limit={limit}";

        _logger.LogInformation("Đang lấy {Limit} nến {Interval} của {Symbol} từ API: {Url}", limit, interval, symbol, url);

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Lỗi khi gọi Klines API ({StatusCode}): {Error}", (int)response.StatusCode, error);
            throw new HttpRequestException($"Mobius Klines API failed ({response.StatusCode}): {error}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Không tìm thấy trường 'data' dạng array trong response klines.");
            return new List<RawCandleDto>();
        }

        var rawList = new List<RawCandleDto>();
        foreach (var item in dataElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array) continue;
            var values = item.EnumerateArray().ToList();
            if (values.Count < 5) continue;

            long rawTime = values[0].GetInt64();
            long unixSeconds = rawTime > 9999999999L ? rawTime / 1000L : rawTime;

            decimal open = values[1].GetDecimal();
            decimal high = values[2].GetDecimal();
            decimal low = values[3].GetDecimal();
            decimal close = values[4].GetDecimal();
            long vol = values.Count > 5 ? (long)values[5].GetDouble() : 0L;

            rawList.Add(new RawCandleDto
            {
                Time = unixSeconds,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                TickVolume = vol
            });
        }

        if (rawList.Count == 0) return rawList;

        // Tự động tính toán các chỉ báo kỹ thuật phổ thông (EMA 20, EMA 50, RSI 14, ATR 14, ADX 14) nếu thiếu
        CalculateTechnicalIndicators(rawList);

        _logger.LogInformation("Đã lấy thành công {Count} nến cho {Symbol} ({Interval}).", rawList.Count, symbol, interval);
        return rawList;
    }

    private static void CalculateTechnicalIndicators(List<RawCandleDto> candles)
    {
        if (candles.Count == 0) return;

        var closes = candles.Select(c => c.Close).ToList();
        var domainCandles = candles.Select(c => new Candle(
            time: DateTimeOffset.FromUnixTimeSeconds(c.Time),
            open: c.Open,
            high: c.High,
            low: c.Low,
            close: c.Close,
            volume: c.TickVolume
        )).ToList();

        // Tính toán các chỉ báo theo chuẩn
        int rsiPeriod = 14;
        int atrPeriod = 14;
        int adxPeriod = 14;
        int emaFastPeriod = 20;
        int emaSlowPeriod = 50;

        for (int i = 0; i < candles.Count; i++)
        {
            var subCloses = closes.Take(i + 1).ToList();
            var subCandles = domainCandles.Take(i + 1).ToList();

            candles[i].EmaFast = IndicatorMath.CalculateEma(subCloses, emaFastPeriod);
            candles[i].EmaSlow = IndicatorMath.CalculateEma(subCloses, emaSlowPeriod);

            if (i >= rsiPeriod)
                candles[i].Rsi = IndicatorMath.CalculateRsi(subCloses, rsiPeriod);
            else
                candles[i].Rsi = 50m;

            if (i >= atrPeriod)
                candles[i].Atr = IndicatorMath.CalculateAtr(subCandles, atrPeriod);
            else
                candles[i].Atr = subCandles[i].High - subCandles[i].Low;

            if (i >= adxPeriod * 2)
                candles[i].Adx = IndicatorMath.CalculateAdx(subCandles, adxPeriod);
            else
                candles[i].Adx = 20m;
        }
    }
}
