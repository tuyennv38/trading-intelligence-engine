using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using TradingIntelligenceEngine.Application.Interfaces;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/scenarios")]
public class EaScenarioController : ControllerBase
{
    private readonly IMarketDataStore _marketDataStore;

    public EaScenarioController(IMarketDataStore marketDataStore)
    {
        _marketDataStore = marketDataStore;
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
            return Ok(new { signal_id = string.Empty });
        }

        // Backfill ID cho plan cũ (trước khi có SignalId)
        if (string.IsNullOrEmpty(plan.SignalId))
        {
            var created = plan.CreatedAt > 0
                ? DateTimeOffset.FromUnixTimeSeconds(plan.CreatedAt).UtcDateTime
                : DateTime.UtcNow;
            plan.SignalId = $"{created:yyyyMMddHHmmss}_{symbol.ToUpperInvariant()}";
            await _marketDataStore.SetLatestPlanAsync(symbol, plan);
        }

        return Ok(new { signal_id = plan.SignalId });
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
}
