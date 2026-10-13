namespace TradingIntelligenceEngine.Api.Models;

public sealed class AiProviderConfigDto
{
    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string Model { get; set; } = string.Empty;
}

public sealed class AiProviderConfigResponseDto
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool HasApiKey { get; set; }
    public string? ApiKeyMasked { get; set; }
}
