using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Application.Interfaces;
using TradingIntelligenceEngine.Domain.Configuration;

namespace TradingIntelligenceEngine.Api.Services;

/// <summary>
/// Hosted Service: Tự động chạy ngay khi khởi động chương trình,
/// sau đó lặp lại định kỳ mỗi 15 phút để lấy nến từ Mobius API và nhận diện kịch bản Breakout.
/// </summary>
public class BreakoutMonitoringHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BreakoutOptions _options;
    private readonly ILogger<BreakoutMonitoringHostedService> _logger;

    public BreakoutMonitoringHostedService(
        IServiceProvider serviceProvider,
        BreakoutOptions options,
        ILogger<BreakoutMonitoringHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("BreakoutMonitoringHostedService đang ở trạng thái TẮT (Enabled = false).");
            return;
        }

        var intervalMinutes = _options.IntervalMinutes > 0 ? _options.IntervalMinutes : 15;
        _logger.LogInformation("Khởi động BreakoutMonitoringHostedService. Chu kỳ quét: mỗi {Minutes} phút.", intervalMinutes);

        // Đợi 2 giây để Web server và DI container khởi động hoàn toàn
        await Task.Delay(2000, stoppingToken);

        // 1. Chạy ngay lần đầu tiên lúc khởi động chương trình
        await ExecuteScanSafelyAsync(stoppingToken);

        // 2. Chạy lặp lại định kỳ mỗi 15 phút
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteScanSafelyAsync(stoppingToken);
        }
    }

    private async Task ExecuteScanSafelyAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("⏰ Bắt đầu chu kỳ quét nến Mobius & nhận diện mô hình Breakout...");
            using var scope = _serviceProvider.CreateScope();
            var breakoutService = scope.ServiceProvider.GetRequiredService<IBreakoutPatternService>();

            var result = await breakoutService.AnalyzeAndExecuteBreakoutSyncAsync(
                cancellationToken: stoppingToken);

            _logger.LogInformation(
                "✅ Hoàn tất chu kỳ quét Breakout cho {Symbol}. Mô hình: {Pattern} ({Status}). Số kịch bản: {ScenariosCount}",
                result.PatternRecognition.Symbol,
                result.PatternRecognition.PrimaryPattern.Name,
                result.PatternRecognition.PrimaryPattern.Status,
                result.TradingPlan.Scenarios.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Dừng chu kỳ quét Breakout do ứng dụng đang tắt.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Lỗi trong quá trình quét Breakout định kỳ.");
        }
    }
}
