namespace TradingIntelligenceEngine.Domain.Configuration;

/// <summary>
/// Cấu hình hạ tầng & thuật toán Breakout. Tham số quét (symbol, khung nến, số nến, rủi ro, chế độ AI/Quant, notify)
/// KHÔNG nằm ở đây: chúng luôn do client (EA) gửi qua API POST /api/v1/breakout/scan.
/// </summary>
public sealed class BreakoutOptions
{
    /// <summary>Bật job quét lại định kỳ các symbol mà EA đã đăng ký qua API scan.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Chu kỳ job quét lại (phút).</summary>
    public int IntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Job chỉ quét lại một symbol khi EA còn hoạt động: quá số phút này không thấy EA gọi scan / poll kế hoạch thì dừng quét symbol đó.
    /// </summary>
    public int SubscriptionTtlMinutes { get; set; } = 30;

    // --- Nguồn dữ liệu nến Mobius ---
    public string KlinesApiUrl { get; set; } = "https://api.mobiusquant.ai/api/klines";
    public string Exchange { get; set; } = "commodity";
    public string Market { get; set; } = "spot";

    // --- Tham số thuật toán lập kịch bản ---

    /// <summary>
    /// Nếu true: chỉ lập kịch bản breakout khi mô hình đã CONFIRMED (mô hình POTENTIAL/DEVELOPING chỉ theo dõi).
    /// Nếu false (mặc định): lập kịch bản cho từng biên đã có tối thiểu 2 lần chạm độc lập, bất kể nhãn mô hình.
    /// </summary>
    public bool RequireConfirmedPattern { get; set; } = false;

    /// <summary>
    /// Bỏ kịch bản (NO_CHASE) nếu giá đã vượt trigger quá số lần ATR này.
    /// </summary>
    public decimal NoChaseAtr { get; set; } = 1.0m;

    /// <summary>Khoảng cách SL tối thiểu tính từ trigger (bội số ATR).</summary>
    public decimal SlMinAtr { get; set; } = 1.2m;

    /// <summary>Khoảng cách SL tối đa tính từ trigger (bội số ATR).</summary>
    public decimal SlMaxAtr { get; set; } = 2.5m;

    /// <summary>Thời gian hiệu lực của kế hoạch Breakout (phút). EA không vào lệnh mới theo plan đã hết hạn.</summary>
    public int PlanValidityMinutes { get; set; } = 60;
}
