namespace TradingIntelligenceEngine.Domain.Configuration;

public sealed class MobiusOptions
{
    private readonly object _lock = new();
    private string _baseUrl = "https://mobius.cscmobicorp.com";
    private string _endpoint = "/api/v1/strategy/scenarios";
    private int _timeoutSeconds = 90;

    public string BaseUrl
    {
        get { lock (_lock) return _baseUrl; }
        set { lock (_lock) _baseUrl = string.IsNullOrWhiteSpace(value) ? "https://mobius.cscmobicorp.com" : value.TrimEnd('/'); }
    }

    public string Endpoint
    {
        get { lock (_lock) return _endpoint; }
        set { lock (_lock) _endpoint = string.IsNullOrWhiteSpace(value) ? "/api/v1/strategy/scenarios" : value; }
    }

    public int TimeoutSeconds
    {
        get { lock (_lock) return _timeoutSeconds; }
        set { lock (_lock) _timeoutSeconds = value > 0 ? value : 90; }
    }
}
