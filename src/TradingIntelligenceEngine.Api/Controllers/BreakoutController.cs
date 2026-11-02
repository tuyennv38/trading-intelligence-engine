using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Application.Interfaces;
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
    /// Lấy kế hoạch kịch bản Breakout mới nhất đang lưu trong Store cho Symbol.
    /// </summary>
    [HttpGet("latest/{symbol}")]
    public async Task<IActionResult> GetLatestBreakoutPlan(string symbol)
    {
        var plan = await _marketDataStore.GetLatestPlanAsync(symbol);
        if (plan == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Chưa có kế hoạch nào được lưu cho {symbol}."
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
