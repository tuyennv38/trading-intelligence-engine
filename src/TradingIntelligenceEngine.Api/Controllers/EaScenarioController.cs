using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using TradingIntelligenceEngine.Api.Services;
using TradingIntelligenceEngine.Application.Interfaces;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/scenarios")]
public class EaScenarioController : ControllerBase
{
    private readonly IMarketDataStore _marketDataStore;
    private readonly ITradingPlanRefreshService _planRefreshService;

    public EaScenarioController(
        IMarketDataStore marketDataStore,
        ITradingPlanRefreshService planRefreshService)
    {
        _marketDataStore = marketDataStore;
        _planRefreshService = planRefreshService;
    }

    /// <summary>
    /// API 1: Lấy ID kịch bản mới nhất theo symbol (EA poll liên tục)
    /// GET /api/v1/scenarios/latest-id?symbol=XAUUSD
    /// </summary>
    [HttpGet("latest-id")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetLatestScenarioId([FromQuery] string symbol = "XAUUSD")
    {
        var plan = await _marketDataStore.GetLatestPlanAsync(symbol);
        if (plan == null)
        {
            return Ok(new { signal_id = string.Empty, needs_refresh = true });
        }

        if (string.IsNullOrEmpty(plan.SignalId))
        {
            var created = plan.CreatedAt > 0
                ? DateTimeOffset.FromUnixTimeSeconds(plan.CreatedAt).UtcDateTime
                : DateTime.UtcNow;
            plan.SignalId = $"{created:yyyyMMddHHmmss}_{symbol.ToUpperInvariant()}";
            await _marketDataStore.SetLatestPlanAsync(symbol, plan);
        }

        return Ok(new
        {
            signal_id = plan.SignalId,
            needs_refresh = plan.NeedsRefresh
        });
    }

    /// <summary>
    /// API 2: Lấy chi tiết kịch bản theo signal_id
    /// GET /api/v1/scenarios/{signalId}?symbol=XAUUSD
    /// </summary>
    [HttpGet("{signalId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetScenarioDetails(string signalId, [FromQuery] string symbol = "XAUUSD")
    {
        var plan = await _marketDataStore.GetLatestPlanAsync(symbol);
        if (plan == null || string.IsNullOrEmpty(plan.SignalId) || plan.SignalId != signalId)
        {
            return NotFound(new { error = $"Scenario with ID '{signalId}' not found for symbol '{symbol}'" });
        }

        return Ok(plan);
    }

    /// <summary>
    /// EA xin sinh kịch bản mới khi đã xong plan hiện tại (SL/TP/hết PENDING).
    /// Giữ nguyên mode useAiStructure lúc initialize.
    /// POST /api/v1/scenarios/refresh?symbol=XAUUSD
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> RefreshScenarios([FromQuery] string symbol = "XAUUSD")
    {
        var (queued, message) = await _planRefreshService.EnqueueRefreshAsync(
            symbol, "EA gọi /scenarios/refresh");

        if (!queued)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, refresh_queued = true });
    }
}
