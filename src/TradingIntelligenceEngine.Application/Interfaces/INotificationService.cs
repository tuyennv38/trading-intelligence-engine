namespace TradingIntelligenceEngine.Application.Interfaces;

public interface INotificationService
{
    /// <summary>
    /// Gửi một tin nhắn thông báo (tới Telegram, Google Chat, v.v. tùy thuộc vào cấu hình).
    /// </summary>
    /// <param name="message">Nội dung tin nhắn cần gửi.</param>
    /// <returns>Task đại diện cho quá trình xử lý bất đồng bộ.</returns>
    Task SendMessageAsync(string message);
}