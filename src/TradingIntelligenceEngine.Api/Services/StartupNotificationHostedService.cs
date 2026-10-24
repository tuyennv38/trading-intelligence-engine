using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Application.Interfaces;

namespace TradingIntelligenceEngine.Api.Services;

/// <summary>
/// Gửi Google Chat ngay khi API khởi động xong.
/// </summary>
public class StartupNotificationHostedService : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<StartupNotificationHostedService> _logger;

    public StartupNotificationHostedService(
        IServiceProvider services,
        ILogger<StartupNotificationHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var notifier = scope.ServiceProvider.GetService<INotificationService>();
        if (notifier == null)
        {
            _logger.LogWarning("Bỏ qua Chat khởi động vì Notification chưa cấu hình.");
            return;
        }

        var now = VietnamNow();
        var message =
            "🚀 *[AI TRADING STRATEGIST]*\n" +
            "🟢 Chương trình bắt đầu khởi chạy.\n" +
            $"⏰ {now:dd/MM/yyyy HH:mm:ss} (UTC+7)\n" +
            "📡 Sẵn sàng nhận nến từ EA.";

        try
        {
            await notifier.SendMessageAsync(message);
            _logger.LogInformation("Đã gửi Google Chat: chương trình bắt đầu khởi chạy.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gửi Google Chat lúc khởi động thất bại.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static DateTime VietnamNow()
    {
        var utc = DateTime.UtcNow;
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(id));
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return utc.AddHours(7);
    }
}
