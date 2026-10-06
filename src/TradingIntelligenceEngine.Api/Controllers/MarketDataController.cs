using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingIntelligenceEngine.Application.DTOs.MarketData;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.MarketData;
using TradingIntelligenceEngine.Domain.MarketState;

namespace TradingIntelligenceEngine.Api.Controllers;

[ApiController]
[Route("api/v1/market-data")]
public class MarketDataController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly IMarketAnalyzer _marketAnalyzer;
    private readonly ILogger<MarketDataController> _logger;

    public MarketDataController(
        INotificationService notificationService,
        IMarketAnalyzer marketAnalyzer,
        ILogger<MarketDataController> logger)
    {
        _notificationService = notificationService;
        _marketAnalyzer = marketAnalyzer;
        _logger = logger;
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
                    volume: c.TickVolume
                )).ToList();

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

            // Trả về JSON Data của thuật toán để kiểm tra
            return Ok(new 
            { 
                success = true, 
                message = "Phân tích cấu trúc thành công.",
                data = analysisResults
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi hệ thống khi phân tích Market Data.");
            return StatusCode(500, new { success = false, message = "Đã xảy ra lỗi nội bộ." });
        }
    }
}