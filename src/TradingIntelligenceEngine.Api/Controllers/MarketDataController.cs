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

            foreach (var tfData in request.Timeframes)
            {
                // Parse Timeframe Enum
                if (!Enum.TryParse<Timeframe>(tfData.Key, true, out var timeframeEnum))
                {
                    _logger.LogWarning("Timeframe {Timeframe} không hợp lệ, bỏ qua.", tfData.Key);
                    continue;
                }

                // Map DTO to Domain Models
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

                // Lưu dữ liệu vào RAM
                await _marketDataStore.InitializeCandlesAsync(request.Symbol, tfData.Key, tfData.Value);

                // Run the core Quant Algorithm (Phase 1)
                var analysisRequest = new MarketAnalysisRequest(request.Symbol, timeframeEnum, domainCandles);
                var marketState = _marketAnalyzer.Analyze(analysisRequest);
                
                analysisResults.Add(tfData.Key, marketState);
            }

            int totalCandles = request.Timeframes.Sum(tf => tf.Value.Count);
            
            // Xây dựng nội dung tin nhắn dễ đọc gửi lên Google Chat
            var msgBuilder = new System.Text.StringBuilder();
            msgBuilder.AppendLine("🚀 *[AI TRADING STRATEGIST]*");
            msgBuilder.AppendLine("Đã hoàn thành quét Cấu Trúc Thị Trường (Giai đoạn 1)!\n");
            msgBuilder.AppendLine($"📊 *Cặp giao dịch:* {request.Symbol}\n");

            foreach (var kvp in analysisResults)
            {
                var tf = kvp.Key;
                var state = kvp.Value;
                
                string trend = state.Trend?.Direction.ToString() == "Bullish" ? "🟢 Tăng (Bullish)" : "🔴 Giảm (Bearish)";
                
                // Dịch các nhãn cấu trúc sang tiếng Việt cho dễ hiểu
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

            string message = msgBuilder.ToString();
            _logger.LogInformation("Phân tích thành công {Symbol}. Tổng nến: {TotalCandles}", request.Symbol, totalCandles);
            await _notificationService.SendMessageAsync(message);

            // GIAI ĐOẠN 2: Gọi AI Lên Kịch Bản thông qua HANGFIRE (Background Job)
            _backgroundJobClient.Enqueue<IAiNotificationJob>(job => job.GenerateAndNotifyPlanAsync(request.Symbol, analysisResults, "Khởi tạo dữ liệu ban đầu"));

            // Trả về response ngay lập tức cho EA/Postman mà không cần chờ AI
            return Ok(new 
            { 
                success = true, 
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
                var justActivatedZones = new HashSet<string>();

                // Kiểm tra xem hiện tại CÓ KỊCH BẢN NÀO ĐANG CHẠY (ĐÃ KHỚP) CHƯA?
                bool isAnyScenarioActive = 
                    (latestPlan.BuyScenarios?.Any(b => b.IsActive) == true) ||
                    (latestPlan.SellScenarios?.Any(s => s.IsActive) == true) ||
                    (latestPlan.BreakoutScenarios?.Any(br => br.IsActive) == true);

                // 1. Cập nhật trạng thái IsActive 
                // LOGIC QUAN TRỌNG (One-Cancels-Other): CHỈ khớp lệnh mới nếu CHƯA CÓ lệnh nào đang chạy.
                // Nếu đã có 1 lệnh Buy đang chạy, thì bỏ qua không kiểm tra điểm vào của lệnh Sell/Breakout nữa, tránh xung đột.
                if (!isAnyScenarioActive)
                {
                    if (latestPlan.BuyScenarios != null)
                    {
                        foreach (var b in latestPlan.BuyScenarios)
                        {
                            if (!isAnyScenarioActive && currentPrice <= b.EntryTop)
                            { 
                                b.IsActive = true; 
                                planUpdated = true; 
                                justActivatedZones.Add(b.ZoneName); 
                                isAnyScenarioActive = true;
                            }
                        }
                    }
                    
                    if (latestPlan.SellScenarios != null)
                    {
                        foreach (var s in latestPlan.SellScenarios)
                        {
                            if (!isAnyScenarioActive && currentPrice >= s.EntryBottom)
                            { 
                                s.IsActive = true; 
                                planUpdated = true; 
                                justActivatedZones.Add(s.ZoneName);
                                isAnyScenarioActive = true;
                            }
                        }
                    }

                    if (latestPlan.BreakoutScenarios != null)
                    {
                        foreach (var br in latestPlan.BreakoutScenarios)
                        {
                            if (!isAnyScenarioActive)
                            {
                                if (br.Type.Contains("BUY", StringComparison.OrdinalIgnoreCase) && currentPrice >= br.TriggerPrice)
                                { 
                                    br.IsActive = true; 
                                    planUpdated = true; 
                                    justActivatedZones.Add(br.Type);
                                    isAnyScenarioActive = true;
                                }
                                else if (br.Type.Contains("SELL", StringComparison.OrdinalIgnoreCase) && currentPrice <= br.TriggerPrice)
                                { 
                                    br.IsActive = true; 
                                    planUpdated = true; 
                                    justActivatedZones.Add(br.Type);
                                    isAnyScenarioActive = true;
                                }
                            }
                        }
                    }
                }

                // Nếu có sự thay đổi State thì lưu lại Plan vào Cache ngay lập tức
                if (planUpdated)
                {
                    await _marketDataStore.SetLatestPlanAsync(request.Symbol, latestPlan);
                }

                // TH1: AI đang bảo "NO TRADE", nhưng nay cấu trúc đã thay đổi
                if ((latestPlan.BuyScenarios == null || !latestPlan.BuyScenarios.Any()) && 
                    (latestPlan.SellScenarios == null || !latestPlan.SellScenarios.Any()) && hasStructuralChange)
                {
                    shouldTriggerAi = true;
                    triggerReason = "Cấu trúc vừa thay đổi, gọi AI để xem đã có setup giao dịch chưa.";
                }
                
                // TH2: Giá hiện tại đã cắn Stop Loss của các kịch bản ĐANG ACTIVE (Invalidation)
                if (currentPrice > 0)
                {
                    // LƯU Ý LOGIC: 
                    // Nếu kịch bản VỪA MỚI kích hoạt ở tick này, chỉ dùng CurrentPrice để check SL (tránh râu nến ảo quá khứ).
                    // Nếu kịch bản ĐÃ kích hoạt từ trước, được phép dùng maxHigh/minLow để bắt râu nến quét SL.
                    var hitSell = latestPlan.SellScenarios?.FirstOrDefault(s => s.IsActive && (justActivatedZones.Contains(s.ZoneName) ? currentPrice >= s.StopLoss : maxHigh >= s.StopLoss));
                    var hitBuy = latestPlan.BuyScenarios?.FirstOrDefault(b => b.IsActive && (justActivatedZones.Contains(b.ZoneName) ? currentPrice <= b.StopLoss : minLow <= b.StopLoss));
                    var hitBreakout = latestPlan.BreakoutScenarios?.FirstOrDefault(br => 
                        br.IsActive && (
                            (br.Type.Contains("BUY", StringComparison.OrdinalIgnoreCase) && (justActivatedZones.Contains(br.Type) ? currentPrice <= br.StopLoss : minLow <= br.StopLoss)) || 
                            (br.Type.Contains("SELL", StringComparison.OrdinalIgnoreCase) && (justActivatedZones.Contains(br.Type) ? currentPrice >= br.StopLoss : maxHigh >= br.StopLoss))
                        ));
                    
                    if (hitSell != null)
                    {
                        shouldTriggerAi = true;
                        triggerReason = $"Giá ({maxHigh}) đã cắn Stop Loss ({hitSell.StopLoss}) của kịch bản BÁN [{hitSell.ZoneName}]. Đang tính toán lại...";
                    }
                    else if (hitBuy != null)
                    {
                        shouldTriggerAi = true;
                        triggerReason = $"Giá ({minLow}) đã cắn Stop Loss ({hitBuy.StopLoss}) của kịch bản MUA [{hitBuy.ZoneName}]. Đang tính toán lại...";
                    }
                    else if (hitBreakout != null)
                    {
                        shouldTriggerAi = true;
                        decimal hitPrice = hitBreakout.Type.Contains("BUY", StringComparison.OrdinalIgnoreCase) ? minLow : maxHigh;
                        triggerReason = $"Giá ({hitPrice}) đã cắn Stop Loss ({hitBreakout.StopLoss}) của kịch bản BREAKOUT [{hitBreakout.Type}]. Đang tính toán lại...";
                    }
                    else
                    {
                        // TH3: Giá hiện tại đã đạt Full TP (Take Profit cuối cùng) của một kịch bản ĐANG ACTIVE
                        var hitFullTpBuy = latestPlan.BuyScenarios?.FirstOrDefault(b => b.IsActive && b.TakeProfits != null && b.TakeProfits.Any() && (justActivatedZones.Contains(b.ZoneName) ? currentPrice >= b.TakeProfits.Max() : maxHigh >= b.TakeProfits.Max()));
                        var hitFullTpSell = latestPlan.SellScenarios?.FirstOrDefault(s => s.IsActive && s.TakeProfits != null && s.TakeProfits.Any() && (justActivatedZones.Contains(s.ZoneName) ? currentPrice <= s.TakeProfits.Min() : minLow <= s.TakeProfits.Min()));
                        var hitFullTpBreakoutBuy = latestPlan.BreakoutScenarios?.FirstOrDefault(br => br.IsActive && br.Type.Contains("BUY", StringComparison.OrdinalIgnoreCase) && br.TakeProfits != null && br.TakeProfits.Any() && (justActivatedZones.Contains(br.Type) ? currentPrice >= br.TakeProfits.Max() : maxHigh >= br.TakeProfits.Max()));
                        var hitFullTpBreakoutSell = latestPlan.BreakoutScenarios?.FirstOrDefault(br => br.IsActive && br.Type.Contains("SELL", StringComparison.OrdinalIgnoreCase) && br.TakeProfits != null && br.TakeProfits.Any() && (justActivatedZones.Contains(br.Type) ? currentPrice <= br.TakeProfits.Min() : minLow <= br.TakeProfits.Min()));

                        if (hitFullTpBuy != null)
                        {
                            shouldTriggerAi = true;
                            triggerReason = $"🎉 TUYỆT VỜI! Giá ({maxHigh}) đã lấp đầy toàn bộ TP ({hitFullTpBuy.TakeProfits.Max()}) của kịch bản MUA [{hitFullTpBuy.ZoneName}]. Bắt đầu lấy Plan mới...";
                        }
                        else if (hitFullTpSell != null)
                        {
                            shouldTriggerAi = true;
                            triggerReason = $"🎉 TUYỆT VỜI! Giá ({minLow}) đã lấp đầy toàn bộ TP ({hitFullTpSell.TakeProfits.Min()}) của kịch bản BÁN [{hitFullTpSell.ZoneName}]. Bắt đầu lấy Plan mới...";
                        }
                        else if (hitFullTpBreakoutBuy != null)
                        {
                            shouldTriggerAi = true;
                            triggerReason = $"🎉 TUYỆT VỜI! Giá ({maxHigh}) đã lấp đầy toàn bộ TP ({hitFullTpBreakoutBuy.TakeProfits.Max()}) của kịch bản BREAKOUT BUY. Bắt đầu lấy Plan mới...";
                        }
                        else if (hitFullTpBreakoutSell != null)
                        {
                            shouldTriggerAi = true;
                            triggerReason = $"🎉 TUYỆT VỜI! Giá ({minLow}) đã lấp đầy toàn bộ TP ({hitFullTpBreakoutSell.TakeProfits.Min()}) của kịch bản BREAKOUT SELL. Bắt đầu lấy Plan mới...";
                        }
                    }
                }
            }

            if (shouldTriggerAi)
            {
                _logger.LogInformation("TRIGGER KÍCH HOẠT: {Reason}", triggerReason);
                await _notificationService.SendMessageAsync($"🚨 *CẬP NHẬT KHẨN CẤP:* {triggerReason}");
                
                // Đẩy vào Hangfire để gọi lại AI
                _backgroundJobClient.Enqueue<IAiNotificationJob>(job => job.GenerateAndNotifyPlanAsync(request.Symbol, analysisResults, triggerReason));
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