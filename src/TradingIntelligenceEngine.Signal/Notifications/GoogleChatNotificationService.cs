using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingIntelligenceEngine.Application.Interfaces;

namespace TradingIntelligenceEngine.Signal.Notifications;

public class GoogleChatNotificationService : INotificationService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleChatNotificationService> _logger;

    public GoogleChatNotificationService(
        HttpClient httpClient, 
        IConfiguration configuration, 
        ILogger<GoogleChatNotificationService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendMessageAsync(string message)
    {
        try
        {
            var webhookUrl = _configuration["Notification:GoogleChatWebhookUrl"];
            
            if (string.IsNullOrEmpty(webhookUrl))
            {
                _logger.LogWarning("Google Chat Webhook URL is not configured. Message was not sent.");
                return;
            }

            // Theo document của Google Chat API, body cần có trường "text"
            var payload = new { text = message };

            var response = await _httpClient.PostAsJsonAsync(webhookUrl, payload);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to send message to Google Chat. Status: {response.StatusCode}, Details: {errorContent}");
            }
            else
            {
                _logger.LogInformation("Successfully sent message to Google Chat.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while sending message to Google Chat.");
        }
    }
}