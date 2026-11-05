using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketStructure;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/breakout")]
public class BreakoutController : ControllerBase
{
    private readonly IBreakoutPatternService _breakoutService;
    private readonly IMarketDataStore _marketDataStore;
    private readonly ILogger<BreakoutController> _logger;

    public BreakoutController(
        IBreakoutPatternService breakoutService,
        IMarketDataStore marketDataStore,
        ILogger<BreakoutController> logger)
    {
        _breakoutService = breakoutService;
        _marketDataStore = marketDataStore;
        _logger = logger;
    }

    /// <summary>
    /// Kích hoạt quét nến từ Mobius API, nhận diện cấu trúc mô hình và tạo kịch bản Breakout.
    /// </summary>
    [HttpPost("scan")]
    public async Task<IActionResult> ScanBreakout(
        [FromQuery] string symbol = "XAUUSD",
        [FromQuery] string interval = "5m",
        [FromQuery] int limit = 60,
        [FromQuery] decimal maxRiskUsd = 100m,
        [FromQuery] string? patternMode = null,
        [FromQuery] string? scenarioMode = null,
        [FromQuery] bool notify = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _breakoutService.AnalyzeAndExecuteBreakoutSyncAsync(
                symbol: symbol,
                interval: interval,
                limit: limit,
                maxRiskUsd: maxRiskUsd,
                patternMode: patternMode,
                scenarioMode: scenarioMode,
                notify: notify,
                cancellationToken: cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Đã quét nến và nhận diện mô hình Breakout thành công.",
                data = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi quét mô hình Breakout cho {Symbol}", symbol);
            return StatusCode(500, new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    /// <summary>
    /// Chạy thử thuật toán Quant trên một đoạn nến tự cung cấp (dry-run): KHÔNG gọi AI, KHÔNG lưu plan vào Store,
    /// KHÔNG gửi thông báo, KHÔNG ảnh hưởng EA. Toàn bộ nến được coi là đã đóng.
    /// </summary>
    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeCandles(
        [FromBody] BreakoutAnalyzeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Candles == null || request.Candles.Count < 20)
            return BadRequest(new { success = false, message = "Cần tối thiểu 20 nến (khuyến nghị 60) để nhận diện mô hình." });

        List<Candle> candles;
        try
        {
            candles = request.Candles
                .OrderBy(c => c.Time)
                .Select(c => new Candle(
                    DateTimeOffset.FromUnixTimeSeconds(c.Time > 100_000_000_000L ? c.Time / 1000 : c.Time),
                    c.Open, c.High, c.Low, c.Close, c.TickVolume))
                .ToList();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { success = false, message = $"Dữ liệu nến không hợp lệ: {ex.Message}" });
        }

        var symbol = string.IsNullOrWhiteSpace(request.Symbol) ? "XAUUSD" : request.Symbol.Trim().ToUpperInvariant();
        var timeframe = string.IsNullOrWhiteSpace(request.Timeframe) ? "M5" : request.Timeframe.Trim().ToUpperInvariant();

        var pattern = await _breakoutService.RecognizePatternAsync(symbol, timeframe, candles, cancellationToken);
        var result = await _breakoutService.PlanBreakoutScenarioAsync(pattern, candles, request.MaxRiskUsd, cancellationToken);

        return Ok(new
        {
            success = true,
            engine = "QUANT (dry-run)",
            candles_used = candles.Count,
            has_breakout_scenario = result.TradingPlan.Scenarios.Count > 0,
            data = result
        });
    }

    /// <summary>
    /// EA BreakoutTrader poll: thông tin tóm tắt kế hoạch Breakout mới nhất (slot riêng, không lẫn plan của luồng khác).
    /// GET /api/v1/breakout/latest-id?symbol=XAUUSD
    /// </summary>
    [HttpGet("latest-id")]
    public async Task<IActionResult> GetLatestBreakoutId([FromQuery] string symbol = "XAUUSD")
    {
        var plan = await _marketDataStore.GetLatestBreakoutPlanAsync(symbol);
        if (plan == null)
        {
            return Ok(new
            {
                signal_id = string.Empty,
                has_scenarios = false,
                no_trade = true
            });
        }

        return Ok(new
        {
            signal_id = plan.SignalId,
            has_scenarios = plan.HasScenarios,
            no_trade = plan.HasNoTrade,
            created_at = plan.CreatedAt,
            valid_until = plan.ValidUntil
        });
    }

    /// <summary>
    /// EA BreakoutTrader: chi tiết kế hoạch Breakout theo signal_id.
    /// GET /api/v1/breakout/plan/{signalId}?symbol=XAUUSD
    /// </summary>
    [HttpGet("plan/{signalId}")]
    public async Task<IActionResult> GetBreakoutPlan(string signalId, [FromQuery] string symbol = "XAUUSD")
    {
        var plan = await _marketDataStore.GetLatestBreakoutPlanAsync(symbol);
        if (plan == null || plan.SignalId != signalId)
        {
            return NotFound(new { error = $"Breakout plan '{signalId}' not found for symbol '{symbol}'" });
        }

        return Ok(plan);
    }

    /// <summary>
    /// Lấy kế hoạch kịch bản Breakout mới nhất đang lưu trong Store cho Symbol.
    /// </summary>
    [HttpGet("latest/{symbol}")]
    public async Task<IActionResult> GetLatestBreakoutPlan(string symbol)
    {
        var plan = await _marketDataStore.GetLatestBreakoutPlanAsync(symbol);
        if (plan == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Chưa có kế hoạch Breakout nào được lưu cho {symbol}."
            });
        }

        return Ok(new
        {
            success = true,
            symbol = symbol,
            plan = plan
        });
    }
}

public class BreakoutAnalyzeRequest
{
    public string? Symbol { get; set; }

    /// <summary>Khung thời gian của nến, ví dụ M5, M15.</summary>
    public string? Timeframe { get; set; }

    public decimal MaxRiskUsd { get; set; } = 100m;

    /// <summary>Nến OHLCV; time là Unix timestamp (giây hoặc mili-giây) của thời điểm mở nến.</summary>
    public List<RawCandleDto> Candles { get; set; } = new();
}
