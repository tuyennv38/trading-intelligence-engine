using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.AI;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketState;
using Hangfire;
using TradingIntelligenceEngine.Api.Jobs;
using TradingIntelligenceEngine.Api.Services;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/market-data")]
public class MarketDataController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly IMarketAnalyzer _marketAnalyzer;
    private readonly IMarketDataStore _marketDataStore;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ITradingPlanRefreshService _planRefreshService;
    private readonly ILogger<MarketDataController> _logger;

    public MarketDataController(
        INotificationService notificationService,
        IMarketAnalyzer marketAnalyzer,
        IMarketDataStore marketDataStore,
        IBackgroundJobClient backgroundJobClient,
        ITradingPlanRefreshService planRefreshService,
        ILogger<MarketDataController> logger)
    {
        _notificationService = notificationService;
        _marketAnalyzer = marketAnalyzer;
        _marketDataStore = marketDataStore;
        _backgroundJobClient = backgroundJobClient;
        _planRefreshService = planRefreshService;
        _logger = logger;
    }

    [HttpGet("{symbol}")]
    public async Task<IActionResult> GetState(string symbol, [FromQuery] bool includeCandles = false)
    {
        try
        {
            var state = await _marketDataStore.GetLatestStateAsync(symbol);
            if (state == null || !state.Any())
            {
                return NotFound(new { success = false, message = "Chưa có dữ liệu cho Symbol này. Vui lòng gọi /initialize trước." });
            }

            var plan = await _marketDataStore.GetLatestPlanAsync(symbol);
            
            var latestCandles = new Dictionary<string, RawCandleDto>();
            var allCandles = new Dictionary<string, List<RawCandleDto>>();

            foreach (var tf in state.Keys)
            {
                var tfCandles = await _marketDataStore.GetCandlesAsync(symbol, tf);
                if (tfCandles.Any())
                {
                    latestCandles.Add(tf, tfCandles.Last());
                    if (includeCandles)
                    {
                        allCandles.Add(tf, tfCandles);
                    }
                }
            }

            return Ok(new 
            {
                success = true,
                symbol = symbol,
                market_structure = state,
                latest_plan = plan,
                latest_candles = latestCandles,
                candles_data = includeCandles ? allCandles : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi lấy trạng thái Market Data.");
            return StatusCode(500, new { success = false, message = "Đã xảy ra lỗi nội bộ." });
        }
    }

    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize([FromBody] MarketDataInitializeRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.Symbol) || !request.Timeframes.Any())
        {
            return BadRequest(new { success = false, message = "Dữ liệu nạp vào không hợp lệ hoặc bị trống." });
        }

        try
        {
            var analysisResults = new Dictionary<string, MarketState>();
            var candlesByTimeframe = new Dictionary<string, List<AiCandleInput>>();
            int totalCandles = 0;

            foreach (var tfData in request.Timeframes)
            {
                if (!Enum.TryParse<Timeframe>(tfData.Key, true, out var timeframeEnum))
                {
                    _logger.LogWarning("Timeframe {Timeframe} không hợp lệ, bỏ qua.", tfData.Key);
                    continue;
                }

                if (tfData.Value == null || tfData.Value.Count == 0)
                    continue;

                await _marketDataStore.InitializeCandlesAsync(request.Symbol, tfData.Key, tfData.Value);
                totalCandles += tfData.Value.Count;

                candlesByTimeframe[tfData.Key] = tfData.Value.Select(c => new AiCandleInput(
                    c.Time, c.Open, c.High, c.Low, c.Close, c.TickVolume,
                    c.EmaFast, c.EmaSlow, c.Rsi, c.Atr, c.Adx)).ToList();

                if (!request.UseAiStructure)
                {
                    var domainCandles = tfData.Value.Select(c => new Candle(
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

                    var analysisRequest = new MarketAnalysisRequest(request.Symbol, timeframeEnum, domainCandles);
                    analysisResults.Add(tfData.Key, _marketAnalyzer.Analyze(analysisRequest));
                }
            }

            if (candlesByTimeframe.Count == 0)
            {
                return BadRequest(new { success = false, message = "Không có timeframe hợp lệ nào." });
            }

            await _marketDataStore.SetSymbolSessionAsync(request.Symbol, new SymbolTradingSession
            {
                Symbol = request.Symbol,
                UseAiStructure = request.UseAiStructure,
                Timeframes = candlesByTimeframe.Keys.ToList()
            });

            if (request.UseAiStructure)
            {
                _logger.LogInformation(
                    "Initialize {Symbol} useAiStructure=true. Tổng nến: {TotalCandles}. Queue AI structure → strategist.",
                    request.Symbol, totalCandles);

                await _notificationService.SendMessageAsync(
                    $"🚀 *[AI TRADING STRATEGIST]*\n" +
                    $"📊 *Cặp:* {request.Symbol}\n" +
                    $"🧠 Mode: *AI nhận dạng cấu trúc* (useAiStructure=true)\n" +
                    $"⏳ Đang đưa vào hàng đợi: AI Structure → AI Strategist...");

                _backgroundJobClient.Enqueue<IAiNotificationJob>(job =>
                    job.GenerateAndNotifyPlanAsync(
                        request.Symbol,
                        null,
                        "Khởi tạo dữ liệu ban đầu (AI structure)",
                        true,
                        candlesByTimeframe));

                return Ok(new
                {
                    success = true,
                    useAiStructure = true,
                    message = "Đã đưa vào hàng đợi: AI nhận dạng cấu trúc rồi lập kịch bản (Hangfire).",
                    timeframes = candlesByTimeframe.Keys.ToList(),
                    totalCandles
                });
            }

            var msgBuilder = new System.Text.StringBuilder();
            msgBuilder.AppendLine("🚀 *[AI TRADING STRATEGIST]*");
            msgBuilder.AppendLine("Đã hoàn thành quét Cấu Trúc Thị Trường (Giai đoạn 1)!\n");
            msgBuilder.AppendLine($"📊 *Cặp giao dịch:* {request.Symbol}\n");

            foreach (var kvp in analysisResults)
            {
                var tf = kvp.Key;
                var state = kvp.Value;

                string trend = state.Trend?.Direction.ToString() == "Bullish" ? "🟢 Tăng (Bullish)" : "🔴 Giảm (Bearish)";
                string structureStr = state.Structure?.LastLabel.ToString() switch
                {
                    "HH" => "Tạo Đỉnh cao dần (Higher High)",
                    "HL" => "Tạo Đáy cao dần (Higher Low)",
                    "LH" => "Tạo Đỉnh thấp dần (Lower High)",
                    "LL" => "Tạo Đáy thấp dần (Lower Low)",
                    _ => "Đi ngang (Ranging/Sideway)"
                };

                msgBuilder.AppendLine($"📍 *Khung {tf}:*");
                msgBuilder.AppendLine($"- Giá hiện tại: {state.CurrentPrice}");
                msgBuilder.AppendLine($"- Xu hướng chính: {trend}");
                msgBuilder.AppendLine($"- Hành vi giá: {structureStr}");
                msgBuilder.AppendLine();
            }

            msgBuilder.AppendLine("⏳ *Đang chuyển dữ liệu cho AI (Giai đoạn 2) để lên Kịch bản chi tiết...*");

            _logger.LogInformation("Phân tích thành công {Symbol}. Tổng nến: {TotalCandles}", request.Symbol, totalCandles);
            await _notificationService.SendMessageAsync(msgBuilder.ToString());

            _backgroundJobClient.Enqueue<IAiNotificationJob>(job =>
                job.GenerateAndNotifyPlanAsync(
                    request.Symbol,
                    analysisResults,
                    "Khởi tạo dữ liệu ban đầu",
                    false,
                    null));

            return Ok(new
            {
                success = true,
                useAiStructure = false,
                message = "Phân tích cấu trúc thành công. Kế hoạch giao dịch đã được đưa vào hàng đợi Hangfire (Background Job).",
                analysis = analysisResults
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi phân tích Market Data.");
            return StatusCode(500, new { success = false, message = "Đã xảy ra lỗi nội bộ." });
        }
    }

    [HttpPost("update")]
    public async Task<IActionResult> Update([FromBody] MarketDataUpdateRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.Symbol) || !request.Timeframes.Any())
        {
            return BadRequest(new { success = false, message = "Dữ liệu nạp vào không hợp lệ." });
        }

        try
        {
            // 1) Luôn cập nhật nến
            foreach (var tfData in request.Timeframes)
            {
                await _marketDataStore.AppendCandleAsync(request.Symbol, tfData.Key, tfData.Value);
            }

            // 2) Cập nhật trạng thái scenario (ACTIVE / HIT_SL / HIT_TP) — không tự sinh plan mới
            var latestPlan = await _marketDataStore.GetLatestPlanAsync(request.Symbol);
            string statusNote = "";
            bool planUpdated = false;

            if (latestPlan != null)
            {
                var smallestTfKey = request.Timeframes.Keys
                    .OrderBy(k => Enum.TryParse<Timeframe>(k, true, out var tf) ? (int)tf : 99)
                    .FirstOrDefault();

                decimal maxHigh = 0;
                decimal minLow = 0;
                decimal currentPrice = 0;

                if (smallestTfKey != null)
                {
                    var triggerCandle = request.Timeframes[smallestTfKey];
                    maxHigh = triggerCandle.High;
                    minLow = triggerCandle.Low;
                    currentPrice = triggerCandle.Close;
                }

                var justActivatedIds = new HashSet<string>();
                latestPlan.Scenarios ??= new List<EaTradeScenario>();
                bool isAnyScenarioActive = latestPlan.HasActiveScenario;

                if (!isAnyScenarioActive && currentPrice > 0)
                {
                    foreach (var scenario in latestPlan.Scenarios)
                    {
                        if (isAnyScenarioActive)
                            break;

                        bool shouldActivate = false;
                        if (scenario.IsReversal && scenario.EntryZone != null)
                        {
                            if (scenario.IsBuy && currentPrice <= scenario.EntryZone.Max)
                                shouldActivate = true;
                            else if (scenario.IsSell && currentPrice >= scenario.EntryZone.Min)
                                shouldActivate = true;
                        }
                        else if (scenario.IsBreakout && scenario.TriggerPrice.HasValue)
                        {
                            if (scenario.IsBuy && currentPrice >= scenario.TriggerPrice.Value)
                                shouldActivate = true;
                            else if (scenario.IsSell && currentPrice <= scenario.TriggerPrice.Value)
                                shouldActivate = true;
                        }

                        if (!shouldActivate)
                            continue;

                        scenario.Status = EaScenarioStatus.Active;
                        planUpdated = true;
                        justActivatedIds.Add(scenario.Id);
                        isAnyScenarioActive = true;
                        statusNote = $"Scenario [{scenario.Id}] chuyển ACTIVE.";
                    }
                }

                if (currentPrice > 0)
                {
                    foreach (var scenario in latestPlan.Scenarios.Where(s => s.IsActive).ToList())
                    {
                        bool justActivated = justActivatedIds.Contains(scenario.Id);
                        bool hitSl = scenario.IsBuy
                            ? (justActivated ? currentPrice <= scenario.StopLoss : minLow <= scenario.StopLoss)
                            : (justActivated ? currentPrice >= scenario.StopLoss : maxHigh >= scenario.StopLoss);

                        if (hitSl)
                        {
                            scenario.Status = EaScenarioStatus.HitSl;
                            planUpdated = true;
                            statusNote = $"Giá cắn SL ({scenario.StopLoss}) của [{scenario.Id}]. EA nên refreshPlan khi sẵn sàng.";
                            await _notificationService.SendMessageAsync(
                                $"⚠️ *{request.Symbol}* cắn SL kịch bản `{scenario.Id}` — chờ EA xin plan mới (refreshPlan=true).");
                            break;
                        }

                        if (scenario.TakeProfits == null || scenario.TakeProfits.Count == 0)
                            continue;

                        decimal finalTp = scenario.IsBuy
                            ? scenario.TakeProfits.Max()
                            : scenario.TakeProfits.Min();

                        bool hitFullTp = scenario.IsBuy
                            ? (justActivated ? currentPrice >= finalTp : maxHigh >= finalTp)
                            : (justActivated ? currentPrice <= finalTp : minLow <= finalTp);

                        if (hitFullTp)
                        {
                            scenario.Status = EaScenarioStatus.HitTp;
                            planUpdated = true;
                            statusNote = $"Giá đạt full TP ({finalTp}) của [{scenario.Id}]. EA nên refreshPlan khi sẵn sàng.";
                            await _notificationService.SendMessageAsync(
                                $"🎉 *{request.Symbol}* full TP kịch bản `{scenario.Id}` — chờ EA xin plan mới (refreshPlan=true).");
                            break;
                        }
                    }
                }

                if (planUpdated)
                    await _marketDataStore.SetLatestPlanAsync(request.Symbol, latestPlan);
            }

            bool needsRefresh = latestPlan == null || latestPlan.NeedsRefresh;
            bool refreshQueued = false;
            string refreshMessage = "";

            // 3) Chỉ khi EA chủ động xin plan mới
            if (request.RefreshPlan)
            {
                var (queued, message) = await _planRefreshService.EnqueueRefreshAsync(
                    request.Symbol,
                    "EA yêu cầu kịch bản mới (refreshPlan=true)");
                refreshQueued = queued;
                refreshMessage = message;
            }

            return Ok(new
            {
                success = true,
                message = request.RefreshPlan
                    ? (refreshQueued ? refreshMessage : "Cập nhật nến OK nhưng không queue được plan mới: " + refreshMessage)
                    : "Cập nhật nến thành công. Không tạo plan mới (refreshPlan=false).",
                refresh_plan = request.RefreshPlan,
                refresh_queued = refreshQueued,
                needs_refresh = needsRefresh,
                signal_id = latestPlan?.SignalId ?? string.Empty,
                status_note = statusNote
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật Market Data.");
            return StatusCode(500, new { success = false, message = "Đã xảy ra lỗi nội bộ." });
        }
    }

    /// <summary>
    /// EA xin kịch bản mới (không cần kèm nến). Dùng mode useAiStructure đã lưu lúc initialize.
    /// </summary>
    [HttpPost("refresh-plan")]
    public async Task<IActionResult> RefreshPlan([FromQuery] string symbol = "XAUUSD")
    {
        var (queued, message) = await _planRefreshService.EnqueueRefreshAsync(
            symbol, "EA gọi /market-data/refresh-plan");

        if (!queued)
            return BadRequest(new { success = false, message });

        return Ok(new { success = true, message, refresh_queued = true });
    }
}