using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Api.Services;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Application.DTOs.Mobius;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.AI;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/scenarios")]
public class EaScenarioController : ControllerBase
{
    private readonly IMarketDataStore _marketDataStore;
    private readonly ITradingPlanRefreshService _planRefreshService;
    private readonly IMobiusStrategyService _mobiusService;
    private readonly INotificationService _notifier;
    private readonly ILogger<EaScenarioController> _logger;

    public EaScenarioController(
        IMarketDataStore marketDataStore,
        ITradingPlanRefreshService planRefreshService,
        IMobiusStrategyService mobiusService,
        INotificationService notifier,
        ILogger<EaScenarioController> logger)
    {
        _marketDataStore = marketDataStore;
        _planRefreshService = planRefreshService;
        _mobiusService = mobiusService;
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
            source = plan.Source,
            created_at = plan.CreatedAt
        });
    }

    /// <summary>
    /// Lấy kịch bản hiện tại cho EA (không tự động gọi AI tìm kịch bản khi trống).
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

        return Ok(new
        {
            success = true,
            has_scenarios = false,
            no_trade = plan == null || plan.HasNoTrade,
            looking_for_plan = lookingForPlan,
            refresh_queued = false,
            signal_id = plan?.SignalId ?? string.Empty,
            context = plan?.Context,
            message = lookingForPlan
                ? "AI đang tìm kịch bản."
                : "Chưa có kịch bản. Chờ kịch bản từ Mobius hoặc /scenarios/refresh."
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

        var session = await _marketDataStore.GetSymbolSessionAsync(symbol);
        var maxRiskUsd = TradingPlanResponse.NormalizeMaxRiskUsd(
            session?.MaxRiskUsd ?? TradingPlanResponse.DefaultMaxRiskUsd);
        var systemPrompt = TradingPlanPromptBuilder.GetSystemPrompt();
        var userPrompt = TradingPlanPromptBuilder.BuildUserPrompt(
            symbol, states, previousPlan, reason, maxRiskUsd, session?.Contract);

        return Ok(new
        {
            success = true,
            has_data = states != null && states.Count > 0,
            message = states != null && states.Count > 0
                ? "Copy messages sang AI (json_object). JSON trả về PUT /api/v1/scenarios."
                : stateError ?? "Chưa có data trong store. Vẫn dùng system_prompt; tự dán data vào user_prompt.",
            symbol,
            max_risk_usd = maxRiskUsd,
            contract = session?.Contract,
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
        var sessionForRisk = await _marketDataStore.GetSymbolSessionAsync(symbol);
        var cap = TradingPlanResponse.NormalizeMaxRiskUsd(
            request.MaxRiskUsd > 0
                ? request.MaxRiskUsd
                : sessionForRisk?.MaxRiskUsd ?? TradingPlanResponse.DefaultMaxRiskUsd);
        plan.EnsureDefaults(symbol, signalId, cap);
        TakeProfitPlanner.PullEarlier(plan, sessionForRisk?.Contract);
        plan.Source = TradingPlanSource.Manual;

        var errors = TradingPlanValidator.Validate(plan);
        if (errors.Count > 0)
            return BadRequest(new { success = false, errors });

        await _marketDataStore.SetLatestPlanAsync(symbol, plan);

        if (sessionForRisk != null)
        {
            sessionForRisk.PlanRefreshInProgress = false;
            await _marketDataStore.SetSymbolSessionAsync(symbol, sessionForRisk);
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

    /// <summary>
    /// API cầu nối gọi sang Mobius Strategy Scenarios API (ICT/SMC).
    /// Nhận các tham số symbol, interval, max_risk_usd từ EA hoặc client.
    /// POST /api/v1/scenarios/mobius
    /// POST /api/v1/strategy/scenarios
    /// </summary>
    [HttpPost("mobius")]
    [HttpPost("/api/v1/strategy/scenarios")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> PostMobiusScenarios(
        [FromBody] MobiusScenarioRequest? bodyRequest,
        [FromQuery] string? symbol,
        [FromQuery] string? interval,
        [FromQuery] decimal? max_risk_usd,
        [FromQuery] bool? notify)
    {
        var request = bodyRequest ?? new MobiusScenarioRequest();

        if (!string.IsNullOrWhiteSpace(symbol))
            request.Symbol = symbol.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(interval))
            request.Interval = interval.Trim();
        if (max_risk_usd.HasValue && max_risk_usd.Value > 0)
            request.MaxRiskUsd = max_risk_usd.Value;

        try
        {
            var response = await _mobiusService.FetchAndApplyScenariosAsync(request, notify, HttpContext.RequestAborted);
            return Ok(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Lỗi kết nối tới Mobius API: {Message}", ex.Message);
            return StatusCode(502, new { success = false, message = $"Lỗi kết nối tới Mobius API: {ex.Message}" });
        }
        catch (TaskCanceledException ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(ex, "Timeout khi chờ phản hồi từ Mobius API.");
            return StatusCode(504, new { success = false, message = "Timeout khi gọi tới Mobius API." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xử lý cầu nối Mobius: {Message}", ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    /// <summary>
    /// API cầu nối gọi sang Mobius Strategy Scenarios API qua GET query params.
    /// GET /api/v1/scenarios/mobius?symbol=XAUUSD&amp;interval=1h&amp;max_risk_usd=100
    /// </summary>
    [HttpGet("mobius")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(502)]
    public async Task<IActionResult> GetMobiusScenarios(
        [FromQuery] string symbol = "XAUUSD",
        [FromQuery] string interval = "1h",
        [FromQuery] decimal? max_risk_usd = null,
        [FromQuery] bool? notify = null)
    {
        var request = new MobiusScenarioRequest
        {
            Symbol = symbol,
            Interval = interval,
            MaxRiskUsd = max_risk_usd.HasValue && max_risk_usd.Value > 0 ? max_risk_usd.Value : 100m
        };

        try
        {
            var response = await _mobiusService.FetchAndApplyScenariosAsync(request, notify, HttpContext.RequestAborted);
            return Ok(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Lỗi kết nối tới Mobius API: {Message}", ex.Message);
            return StatusCode(502, new { success = false, message = $"Lỗi kết nối tới Mobius API: {ex.Message}" });
        }
        catch (TaskCanceledException ex) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogError(ex, "Timeout khi chờ phản hồi từ Mobius API.");
            return StatusCode(504, new { success = false, message = "Timeout khi gọi tới Mobius API." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xử lý cầu nối Mobius: {Message}", ex.Message);
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }
}
