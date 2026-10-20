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
            return Ok(new { signal_id = string.Empty, has_scenarios = false, no_trade = true });
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
            has_scenarios = plan.HasScenarios,
            no_trade = plan.HasNoTrade
        });
    }

    /// <summary>
    /// EA poll mỗi 5 phút: nếu đã có kịch bản thì trả về; nếu chưa (NO TRADE / chưa xong init)
    /// thì xếp hàng AI tìm kịch bản trên nến đã update.
    /// GET /api/v1/scenarios/current?symbol=XAUUSD
    /// </summary>
    [HttpGet("current")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetCurrentScenarios([FromQuery] string symbol = "XAUUSD")
    {
        var session = await _marketDataStore.GetSymbolSessionAsync(symbol);
        var plan = await _marketDataStore.GetLatestPlanAsync(symbol);
        var lookingForPlan = session?.PlanRefreshInProgress == true;

        if (plan != null && plan.HasScenarios)
        {
            return Ok(new
            {
                success = true,
                has_scenarios = true,
                no_trade = false,
                looking_for_plan = false,
                refresh_queued = false,
                signal_id = plan.SignalId,
                plan
            });
        }

        bool refreshQueued = false;
        string message = lookingForPlan
            ? "AI đang tìm kịch bản. EA tiếp tục update nến mỗi phút, gọi lại sau 5 phút."
            : "Chưa có kịch bản. Đang để AI tìm trên dữ liệu nến đã tích lũy.";

        if (!lookingForPlan)
        {
            var (queued, refreshMessage) = await _planRefreshService.EnqueueRefreshAsync(
                symbol, "EA poll /scenarios/current — chưa có kịch bản, tìm lại");
            refreshQueued = queued;
            lookingForPlan = queued || lookingForPlan;
            if (!string.IsNullOrWhiteSpace(refreshMessage))
                message = refreshMessage;
        }

        return Ok(new
        {
            success = true,
            has_scenarios = false,
            no_trade = plan == null || plan.HasNoTrade,
            looking_for_plan = lookingForPlan,
            refresh_queued = refreshQueued,
            signal_id = plan?.SignalId ?? string.Empty,
            context = plan?.Context,
            message
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
    /// EA xin sinh kịch bản mới khi tự xác định plan hiện tại đã xong (hết lệnh / hết vùng canh).
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
