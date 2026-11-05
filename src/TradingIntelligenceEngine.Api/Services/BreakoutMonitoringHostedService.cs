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
/// Hosted Service: định kỳ quét lại các symbol mà EA đã yêu cầu qua API POST /api/v1/breakout/scan,
/// dùng đúng tham số EA gửi (symbol, khung nến, số nến, rủi ro, chế độ AI/Quant, notify).
/// Không tự quét theo appsettings; symbol ngừng được quét khi EA không còn gọi scan / poll kế hoạch.
/// </summary>
public class BreakoutMonitoringHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly BreakoutOptions _options;
    private readonly BreakoutScanRegistry _registry;
    private readonly ILogger<BreakoutMonitoringHostedService> _logger;

    public BreakoutMonitoringHostedService(
        IServiceProvider serviceProvider,
        BreakoutOptions options,
        BreakoutScanRegistry registry,
        ILogger<BreakoutMonitoringHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _registry = registry;
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
        _logger.LogInformation(
            "Khởi động BreakoutMonitoringHostedService. Chu kỳ quét lại: mỗi {Minutes} phút, chỉ cho các symbol EA đã gửi API scan.",
            intervalMinutes);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RescanRegisteredSymbolsAsync(stoppingToken);
        }
    }

    private async Task RescanRegisteredSymbolsAsync(CancellationToken stoppingToken)
    {
        var ttl = TimeSpan.FromMinutes(_options.SubscriptionTtlMinutes > 0 ? _options.SubscriptionTtlMinutes : 30);
        var requests = _registry.GetActive(ttl);
        if (requests.Count == 0)
        {
            _logger.LogInformation("⏰ Chu kỳ quét Breakout: chưa có EA nào đăng ký scan (hoặc EA đã ngừng hoạt động). Bỏ qua.");
            return;
        }

        foreach (var request in requests)
        {
            try
            {
                _logger.LogInformation(
                    "⏰ Quét lại Breakout cho {Symbol} theo tham số EA ({Interval}, limit={Limit}, risk={Risk}, [{PMode}/{SMode}])",
                    request.Symbol, request.Interval, request.Limit, request.MaxRiskUsd, request.PatternMode, request.ScenarioMode);

                using var scope = _serviceProvider.CreateScope();
                var breakoutService = scope.ServiceProvider.GetRequiredService<IBreakoutPatternService>();

                var result = await breakoutService.AnalyzeAndExecuteBreakoutSyncAsync(
                    symbol: request.Symbol,
                    interval: request.Interval,
                    limit: request.Limit,
                    maxRiskUsd: request.MaxRiskUsd,
                    patternMode: request.PatternMode,
                    scenarioMode: request.ScenarioMode,
                    notify: request.Notify,
                    cancellationToken: stoppingToken);

                _logger.LogInformation(
                    "✅ Hoàn tất quét Breakout cho {Symbol}. Mô hình: {Pattern} ({Status}). Số kịch bản: {ScenariosCount}",
                    result.PatternRecognition.Symbol,
                    result.PatternRecognition.PrimaryPattern.Name,
                    result.PatternRecognition.PrimaryPattern.Status,
                    result.TradingPlan.Scenarios.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Dừng chu kỳ quét Breakout do ứng dụng đang tắt.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Lỗi khi quét lại Breakout cho {Symbol}.", request.Symbol);
            }
        }
    }
}
