namespace TradingIntelligenceEngine.Domain.Configuration;

public sealed class BreakoutOptions
{
    public bool Enabled { get; set; } = true;
    public string KlinesApiUrl { get; set; } = "https://api.mobiusquant.ai/api/klines";
    public string Exchange { get; set; } = "commodity";
    public string Market { get; set; } = "spot";
    public string Symbol { get; set; } = "XAUUSD";
    public string Interval { get; set; } = "5m";
    public int Limit { get; set; } = 60;
    public int IntervalMinutes { get; set; } = 15;
    public decimal MaxRiskUsd { get; set; } = 100m;
    public bool NotifyOnScan { get; set; } = true;

    /// <summary>
    /// Chế độ nhận diện cấu trúc mô hình:
    /// - "AI": Dùng AI đọc nến và áp dụng Skill pattern-recognition.
    /// - "Quant" (hoặc "Algorithm"): Dùng 100% thuật toán định lượng hình học C#.
    /// </summary>
    public string PatternMode { get; set; } = "AI";

    /// <summary>
    /// Chế độ lập kịch bản Breakout:
    /// - "AI": Dùng AI lập kịch bản theo Skill breakout-scenario-planning.
    /// - "Quant" (hoặc "Algorithm"): Dùng 100% công thức định lượng C#.
    /// </summary>
    public string ScenarioMode { get; set; } = "AI";

    /// <summary>
    /// Tự động chuyển về Quant nếu AI gặp sự cố (quá tải rate limit, mất mạng, v.v.)
    /// </summary>
    public bool EnableFallbackToQuant { get; set; } = true;
}
