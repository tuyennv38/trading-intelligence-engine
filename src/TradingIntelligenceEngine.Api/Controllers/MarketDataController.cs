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

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/market-data")]
public class MarketDataController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly IMarketAnalyzer _marketAnalyzer;
    private readonly IMarketDataStore _marketDataStore;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly ILogger<MarketDataController> _logger;

    public MarketDataController(
        INotificationService notificationService,
        IMarketAnalyzer marketAnalyzer,
        IMarketDataStore marketDataStore,
        IBackgroundJobClient backgroundJobClient,
        ILogger<MarketDataController> logger)
    {
        _notificationService = notificationService;
        _marketAnalyzer = marketAnalyzer;
        _marketDataStore = marketDataStore;
        _backgroundJobClient = backgroundJobClient;
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
            var analysisResults = new Dictionary<string, MarketState>();
            bool hasStructuralChange = false;

            foreach (var tfData in request.Timeframes)
            {
                var timeframeStr = tfData.Key;
                var newCandle = tfData.Value;

                // 1. Nạp nến mới vào mảng trong RAM
                await _marketDataStore.AppendCandleAsync(request.Symbol, timeframeStr, newCandle);

                // 2. Lấy lại toàn bộ mảng nến hiện hành để tính toán lại Cấu trúc
                var cachedCandles = await _marketDataStore.GetCandlesAsync(request.Symbol, timeframeStr);
                
                if (Enum.TryParse<Timeframe>(timeframeStr, true, out var timeframeEnum))
                {
                    var domainCandles = cachedCandles.Select(c => new Candle(
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
                    var newState = _marketAnalyzer.Analyze(analysisRequest);
                    analysisResults.Add(timeframeStr, newState);
                }
            }

            // 3. GIAI ĐOẠN 3 (STATEFUL CHECK): So sánh trạng thái mới với trạng thái cũ
            var oldState = await _marketDataStore.GetLatestStateAsync(request.Symbol);
            var latestPlan = await _marketDataStore.GetLatestPlanAsync(request.Symbol);

            if (oldState != null)
            {
                // Kiểm tra xem có cấu trúc mới không (Ví dụ: Số lượng Events phá vỡ thay đổi)
                // Một cách đơn giản là check xem LastLabel của khung H1 có thay đổi không
                foreach (var tf in analysisResults.Keys)
                {
                    if (oldState.TryGetValue(tf, out var oldTfState))
                    {
                        var newTfState = analysisResults[tf];
                        
                        // Detect Structure change (e.g., from HH to HL, or a new BoS event)
                        if (newTfState.Structure?.LastLabel != oldTfState.Structure?.LastLabel)
                        {
                            hasStructuralChange = true;
                            _logger.LogInformation("Phát hiện thay đổi cấu trúc ở khung {TF}: {Old} -> {New}", tf, oldTfState.Structure?.LastLabel, newTfState.Structure?.LastLabel);
                        }
                    }
                }
            }

            // GỌI LẠI AI NẾU CÓ TRIGGER
            bool shouldTriggerAi = false;
            string triggerReason = "";

            if (latestPlan != null)
            {
                // TÍNH TOÁN HIGH/LOW TỪ KHUNG THỜI GIAN NHỎ NHẤT (Để bắt các râu nến quét SL/TP)
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
                else
                {
                    currentPrice = analysisResults.Values.FirstOrDefault()?.CurrentPrice ?? 0;
                    maxHigh = currentPrice;
                    minLow = currentPrice;
                }
                
                bool planUpdated = false;
                var justActivatedIds = new HashSet<string>();
                latestPlan.Scenarios ??= new List<EaTradeScenario>();

                // One-Cancels-Other: chỉ kích hoạt scenario mới nếu chưa có scenario ACTIVE
                bool isAnyScenarioActive = latestPlan.HasActiveScenario;

                if (!isAnyScenarioActive)
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
                    }
                }

                if (planUpdated)
                {
                    await _marketDataStore.SetLatestPlanAsync(request.Symbol, latestPlan);
                }

                // TH1: NO TRADE nhưng cấu trúc đổi
                if (!latestPlan.HasScenarios && hasStructuralChange)
                {
                    shouldTriggerAi = true;
                    triggerReason = "Cấu trúc vừa thay đổi, gọi AI để xem đã có setup giao dịch chưa.";
                }

                // TH2/TH3: SL / Full TP của scenario ACTIVE
                if (currentPrice > 0)
                {
                    var active = latestPlan.Scenarios.Where(s => s.IsActive).ToList();
                    foreach (var scenario in active)
                    {
                        bool justActivated = justActivatedIds.Contains(scenario.Id);
                        bool hitSl = scenario.IsBuy
                            ? (justActivated ? currentPrice <= scenario.StopLoss : minLow <= scenario.StopLoss)
                            : (justActivated ? currentPrice >= scenario.StopLoss : maxHigh >= scenario.StopLoss);

                        if (hitSl)
                        {
                            scenario.Status = EaScenarioStatus.HitSl;
                            shouldTriggerAi = true;
                            triggerReason = $"Giá đã cắn Stop Loss ({scenario.StopLoss}) của kịch bản [{scenario.Id}] {scenario.Name}. Đang tính toán lại...";
                            await _marketDataStore.SetLatestPlanAsync(request.Symbol, latestPlan);
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
                            shouldTriggerAi = true;
                            triggerReason = $"🎉 Giá đã lấp đầy TP ({finalTp}) của kịch bản [{scenario.Id}] {scenario.Name}. Bắt đầu lấy Plan mới...";
                            await _marketDataStore.SetLatestPlanAsync(request.Symbol, latestPlan);
                            break;
                        }
                    }
                }
            }

            if (shouldTriggerAi)
            {
                _logger.LogInformation("TRIGGER KÍCH HOẠT: {Reason}", triggerReason);
                await _notificationService.SendMessageAsync($"🚨 *CẬP NHẬT KHẨN CẤP:* {triggerReason}");
                
                // Đẩy vào Hangfire để gọi lại AI
                _backgroundJobClient.Enqueue<IAiNotificationJob>(job =>
                    job.GenerateAndNotifyPlanAsync(request.Symbol, analysisResults, triggerReason, false, null));
            }

            return Ok(new 
            { 
                success = true, 
                message = "Cập nhật nến thành công.",
                triggered_ai = shouldTriggerAi,
                reason = triggerReason
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi cập nhật Market Data.");
            return StatusCode(500, new { success = false, message = "Đã xảy ra lỗi nội bộ." });
        }
    }
}