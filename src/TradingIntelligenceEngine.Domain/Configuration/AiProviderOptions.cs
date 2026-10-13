namespace TradingIntelligenceEngine.Domain.Configuration;

public sealed class AiProviderOptions
{
    private readonly object _lock = new();
    private string _baseUrl = string.Empty;
    private string _apiKey = string.Empty;
    private string _model = string.Empty;

    public string BaseUrl
    {
        get { lock (_lock) return _baseUrl; }
        set { lock (_lock) _baseUrl = value ?? string.Empty; }
    }

    public string ApiKey
    {
        get { lock (_lock) return _apiKey; }
        set { lock (_lock) _apiKey = value ?? string.Empty; }
    }

    public string Model
    {
        get { lock (_lock) return _model; }
        set { lock (_lock) _model = value ?? string.Empty; }
    }

    public void Update(string? baseUrl, string? apiKey, string? model)
    {
        lock (_lock)
        {
            if (baseUrl is not null)
                _baseUrl = baseUrl;

            // Empty/null apiKey keeps the existing key so clients can update URL/model without re-sending secrets.
            if (!string.IsNullOrWhiteSpace(apiKey))
                _apiKey = apiKey;

            if (model is not null)
                _model = model;
        }
    }

    public (string BaseUrl, string ApiKey, string Model) Snapshot()
    {
        lock (_lock)
            return (_baseUrl, _apiKey, _model);
    }
}
