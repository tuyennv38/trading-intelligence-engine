using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Api.Services;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.AI;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/scenarios")]
public class EaScenarioController : ControllerBase
{
    private readonly IMarketDataStore _marketDataStore;
    private readonly ITradingPlanRefreshService _planRefreshService;
    private readonly INotificationService _notifier;
    private readonly ILogger<EaScenarioController> _logger;

    public EaScenarioController(
        IMarketDataStore marketDataStore,
        ITradingPlanRefreshService planRefreshService,
        INotificationService notifier,
        ILogger<EaScenarioController> logger)
    {
        _marketDataStore = marketDataStore;
        _planRefreshService = planRefreshService;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// API 1: Lấy ID kịch bản mới nhất theo symbol (EA poll liên tục)
    /// GET /api/v1/scenarios/latest-id?symbol=XAUUSD
    /// </summary>
    [HttpGet("latest-id")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetLatestScenarioId([FromQuery] string symbol = "XAUUSD")
    {
        var session = await _marketDataStore.GetSymbolSessionAsync(symbol);
        var plan = await _marketDataStore.GetLatestPlanAsync(symbol);
        var lookingForPlan = session?.PlanRefreshInProgress == true;

        if (plan == null)
        {
            return Ok(new
            {
                signal_id = string.Empty,
                has_scenarios = false,
                no_trade = true,
                looking_for_plan = lookingForPlan
            });
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
            no_trade = plan.HasNoTrade,
            looking_for_plan = lookingForPlan,
            source = plan.Source
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
                source = plan.Source,
                plan
            });
        }

        if (plan != null && plan.IsManual)
        {
            return Ok(new
            {
                success = true,
                has_scenarios = false,
                no_trade = true,
                looking_for_plan = false,
                refresh_queued = false,
                signal_id = plan.SignalId,
                source = plan.Source,
                context = plan.Context,
                message = "Kịch bản thủ công: NO TRADE. EA không tự gọi AI cho đến khi POST /scenarios/refresh hoặc set lại."
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
    /// Lấy prompt Strategist (system + user đã nhúng data hiện có) để gọi AI bên ngoài,
    /// rồi PUT JSON vào /api/v1/scenarios.
    /// GET /api/v1/scenarios/prompt?symbol=XAUUSD
    /// </summary>
    [HttpGet("prompt")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetStrategistPrompt(
        [FromQuery] string symbol = "XAUUSD",
        [FromQuery] string? triggerReason = null)
    {
        symbol = (symbol ?? "XAUUSD").Trim().ToUpperInvariant();
        var (states, stateError) = await _planRefreshService.ResolveMarketStatesAsync(symbol);
        var previousPlan = await _marketDataStore.GetLatestPlanAsync(symbol);
        var reason = string.IsNullOrWhiteSpace(triggerReason)
            ? "Xuất prompt để set kịch bản thủ công qua PUT /api/v1/scenarios"
            : triggerReason.Trim();

        var systemPrompt = TradingPlanPromptBuilder.GetSystemPrompt();
        var userPrompt = TradingPlanPromptBuilder.BuildUserPrompt(symbol, states, previousPlan, reason);

        return Ok(new
        {
            success = true,
            has_data = states != null && states.Count > 0,
            message = states != null && states.Count > 0
                ? "Copy messages sang AI (json_object). JSON trả về PUT /api/v1/scenarios."
                : stateError ?? "Chưa có data trong store. Vẫn dùng system_prompt; tự dán data vào user_prompt.",
            symbol,
            put_endpoint = TradingPlanPromptBuilder.PutEndpoint,
            system_prompt = systemPrompt,
            user_prompt = userPrompt,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            json_sample = TradingPlanPromptBuilder.JsonSample(),
            previous_signal_id = previousPlan?.SignalId ?? string.Empty
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
    /// Chủ động set kịch bản (thay plan AI). EA poll /current sẽ nhận plan này.
    /// PUT hoặc POST /api/v1/scenarios
    /// </summary>
    [HttpPut]
    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> SetScenarios(
        [FromBody] SetTradingPlanRequest request,
        [FromQuery] bool? notify = null)
    {
        if (request == null)
            return BadRequest(new { success = false, message = "Body không được rỗng." });

        var plan = request.ToPlan();
        var symbol = string.IsNullOrWhiteSpace(plan.Symbol) ? "XAUUSD" : plan.Symbol.Trim().ToUpperInvariant();
        var signalId = string.IsNullOrWhiteSpace(request.SignalId)
            ? $"{DateTime.UtcNow:yyyyMMddHHmmss}_{symbol}"
            : request.SignalId.Trim();
        plan.EnsureDefaults(symbol, signalId);
        plan.Source = TradingPlanSource.Manual;

        var errors = TradingPlanValidator.Validate(plan);
        if (errors.Count > 0)
            return BadRequest(new { success = false, errors });

        await _marketDataStore.SetLatestPlanAsync(symbol, plan);

        var session = await _marketDataStore.GetSymbolSessionAsync(symbol);
        if (session != null)
        {
            session.PlanRefreshInProgress = false;
            await _marketDataStore.SetSymbolSessionAsync(symbol, session);
        }

        bool shouldNotify = notify ?? request.Notify;
        bool notified = false;
        if (shouldNotify)
        {
            try
            {
                var chat = TradingPlanChatFormatter.BuildPlanMessage(
                    symbol,
                    plan,
                    "THỦ CÔNG",
                    "Người dùng set NO TRADE. EA giữ nguyên cho đến khi refresh hoặc set lại.");
                await _notifier.SendMessageAsync(chat);
                notified = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Set kịch bản thủ công thành công nhưng gửi Google Chat thất bại.");
            }
        }

        return Ok(new
        {
            success = true,
            message = "Đã set kịch bản thủ công.",
            notified,
            signal_id = plan.SignalId,
            source = plan.Source,
            has_scenarios = plan.HasScenarios,
            no_trade = plan.HasNoTrade,
            plan
        });
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
