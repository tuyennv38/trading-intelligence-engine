namespace TradingIntelligenceEngine.Domain.Configuration;

public sealed class AiProviderOptions
{
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
}