//+------------------------------------------------------------------+
//|                                             BreakoutTrader.mq5   |
//|                                  Trading Intelligence Engine     |
//|               Chuyên Giao Dịch Breakout Theo Kịch Bản API        |
//+------------------------------------------------------------------+
#property copyright "Trading Intelligence Engine"
#property link      "https://github.com/tuyennv38/trading-intelligence-engine"
#property version   "2.00"
#property description "EA chuyên giao dịch Breakout theo kịch bản từ Trading Intelligence Engine API"
#property description "v2: 1 lệnh/1 thời điểm (OCO), 1 TP + chốt một phần theo point, hủy kịch bản khi phá vỡ giả."

#include <Trade\Trade.mqh>
#include <JAson.mqh>

enum ENUM_ENTRY_MODE {
    ENTRY_AUTO = 0,      // Theo server (entry_mode của kịch bản)
    ENTRY_RETEST = 1,    // Luôn chờ retest
    ENTRY_MOMENTUM = 2   // Luôn vào ngay khi nến xác nhận đóng phá vỡ
};

//====================================================================
// 1. CÀI ĐẶT KẾT NỐI API & QUÉT KỊCH BẢN BREAKOUT
//====================================================================
input group "=== [1] KẾT NỐI API & QUÉT KỊCH BẢN BREAKOUT ==="
input string InpApiBaseUrl                 = "https://botai.cscmobicorp.com"; // Địa chỉ Base URL của Trading Intelligence Engine
input bool   InpRequestScanOnStart         = true;             // Gửi yêu cầu scan kịch bản ngay khi khởi động EA
input int    InpAutoScanIntervalMinutes    = 15;               // Chu kỳ tự động gọi API scan kịch bản mới (phút, 0 = tắt)
input string InpScanInterval               = "5m";             // Khung thời gian nến quét phân tích (5m, 15m, 1h, ...)
input int    InpScanLimit                  = 60;               // Số lượng nến quét cấu trúc mô hình
input string InpScanPatternMode            = "AI";             // Chế độ nhận diện mô hình: "AI" hoặc "Quant"
input string InpScanScenarioMode           = "AI";             // Chế độ lập kịch bản: "AI" hoặc "Quant"
input bool   InpNotifyGoogleChat           = true;             // Gửi thông báo kết quả scan lên Google Chat
input int    InpCheckScenarioSec           = 60;               // Chu kỳ gọi API kiểm tra kịch bản mới (giây)
input int    InpMaxPlanAgeMinutes          = 90;               // Tuổi tối đa của kế hoạch nếu server không gửi valid_until (phút, 0 = tắt)

//====================================================================
// 2. QUẢN LÝ RỦI RO NGÀY (DAILY RISK MANAGEMENT)
//====================================================================
input group "=== [2] QUẢN LÝ RỦI RO LÃI/LỖ THEO NGÀY ==="
input double InpDailyTargetProfit          = 100.0;            // Mục tiêu chốt lời ngày (USD, 0 = tắt). Đạt mức này sẽ chốt hết lệnh và nghỉ
input double InpMaxDailyLoss               = 200.0;            // Ngưỡng cắt lỗ tối đa 1 ngày (USD, 0 = tắt). Chạm mức này đóng hết lệnh và nghỉ
input int    InpLossCooldownMinutes        = 30;               // Thời gian nghỉ sau lệnh thua (phút, 0 = tắt). Tránh giao dịch trả thù
input int    InpMinPlanIntervalMinutes     = 10;               // Giãn cách tối thiểu giữa 2 lần gửi yêu cầu scan (phút)

//====================================================================
// 3. CÀI ĐẶT VÀO LỆNH BREAKOUT
//====================================================================
input group "=== [3] CÀI ĐẶT VÀO LỆNH BREAKOUT ==="
input double          InpMaxRiskMoney       = 50.0;            // Rủi ro tối đa 1 lần vào lệnh (USD). Lot tính chuẩn từ SL
input ulong           InpMagicNumber        = 888999;          // Magic Number riêng cho Breakout Trader
input int             InpSlippage           = 10;              // Độ trượt giá tối đa (points)
input ENUM_ENTRY_MODE InpEntryMode          = ENTRY_AUTO;      // Cách vào lệnh sau khi nến xác nhận phá vỡ
input int             InpMaxRetestBars      = 12;              // Tối đa số nến khung retest chờ hồi về trigger (0 = không giới hạn)
input bool            InpUseConfirmPatterns = true;            // Nến xác nhận phải khớp mô hình nến server gửi (STRONG_BODY/MARUBOZU/...)
input bool            InpUseRetestPatterns  = false;           // Nến retest phải khớp mô hình nến server gửi (PINBAR/ENGULFING/...)
input double          InpMaxChaseSlPct      = 50.0;            // Giá vào cách trigger tối đa bao nhiêu % khoảng SL (0 = tắt). Vượt = NO_CHASE
input double          InpMaxSpreadSlPct     = 10.0;            // Spread tối đa bao nhiêu % khoảng SL (0 = tắt)

//====================================================================
// 4. QUẢN LÝ LỆNH: 1 TP + CHỐT MỘT PHẦN
//====================================================================
input group "=== [4] QUẢN LÝ LỆNH: 1 TP + CHỐT MỘT PHẦN ==="
input int    InpPartialTriggerPoints       = 500;              // Lãi >= bao nhiêu points thì chốt một phần (0 = tắt)
input double InpPartialClosePercent        = 50.0;             // Chốt bao nhiêu % volume khi đạt ngưỡng trên
input bool   InpMoveSlToBeAfterPartial     = true;             // Dời SL phần còn lại về hòa vốn sau khi chốt một phần
input int    InpBreakevenOffset            = 20;               // Bù trừ hòa vốn (points) khi dời SL
input bool   InpEnableSmallTfExit          = true;             // Sau khi chốt một phần: thoát phần còn lại khi nến khung retest đảo chiều
input bool   InpSmallTfExitInProfitOnly    = true;             // Chỉ thoát đảo chiều khung nhỏ khi phần còn lại đang có lãi

//====================================================================
// ĐỊNH NGHĨA STRUCT VÀ BIẾN TOÀN CỤC
//====================================================================
CTrade trade;

enum ENUM_SCENARIO_TYPE {
    BREAKOUT_SELL,
    BREAKOUT_BUY,
    UNKNOWN_TYPE
};

enum ENUM_SCENARIO_STATUS {
    PENDING,
    ACTIVE,
    EXPIRED,
    COMPLETED
};

struct Scenario {
    string               id;
    ENUM_SCENARIO_TYPE   type;
    string               name;

    double               trigger_price;
    double               sl;
    double               tp;                   // TP duy nhất
    double               max_risk_usd;
    double               zone_low;             // Vùng bị phá (0 = server không gửi)
    double               zone_high;
    double               invalidation;         // Đóng nến ngược qua mức này trước khi vào lệnh = phá vỡ giả
    string               entry_mode;           // RETEST | MOMENTUM

    ENUM_TIMEFRAMES      confirm_timeframe;
    string               confirm_patterns[5];
    int                  patterns_count;

    ENUM_TIMEFRAMES      retest_timeframe;
    string               retest_patterns[5];
    int                  retest_patterns_count;

    ENUM_SCENARIO_STATUS status;
    bool                 breakout_confirmed;   // Nến confirm đã đóng phá qua trigger
    bool                 retest_touched;       // Giá đã hồi chạm lại trigger
    int                  touch_bar;            // Nến retest thứ mấy chạm trigger
    int                  retest_wait_bars;     // Số nến đã chờ retest
    datetime             last_bar_time;
    datetime             last_retest_bar_time;
};

// Quản lý kịch bản
Scenario g_scenarios[];
int      g_scenarios_count = 0;
string   g_current_signal_id = "";
string   g_current_bias = "";
long     g_current_plan_created_at = 0;   // UTC
long     g_plan_valid_until = 0;          // UTC, 0 = server không gửi
bool     g_plan_expiry_logged = false;
datetime g_last_check_scenario_time = 0;
datetime g_last_scan_request_time = 0;    // Lần scan thành công gần nhất
datetime g_last_scan_attempt_time = 0;    // Lần gửi scan gần nhất (kể cả lỗi)
bool     g_force_scan = false;            // Xin kịch bản mới ngay sau khi đóng lệnh

// Trạng thái lệnh đang chạy (luôn chỉ 1 position)
bool            g_is_trading = false;
int             g_active_scenario_index = -1;
string          g_trade_scenario_id = "";
ulong           g_pos_ticket = 0;
long            g_pos_identifier = 0;
datetime        g_trade_start_time = 0;
bool            g_partial_done = false;
ENUM_TIMEFRAMES g_exit_tf = PERIOD_M1;
datetime        g_exit_bar_time = 0;

// Quản lý lãi/lỗ theo ngày
datetime g_current_day_start = 0;
double   g_daily_closed_pnl = 0.0;
bool     g_history_dirty = true;
bool     g_daily_target_reached = false;
bool     g_daily_loss_reached = false;
datetime g_last_loss_time = 0;

//+------------------------------------------------------------------+
//| Chuyển chuỗi TF thành ENUM_TIMEFRAMES                            |
//+------------------------------------------------------------------+
ENUM_TIMEFRAMES StringToTimeframe(string tf) {
    if(tf == "M1" || tf == "1m") return PERIOD_M1;
    if(tf == "M5" || tf == "5m") return PERIOD_M5;
    if(tf == "M15" || tf == "15m") return PERIOD_M15;
    if(tf == "M30" || tf == "30m") return PERIOD_M30;
    if(tf == "H1" || tf == "1h") return PERIOD_H1;
    if(tf == "H4" || tf == "4h") return PERIOD_H4;
    if(tf == "D1" || tf == "1d") return PERIOD_D1;
    return PERIOD_M5;
}

//+------------------------------------------------------------------+
//| 1 bậc nhỏ hơn khung confirm (M15→M5, M5→M1, H1→M15)              |
//+------------------------------------------------------------------+
ENUM_TIMEFRAMES OneStepSmallerTf(ENUM_TIMEFRAMES tf) {
    if(tf == PERIOD_M1)  return PERIOD_M1;
    if(tf == PERIOD_M5)  return PERIOD_M1;
    if(tf == PERIOD_M15) return PERIOD_M5;
    if(tf == PERIOD_M30) return PERIOD_M15;
    if(tf == PERIOD_H1)  return PERIOD_M15;
    if(tf == PERIOD_H4)  return PERIOD_H1;
    if(tf == PERIOD_D1)  return PERIOD_H4;
    return PERIOD_M1;
}

string TimeframeToString(ENUM_TIMEFRAMES tf) {
    switch(tf) {
        case PERIOD_M1:  return "M1";
        case PERIOD_M5:  return "M5";
        case PERIOD_M15: return "M15";
        case PERIOD_M30: return "M30";
        case PERIOD_H1:  return "H1";
        case PERIOD_H4:  return "H4";
        case PERIOD_D1:  return "D1";
        default:         return "CURRENT";
    }
}

//+------------------------------------------------------------------+
//| Ghép Base URL với path endpoint                                 |
//+------------------------------------------------------------------+
string GetApiUrl(string path) {
    string base = InpApiBaseUrl;
    while(StringLen(base) > 0 && StringSubstr(base, StringLen(base) - 1, 1) == "/") {
        base = StringSubstr(base, 0, StringLen(base) - 1);
    }
    if(StringSubstr(path, 0, 1) != "/") {
        path = "/" + path;
    }
    return base + path;
}

//+------------------------------------------------------------------+
//| Chuẩn hóa mã mô hình nến (khớp BreakoutCandlePatterns ở backend) |
//+------------------------------------------------------------------+
string NormalizePatternCode(string name) {
    string n = name;
    StringToUpper(n);
    if(StringFind(n, "MARUBOZU") >= 0) return "MARUBOZU";
    if(StringFind(n, "ENGULF") >= 0) return "ENGULFING";
    if(StringFind(n, "PIN") >= 0 || StringFind(n, "HAMMER") >= 0 || StringFind(n, "SHOOTING") >= 0 || StringFind(n, "REJECT") >= 0) return "PINBAR";
    if(StringFind(n, "STRONG") >= 0 || StringFind(n, "EXPANSION") >= 0 || StringFind(n, "MOMENTUM") >= 0 || StringFind(n, "BODY") >= 0) return "STRONG_BODY";
    return "";
}

//+------------------------------------------------------------------+
//| Chuẩn hóa volume theo bước lot của sàn                           |
//+------------------------------------------------------------------+
double NormalizeVolume(double volume) {
    double step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
    if(step <= 0.0) step = 0.01;
    double v = MathFloor(volume / step + 1e-9) * step;
    int digits = (int)MathMax(0, MathCeil(-MathLog10(step) - 1e-9));
    return NormalizeDouble(v, digits);
}

//+------------------------------------------------------------------+
//| Kiểm tra còn lệnh đang mở của EA không                           |
//+------------------------------------------------------------------+
bool HasOpenPositions() {
    for(int i = PositionsTotal() - 1; i >= 0; i--) {
        if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber)
            return true;
    }
    return false;
}

//+------------------------------------------------------------------+
//| Chọn position của lệnh breakout đang chạy                        |
//+------------------------------------------------------------------+
bool SelectTradePosition() {
    if(g_pos_ticket > 0 && PositionSelectByTicket(g_pos_ticket))
        return true;

    for(int i = PositionsTotal() - 1; i >= 0; i--) {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0) continue;
        if(PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber) {
            g_pos_ticket = ticket;
            g_pos_identifier = PositionGetInteger(POSITION_IDENTIFIER);
            return true;
        }
    }
    return false;
}

//+------------------------------------------------------------------+
//| Kiểm tra tất cả kịch bản đã kết thúc                             |
//+------------------------------------------------------------------+
bool IsAllScenariosFinished() {
    if(g_scenarios_count == 0) return true;
    for(int i = 0; i < g_scenarios_count; i++) {
        if(g_scenarios[i].status == PENDING || g_scenarios[i].status == ACTIVE)
            return false;
    }
    return true;
}

//+------------------------------------------------------------------+
//| Kế hoạch đã hết hiệu lực chưa (so theo giờ UTC)                  |
//+------------------------------------------------------------------+
bool IsPlanExpired() {
    long now_utc = (long)TimeGMT();
    if(g_plan_valid_until > 0)
        return now_utc > g_plan_valid_until;
    if(InpMaxPlanAgeMinutes > 0 && g_current_plan_created_at > 0)
        return now_utc > g_current_plan_created_at + (long)InpMaxPlanAgeMinutes * 60;
    return false;
}

//+------------------------------------------------------------------+
//| Hủy các kịch bản đang chờ (chưa phá vỡ) khi kế hoạch hết hạn     |
//+------------------------------------------------------------------+
void ExpireStalePendingScenarios() {
    if(g_scenarios_count == 0 || !IsPlanExpired()) return;
    bool changed = false;
    for(int i = 0; i < g_scenarios_count; i++) {
        if(g_scenarios[i].status == PENDING && !g_scenarios[i].breakout_confirmed) {
            g_scenarios[i].status = EXPIRED;
            changed = true;
        }
    }
    if(changed && !g_plan_expiry_logged) {
        g_plan_expiry_logged = true;
        Print("[PLAN EXPIRED] Kế hoạch [", g_current_signal_id, "] đã hết hiệu lực. Hủy các kịch bản chưa phá vỡ, chờ kế hoạch mới.");
        UpdateChartDashboard();
    }
}

//+------------------------------------------------------------------+
//| Kiểm tra sang ngày mới và reset thống kê lãi/lỗ                  |
//+------------------------------------------------------------------+
void CheckDailyReset() {
    MqlDateTime dt;
    TimeCurrent(dt);
    dt.hour = 0; dt.min = 0; dt.sec = 0;
    datetime today_start = StructToTime(dt);

    if(g_current_day_start != today_start) {
        g_current_day_start = today_start;
        g_daily_closed_pnl = 0.0;
        g_history_dirty = true;
        g_daily_target_reached = false;
        g_daily_loss_reached = false;
        Print(StringFormat("[DAILY RESET] Bắt đầu ngày giao dịch mới: %s. Reset chỉ số PnL ngày.", TimeToString(today_start, TIME_DATE)));
    }
}

//+------------------------------------------------------------------+
//| PnL các lệnh đã đóng trong ngày (cache, chỉ tính lại khi có deal) |
//+------------------------------------------------------------------+
double CalculateDailyClosedPnL() {
    CheckDailyReset();
    if(!g_history_dirty) return g_daily_closed_pnl;

    HistorySelect(g_current_day_start, TimeCurrent() + 60);
    double total = 0.0;
    for(int i = 0; i < HistoryDealsTotal(); i++) {
        ulong ticket = HistoryDealGetTicket(i);
        if(HistoryDealGetInteger(ticket, DEAL_MAGIC) == InpMagicNumber && HistoryDealGetString(ticket, DEAL_SYMBOL) == _Symbol) {
            total += HistoryDealGetDouble(ticket, DEAL_PROFIT)
                   + HistoryDealGetDouble(ticket, DEAL_COMMISSION)
                   + HistoryDealGetDouble(ticket, DEAL_SWAP);
        }
    }
    g_daily_closed_pnl = total;
    g_history_dirty = false;
    return total;
}

//+------------------------------------------------------------------+
//| Tính Floating PnL các lệnh đang mở                              |
//+------------------------------------------------------------------+
double GetFloatingPnL() {
    double float_pnl = 0.0;
    for(int i = PositionsTotal() - 1; i >= 0; i--) {
        if(PositionGetSymbol(i) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber) {
            float_pnl += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
        }
    }
    return float_pnl;
}

//+------------------------------------------------------------------+
//| Kiểm tra đang trong thời gian Cooldown sau khi lỗ               |
//+------------------------------------------------------------------+
bool IsInLossCooldown() {
    if(InpLossCooldownMinutes <= 0) return false;
    if(g_last_loss_time == 0) return false;
    datetime now = TimeCurrent();
    return (now - g_last_loss_time) < (InpLossCooldownMinutes * 60);
}

//+------------------------------------------------------------------+
//| Kiểm tra ngày đã bị dừng (đạt target hoặc chạm max loss)        |
//+------------------------------------------------------------------+
bool IsDayStopped() {
    CheckDailyReset();
    return (g_daily_target_reached || g_daily_loss_reached);
}

//+------------------------------------------------------------------+
//| Đóng toàn bộ vị thế của EA                                      |
//+------------------------------------------------------------------+
void CloseAllPositions(string reason) {
    Print("=> ĐANG THỰC HIỆN ĐÓNG SẠCH TOÀN BỘ VỊ THẾ: ", reason);
    for(int i = PositionsTotal() - 1; i >= 0; i--) {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0) continue;
        if(PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber) {
            if(!trade.PositionClose(ticket))
                Print("Lỗi đóng vị thế #", ticket, " | Error=", GetLastError());
            else
                Print("Đã đóng thành công vị thế #", ticket);
        }
    }
}

//+------------------------------------------------------------------+
//| Kiểm tra ngưỡng chốt lời và cắt lỗ tối đa trong ngày            |
//+------------------------------------------------------------------+
void CheckDailyRiskThresholds() {
    CheckDailyReset();
    double closed = CalculateDailyClosedPnL();
    double floating = GetFloatingPnL();
    double total_net = closed + floating;

    // 1. Kiểm tra Max Daily Loss
    if(InpMaxDailyLoss > 0.0 && !g_daily_loss_reached) {
        if(total_net <= -InpMaxDailyLoss) {
            g_daily_loss_reached = true;
            Print(StringFormat("[DAILY LOSS HIT] Tổng lỗ trong ngày: %.2f USD (Chạm ngưỡng trần lỗ: -%.2f USD). Đóng toàn bộ lệnh và dừng giao dịch!", total_net, InpMaxDailyLoss));
            CloseAllPositions("Chạm ngưỡng cắt lỗ ngày tối đa (-" + DoubleToString(InpMaxDailyLoss, 2) + " USD)");
            g_last_loss_time = TimeCurrent();
            UpdateChartDashboard();
            return;
        }
    }

    // 2. Kiểm tra Daily Target Profit
    if(InpDailyTargetProfit > 0.0 && !g_daily_target_reached) {
        if(total_net >= InpDailyTargetProfit) {
            g_daily_target_reached = true;
            Print(StringFormat("[DAILY TARGET HIT] Tổng lãi trong ngày: %.2f USD (Đạt mục tiêu: +%.2f USD). Chốt toàn bộ lệnh và nghỉ ngơi!", total_net, InpDailyTargetProfit));
            CloseAllPositions("Đạt mục tiêu lợi nhuận ngày (+" + DoubleToString(InpDailyTargetProfit, 2) + " USD)");
            UpdateChartDashboard();
            return;
        }
    }
}

//+------------------------------------------------------------------+
//| Hàm gửi HTTP GET Request                                         |
//+------------------------------------------------------------------+
bool SendHttpGetRequest(string target_url, string &response_str) {
    char post_data[];
    char result_data[];
    string result_headers;
    string headers = "Accept: application/json\r\n";

    int res = WebRequest("GET", target_url, headers, 10000, post_data, result_data, result_headers);
    if(res == 200) {
        response_str = CharArrayToString(result_data, 0, WHOLE_ARRAY, CP_UTF8);
        return true;
    } else {
        Print("[API GET ERROR] HTTP Code: ", res, " | URL: ", target_url, " | Lỗi MT5: ", GetLastError());
        if(res == -1 && GetLastError() == 4014) {
            Print("[API ERROR 4014] LỖI BẢO MẬT MT5: Bạn chưa Allow WebRequest cho URL này trong Tools -> Options -> Expert Advisors.");
        }
        return false;
    }
}

//+------------------------------------------------------------------+
//| Hàm gửi HTTP POST Request                                        |
//+------------------------------------------------------------------+
bool SendHttpPostRequest(string target_url, string post_body, string &response_str) {
    char post_data[];
    char result_data[];
    string result_headers;
    string headers = "Content-Type: application/json\r\nAccept: application/json\r\n";

    StringToCharArray(post_body, post_data, 0, WHOLE_ARRAY, CP_UTF8);
    ArrayResize(post_data, ArraySize(post_data) - 1); // Loại bỏ null-terminator

    int res = WebRequest("POST", target_url, headers, 20000, post_data, result_data, result_headers);
    if(res == 200 || res == 201) {
        response_str = CharArrayToString(result_data, 0, WHOLE_ARRAY, CP_UTF8);
        return true;
    } else {
        Print("[API POST ERROR] HTTP Code: ", res, " | URL: ", target_url, " | Lỗi MT5: ", GetLastError());
        if(ArraySize(result_data) > 0) {
            string err_body = CharArrayToString(result_data, 0, WHOLE_ARRAY, CP_UTF8);
            Print("[API POST ERROR BODY] ", err_body);
        }
        return false;
    }
}

//+------------------------------------------------------------------+
//| Gọi API POST /api/v1/breakout/scan để setup kịch bản mới         |
//+------------------------------------------------------------------+
bool IsScanAttemptAllowed() {
    if(IsDayStopped() || IsInLossCooldown() || HasOpenPositions() || g_is_trading) return false;
    if(InpMinPlanIntervalMinutes > 0 && g_last_scan_attempt_time > 0 &&
       (TimeCurrent() - g_last_scan_attempt_time) < InpMinPlanIntervalMinutes * 60) return false;
    return true;
}

bool TriggerBreakoutScanApi() {
    if(!IsScanAttemptAllowed()) return false;
    datetime now = TimeCurrent();
    g_last_scan_attempt_time = now;

    string scan_url = GetApiUrl(StringFormat(
        "/api/v1/breakout/scan?symbol=%s&interval=%s&limit=%d&maxRiskUsd=%.2f&patternMode=%s&scenarioMode=%s&notify=%s",
        _Symbol, InpScanInterval, InpScanLimit, InpMaxRiskMoney, InpScanPatternMode, InpScanScenarioMode, (InpNotifyGoogleChat ? "true" : "false")));

    Print("--------------------------------------------------");
    Print("=> [BREAKOUT SCAN API] Đang gửi yêu cầu Scan Breakout lên API: ", scan_url);

    string resp = "";
    if(SendHttpPostRequest(scan_url, "{}", resp)) {
        Print("[BREAKOUT SCAN SUCCESS] Đã kích hoạt quét nến và lập kịch bản Breakout mới thành công!");
        g_last_scan_request_time = now;
        // Lấy kết quả ngay thay vì chờ chu kỳ poll
        g_last_check_scenario_time = 0;
        return true;
    }
    return false;
}

//+------------------------------------------------------------------+
//| Xóa toàn bộ kịch bản đang giám sát                               |
//+------------------------------------------------------------------+
void ClearScenarios() {
    g_scenarios_count = 0;
    ArrayResize(g_scenarios, 0);
    g_current_bias = "";
}

//+------------------------------------------------------------------+
//| Parse chuỗi JSON kịch bản - CHỈ NẠP KỊCH BẢN BREAKOUT            |
//+------------------------------------------------------------------+
void ParseAndLoadScenarioJson(string json_str) {
    CJAVal json;
    if(!json.Deserialize(json_str)) {
        Print("[SCENARIO ERROR] Lỗi phân tích cú pháp JSON kịch bản chi tiết!");
        return;
    }

    string symbol_target = json["symbol"].ToStr();
    if(symbol_target != _Symbol && StringFind(_Symbol, symbol_target) < 0) {
        Print("[SCENARIO REJECT] Kịch bản không dành cho cặp tiền này! Cần: ", symbol_target, " | Chart hiện tại: ", _Symbol);
        return;
    }

    g_current_signal_id = json["signal_id"].ToStr();
    g_current_bias = json["bias"].ToStr();
    CJAVal* ca_node = json.FindKey("created_at");
    g_current_plan_created_at = (ca_node != NULL) ? (long)ca_node.ToInt() : (long)TimeGMT();
    CJAVal* vu_node = json.FindKey("valid_until");
    g_plan_valid_until = (vu_node != NULL) ? (long)vu_node.ToInt() : 0;
    g_plan_expiry_logged = false;

    CJAVal* sc_array = json.FindKey("scenarios");
    if(sc_array == NULL || sc_array.Size() == 0) {
        Print("[SCENARIO] Kế hoạch [", g_current_signal_id, "] không có kịch bản Breakout. Tiếp tục theo dõi...");
        ClearScenarios();
        UpdateChartDashboard();
        return;
    }

    if(IsPlanExpired()) {
        Print("[SCENARIO REJECT] Kế hoạch [", g_current_signal_id, "] đã hết hiệu lực (valid_until/created_at quá cũ). Bỏ qua, chờ kế hoạch mới.");
        ClearScenarios();
        UpdateChartDashboard();
        return;
    }

    // Giữ lại kịch bản cũ để chuyển tiếp trạng thái nếu trigger gần như không đổi
    Scenario old_scenarios[];
    int old_count = g_scenarios_count;
    ArrayResize(old_scenarios, old_count);
    for(int k = 0; k < old_count; k++) old_scenarios[k] = g_scenarios[k];

    int raw_count = sc_array.Size();
    int accepted_count = 0;
    Scenario temp_scenarios[];
    ArrayResize(temp_scenarios, raw_count);

    double plan_risk = json["max_risk_usd"].ToDbl();
    if(plan_risk <= 0.0) plan_risk = InpMaxRiskMoney;

    Print("=== [BẮT ĐẦU NẠP KỊCH BẢN BREAKOUT] Signal ID: ", g_current_signal_id, " | Bias: ", g_current_bias, " ===");

    for(int i = 0; i < raw_count; i++) {
        CJAVal* item = sc_array[i];
        string type_str = item["type"].ToStr();

        if(type_str != "BREAKOUT_BUY" && type_str != "BREAKOUT_SELL") {
            Print(StringFormat("[BREAKOUT FILTER] Bỏ qua kịch bản không phải Breakout: %s (ID: %s)", type_str, item["id"].ToStr()));
            continue;
        }

        int idx = accepted_count;
        temp_scenarios[idx].id = item["id"].ToStr();
        temp_scenarios[idx].name = item["name"].ToStr();
        temp_scenarios[idx].type = (type_str == "BREAKOUT_BUY") ? BREAKOUT_BUY : BREAKOUT_SELL;
        temp_scenarios[idx].trigger_price = item["trigger_price"].ToDbl();
        temp_scenarios[idx].sl = item["sl"].ToDbl();
        temp_scenarios[idx].zone_low = item["zone_low"].ToDbl();
        temp_scenarios[idx].zone_high = item["zone_high"].ToDbl();
        temp_scenarios[idx].invalidation = item["invalidation_price"].ToDbl();

        string mode = item["entry_mode"].ToStr();
        StringToUpper(mode);
        temp_scenarios[idx].entry_mode = (mode == "MOMENTUM") ? "MOMENTUM" : "RETEST";

        double sc_risk = item["max_risk_usd"].ToDbl();
        if(sc_risk <= 0.0) sc_risk = plan_risk;
        if(sc_risk > InpMaxRiskMoney) sc_risk = InpMaxRiskMoney;
        temp_scenarios[idx].max_risk_usd = sc_risk;

        // Chỉ dùng 1 TP (TP đầu tiên)
        temp_scenarios[idx].tp = 0.0;
        CJAVal* tps = item.FindKey("tps");
        if(tps != NULL && tps.type == jtARRAY && tps.Size() > 0)
            temp_scenarios[idx].tp = tps[0].ToDbl();

        // Confirmation
        CJAVal* confirm = item.FindKey("confirmation");
        temp_scenarios[idx].confirm_timeframe = StringToTimeframe(InpScanInterval);
        temp_scenarios[idx].patterns_count = 0;
        if(confirm != NULL && confirm.type == jtOBJ) {
            string c_tf_str = confirm["timeframe"].ToStr();
            if(StringLen(c_tf_str) > 0)
                temp_scenarios[idx].confirm_timeframe = StringToTimeframe(c_tf_str);

            CJAVal* patterns = confirm.FindKey("patterns");
            if(patterns != NULL && patterns.type == jtARRAY) {
                for(int p = 0; p < patterns.Size() && temp_scenarios[idx].patterns_count < 5; p++) {
                    string code = NormalizePatternCode(patterns[p].ToStr());
                    if(code != "") temp_scenarios[idx].confirm_patterns[temp_scenarios[idx].patterns_count++] = code;
                }
            }
        }

        // Retest
        CJAVal* retest = item.FindKey("retest");
        temp_scenarios[idx].retest_timeframe = OneStepSmallerTf(temp_scenarios[idx].confirm_timeframe);
        temp_scenarios[idx].retest_patterns_count = 0;
        if(retest != NULL && retest.type == jtOBJ) {
            string r_tf_str = retest["timeframe"].ToStr();
            if(StringLen(r_tf_str) > 0)
                temp_scenarios[idx].retest_timeframe = StringToTimeframe(r_tf_str);

            CJAVal* r_patterns = retest.FindKey("patterns");
            if(r_patterns != NULL && r_patterns.type == jtARRAY) {
                for(int rp = 0; rp < r_patterns.Size() && temp_scenarios[idx].retest_patterns_count < 5; rp++) {
                    string rcode = NormalizePatternCode(r_patterns[rp].ToStr());
                    if(rcode != "") temp_scenarios[idx].retest_patterns[temp_scenarios[idx].retest_patterns_count++] = rcode;
                }
            }
        }

        // Khởi tạo trạng thái ban đầu
        temp_scenarios[idx].status = PENDING;
        temp_scenarios[idx].breakout_confirmed = false;
        temp_scenarios[idx].retest_touched = false;
        temp_scenarios[idx].touch_bar = 0;
        temp_scenarios[idx].retest_wait_bars = 0;
        temp_scenarios[idx].last_bar_time = 0;
        temp_scenarios[idx].last_retest_bar_time = 0;

        // Chuyển tiếp trạng thái từ kịch bản cũ cùng chiều có trigger gần như không đổi
        double tolerance = MathMax(MinBreakDistance(), 0.1 * MathAbs(temp_scenarios[idx].trigger_price - temp_scenarios[idx].sl));
        for(int k = 0; k < old_count; k++) {
            if(old_scenarios[k].status != PENDING || old_scenarios[k].type != temp_scenarios[idx].type) continue;
            if(MathAbs(old_scenarios[k].trigger_price - temp_scenarios[idx].trigger_price) > tolerance) continue;

            temp_scenarios[idx].last_bar_time = old_scenarios[k].last_bar_time;
            if(old_scenarios[k].breakout_confirmed) {
                temp_scenarios[idx].breakout_confirmed = true;
                temp_scenarios[idx].retest_touched = old_scenarios[k].retest_touched;
                temp_scenarios[idx].touch_bar = old_scenarios[k].touch_bar;
                temp_scenarios[idx].retest_wait_bars = old_scenarios[k].retest_wait_bars;
                temp_scenarios[idx].last_retest_bar_time = old_scenarios[k].last_retest_bar_time;
                Print(StringFormat("[SCENARIO CARRY] Giữ trạng thái đã phá vỡ của [%s] sang kịch bản mới [%s] (trigger %.3f -> %.3f).",
                      old_scenarios[k].id, temp_scenarios[idx].id, old_scenarios[k].trigger_price, temp_scenarios[idx].trigger_price));
            }
            break;
        }

        accepted_count++;
    }

    g_scenarios_count = accepted_count;
    ArrayResize(g_scenarios, g_scenarios_count);
    for(int k = 0; k < g_scenarios_count; k++) {
        g_scenarios[k] = temp_scenarios[k];
    }

    Print(StringFormat("=> [NẠP KỊCH BẢN THÀNH CÔNG] Đã nạp %d kịch bản BREAKOUT sẵn sàng giám sát!", g_scenarios_count));
    UpdateChartDashboard();
}

//+------------------------------------------------------------------+
//| Kiểm tra và tải kịch bản Breakout mới từ API (slot riêng)        |
//+------------------------------------------------------------------+
void CheckAndFetchNewScenario() {
    if(IsDayStopped() || IsInLossCooldown()) return;
    if(HasOpenPositions() || g_is_trading) return;

    string latest_url = GetApiUrl("/api/v1/breakout/latest-id?symbol=" + _Symbol);
    string latest_resp = "";
    if(!SendHttpGetRequest(latest_url, latest_resp)) return;

    CJAVal json_latest;
    if(!json_latest.Deserialize(latest_resp)) return;

    string new_signal_id = json_latest["signal_id"].ToStr();
    if(StringLen(new_signal_id) == 0) return;

    CJAVal* hs_node = json_latest.FindKey("has_scenarios");
    bool has_scenarios = (hs_node != NULL) ? hs_node.ToBool() : true;
    CJAVal* nt_node = json_latest.FindKey("no_trade");
    bool no_trade = (nt_node != NULL) ? nt_node.ToBool() : false;

    long new_created_at = 0;
    CJAVal* ca_node = json_latest.FindKey("created_at");
    if(ca_node != NULL) new_created_at = (long)ca_node.ToInt();

    bool is_new_plan = (new_signal_id != g_current_signal_id) ||
                       (new_created_at > 0 && new_created_at != g_current_plan_created_at);
    if(!is_new_plan) return;

    if(!has_scenarios || no_trade) {
        // Kế hoạch mới không có kịch bản: hủy kịch bản cũ, ghi nhận signal để không xử lý lại
        Print("[SCENARIO] Kế hoạch mới [", new_signal_id, "] không có kịch bản Breakout. Hủy kịch bản cũ, tiếp tục theo dõi.");
        g_current_signal_id = new_signal_id;
        g_current_plan_created_at = new_created_at;
        g_plan_valid_until = 0;
        ClearScenarios();
        UpdateChartDashboard();
        return;
    }

    Print("--------------------------------------------------");
    Print(StringFormat("[SCENARIO NEW] Phát hiện kế hoạch Breakout mới: [%s]. Đang tải chi tiết...", new_signal_id));
    string detail_url = GetApiUrl("/api/v1/breakout/plan/" + new_signal_id + "?symbol=" + _Symbol);
    string detail_resp = "";
    if(SendHttpGetRequest(detail_url, detail_resp)) {
        ParseAndLoadScenarioJson(detail_resp);
    }
}

//+------------------------------------------------------------------+
//| Tính khoảng đệm phá vỡ tối thiểu (Min Break Distance)            |
//+------------------------------------------------------------------+
double MinBreakDistance() {
    double tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
    if(tick <= 0.0) tick = _Point;
    double spread = SymbolInfoDouble(_Symbol, SYMBOL_ASK) - SymbolInfoDouble(_Symbol, SYMBOL_BID);
    return MathMax(3.0 * spread, 8.0 * tick);
}

//+------------------------------------------------------------------+
//| Kiểm tra nến mới trên timeframe chỉ định                         |
//+------------------------------------------------------------------+
bool IsNewBarOn(ENUM_TIMEFRAMES tf, datetime &last_time) {
    datetime current_bar = iTime(_Symbol, tf, 0);
    if(current_bar != last_time) {
        last_time = current_bar;
        return true;
    }
    return false;
}

//+------------------------------------------------------------------+
//| Kiểm tra 1 mô hình nến (mã chuẩn) trên nến đã đóng               |
//+------------------------------------------------------------------+
bool CandleMatches(string code, ENUM_TIMEFRAMES tf, int shift, bool is_buy) {
    double o = iOpen(_Symbol, tf, shift);
    double c = iClose(_Symbol, tf, shift);
    double h = iHigh(_Symbol, tf, shift);
    double l = iLow(_Symbol, tf, shift);
    double range = h - l;
    if(range <= 0.0) return false;

    double body = MathAbs(c - o);
    bool dir_ok = is_buy ? (c > o) : (c < o);

    if(code == "MARUBOZU")
        return dir_ok && body >= 0.8 * range;

    if(code == "STRONG_BODY") {
        if(!dir_ok || body < 0.5 * range) return false;
        return is_buy ? (c >= h - range / 3.0) : (c <= l + range / 3.0);
    }

    if(code == "ENGULFING") {
        double po = iOpen(_Symbol, tf, shift + 1);
        double pc = iClose(_Symbol, tf, shift + 1);
        bool prev_opposite = is_buy ? (pc < po) : (pc > po);
        return dir_ok && prev_opposite &&
               MathMax(o, c) >= MathMax(po, pc) && MathMin(o, c) <= MathMin(po, pc);
    }

    if(code == "PINBAR") {
        double upper = h - MathMax(o, c);
        double lower = MathMin(o, c) - l;
        if(is_buy) return lower >= 2.0 * body && lower >= 0.5 * range && c >= l + 0.5 * range;
        return upper >= 2.0 * body && upper >= 0.5 * range && c <= h - 0.5 * range;
    }

    return false;
}

// Nến xác nhận khớp ít nhất 1 mô hình server gửi (rỗng = mặc định STRONG_BODY)
bool ConfirmPatternOk(int index, ENUM_TIMEFRAMES tf, bool is_buy) {
    if(g_scenarios[index].patterns_count == 0)
        return CandleMatches("STRONG_BODY", tf, 1, is_buy);
    for(int p = 0; p < g_scenarios[index].patterns_count; p++) {
        if(CandleMatches(g_scenarios[index].confirm_patterns[p], tf, 1, is_buy)) return true;
    }
    return false;
}

// Nến retest khớp ít nhất 1 mô hình server gửi (rỗng = không lọc)
bool RetestPatternOk(int index, ENUM_TIMEFRAMES tf, bool is_buy) {
    if(g_scenarios[index].retest_patterns_count == 0) return true;
    for(int p = 0; p < g_scenarios[index].retest_patterns_count; p++) {
        if(CandleMatches(g_scenarios[index].retest_patterns[p], tf, 1, is_buy)) return true;
    }
    return false;
}

bool UseMomentumEntry(int index) {
    if(InpEntryMode == ENTRY_MOMENTUM) return true;
    if(InpEntryMode == ENTRY_RETEST) return false;
    return g_scenarios[index].entry_mode == "MOMENTUM";
}

//+------------------------------------------------------------------+
//| Tính Lot tối đa theo khoảng cách SL và mức rủi ro USD           |
//+------------------------------------------------------------------+
double CalcLotsForRisk(ENUM_ORDER_TYPE order_type, double entry, double sl, double max_risk) {
    double sl_dist = MathAbs(entry - sl);
    if(sl_dist < SymbolInfoDouble(_Symbol, SYMBOL_POINT)) return 0.0;

    double loss_1lot = 0.0;
    if(!OrderCalcProfit(order_type, _Symbol, 1.0, entry, sl, loss_1lot)) {
        double tick_size  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
        double tick_value = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
        if(tick_size <= 0.0) return 0.0;
        loss_1lot = -(sl_dist / tick_size) * tick_value;
    }

    double per_lot = MathAbs(loss_1lot);
    if(per_lot <= 0.0) return 0.0;

    double vmin = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    double vmax = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);

    double lots = NormalizeVolume(max_risk / per_lot);
    if(lots < vmin) return 0.0;
    if(lots > vmax) lots = vmax;
    return lots;
}

//+------------------------------------------------------------------+
//| Thực thi vào lệnh Breakout: 1 position, 1 TP                     |
//| Trả về true nếu đã vào lệnh; false nếu chưa vào (vẫn có thể chờ) |
//+------------------------------------------------------------------+
bool ExecuteBreakoutTrade(int index, string reason) {
    Scenario sc = g_scenarios[index];
    bool is_buy = (sc.type == BREAKOUT_BUY);
    ENUM_ORDER_TYPE ot = is_buy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL;
    double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
    double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    double entry = is_buy ? ask : bid;
    double sl = sc.sl;
    double tp = sc.tp;

    if((is_buy && sl >= entry) || (!is_buy && sl <= entry)) {
        Print("[ENTRY SKIP] SL không hợp lệ so với giá hiện tại. Hủy kịch bản [", sc.id, "]");
        g_scenarios[index].status = EXPIRED;
        return false;
    }
    if(tp > 0.0 && ((is_buy && tp <= entry) || (!is_buy && tp >= entry))) {
        Print("[ENTRY SKIP] Giá đã vượt TP trước khi vào lệnh. Hủy kịch bản [", sc.id, "]");
        g_scenarios[index].status = EXPIRED;
        return false;
    }

    double sl_dist = MathAbs(entry - sl);
    double plan_risk_dist = MathAbs(sc.trigger_price - sl);

    // Lọc spread
    double spread = ask - bid;
    if(InpMaxSpreadSlPct > 0.0 && spread > sl_dist * InpMaxSpreadSlPct / 100.0) {
        Print(StringFormat("[ENTRY WAIT] [%s] Spread %.3f > %.0f%% khoảng SL (%.3f). Chờ nến sau.", sc.id, spread, InpMaxSpreadSlPct, sl_dist));
        return false;
    }

    // NO_CHASE: giá vào không được cách trigger quá xa
    if(InpMaxChaseSlPct > 0.0 && plan_risk_dist > 0.0) {
        double chase = is_buy ? (entry - sc.trigger_price) : (sc.trigger_price - entry);
        if(chase > plan_risk_dist * InpMaxChaseSlPct / 100.0) {
            Print(StringFormat("[ENTRY WAIT] [%s] NO_CHASE: giá %.3f cách trigger %.3f quá %.0f%% khoảng SL. Chờ hồi về.", sc.id, entry, sc.trigger_price, InpMaxChaseSlPct));
            return false;
        }
    }

    // Stops level của sàn
    double min_stop = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * _Point;
    if(sl_dist < min_stop || (tp > 0.0 && MathAbs(tp - entry) < min_stop)) {
        Print(StringFormat("[ENTRY WAIT] [%s] SL/TP quá sát giá (stops level %.3f).", sc.id, min_stop));
        return false;
    }

    double max_risk = sc.max_risk_usd > 0.0 ? sc.max_risk_usd : InpMaxRiskMoney;
    if(max_risk > InpMaxRiskMoney) max_risk = InpMaxRiskMoney;

    double lots = CalcLotsForRisk(ot, entry, sl, max_risk);
    if(lots < SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN)) {
        Print("[ENTRY SKIP] Không vào lệnh [", sc.id, "] vì lot tính được < lot tối thiểu của sàn.");
        g_scenarios[index].status = EXPIRED;
        return false;
    }

    string comment = "BO_" + sc.id;
    if(StringLen(comment) > 31) comment = StringSubstr(comment, 0, 31);
    double sl_n = NormalizeDouble(sl, _Digits);
    double tp_n = tp > 0.0 ? NormalizeDouble(tp, _Digits) : 0.0;

    Print(StringFormat("=> [VÀO LỆNH BREAKOUT - %s] %s %s | Lot: %.2f | Entry: %.3f | SL: %.3f | TP: %.3f | Risk: %.2f USD",
          reason, sc.id, (is_buy ? "BUY" : "SELL"), lots, entry, sl_n, tp_n, max_risk));

    bool ok = is_buy ? trade.Buy(lots, _Symbol, 0, sl_n, tp_n, comment)
                     : trade.Sell(lots, _Symbol, 0, sl_n, tp_n, comment);
    uint rc = trade.ResultRetcode();
    if(!ok || (rc != TRADE_RETCODE_DONE && rc != TRADE_RETCODE_PLACED && rc != TRADE_RETCODE_DONE_PARTIAL)) {
        Print("[ENTRY ERROR] Lỗi gửi lệnh: retcode=", rc, " (", trade.ResultRetcodeDescription(), ") | Error=", GetLastError());
        return false;
    }

    // Ghi nhận trạng thái lệnh
    g_pos_ticket = 0;
    g_pos_identifier = 0;
    SelectTradePosition();
    g_is_trading = true;
    g_active_scenario_index = index;
    g_trade_scenario_id = sc.id;
    g_trade_start_time = TimeCurrent();
    g_partial_done = false;
    g_exit_tf = sc.retest_timeframe;
    g_exit_bar_time = iTime(_Symbol, g_exit_tf, 0);
    g_scenarios[index].status = ACTIVE;

    // OCO: chỉ 1 lệnh tại 1 thời điểm, hủy các kịch bản còn lại
    for(int i = 0; i < g_scenarios_count; i++) {
        if(i != index && g_scenarios[i].status == PENDING) {
            g_scenarios[i].status = EXPIRED;
            Print("[OCO] Hủy kịch bản đối ứng [", g_scenarios[i].id, "] vì đã vào lệnh theo [", sc.id, "].");
        }
    }

    UpdateChartDashboard();
    return true;
}

//+------------------------------------------------------------------+
//| Xử lý Logic Kịch Bản Breakout                                    |
//+------------------------------------------------------------------+
void ProcessBreakoutScenario(int index) {
    if(g_scenarios[index].status != PENDING) return;
    if(g_is_trading || HasOpenPositions()) return;

    double trigger = g_scenarios[index].trigger_price;
    bool is_sell = (g_scenarios[index].type == BREAKOUT_SELL);
    bool is_buy = !is_sell;
    ENUM_TIMEFRAMES confirm_tf = g_scenarios[index].confirm_timeframe;
    ENUM_TIMEFRAMES retest_tf  = g_scenarios[index].retest_timeframe;

    // BƯỚC 1: NẾN CONFIRM ĐÓNG PHÁ DỨT KHOÁT QUA TRIGGER
    if(!g_scenarios[index].breakout_confirmed) {
        if(IsPlanExpired()) {
            g_scenarios[index].status = EXPIRED;
            return;
        }
        if(!IsNewBarOn(confirm_tf, g_scenarios[index].last_bar_time)) return;

        double c1 = iClose(_Symbol, confirm_tf, 1);
        double o1 = iOpen(_Symbol, confirm_tf, 1);
        double min_break = MinBreakDistance();
        bool broke = is_sell ? (c1 < trigger - min_break) : (c1 > trigger + min_break);
        if(!broke) return;

        bool dir_ok = is_buy ? (c1 > o1) : (c1 < o1);
        if(!dir_ok) return;

        if(InpUseConfirmPatterns && !ConfirmPatternOk(index, confirm_tf, is_buy)) {
            Print(StringFormat("[BREAKOUT WEAK] Kịch bản [%s]: Nến %s đóng qua trigger nhưng thân nến không đạt mô hình xác nhận. Bỏ qua nến này.",
                  g_scenarios[index].id, TimeframeToString(confirm_tf)));
            return;
        }

        g_scenarios[index].breakout_confirmed = true;
        g_scenarios[index].retest_touched = false;
        g_scenarios[index].touch_bar = 0;
        g_scenarios[index].retest_wait_bars = 0;
        g_scenarios[index].last_retest_bar_time = iTime(_Symbol, retest_tf, 0);

        bool momentum = UseMomentumEntry(index);
        Print(StringFormat("[BREAKOUT CONFIRMED] Kịch bản [%s]: Nến %s đóng phá qua trigger (Close: %.3f, Trigger: %.3f). %s",
              g_scenarios[index].id, TimeframeToString(confirm_tf), c1, trigger,
              (momentum ? "Vào lệnh MOMENTUM..." : "Chờ nến retest...")));

        if(momentum && !ExecuteBreakoutTrade(index, "MOMENTUM") && g_scenarios[index].status == PENDING) {
            Print("[BREAKOUT] Chưa vào được theo MOMENTUM, chuyển sang chờ retest.");
        }
        return;
    }

    // BƯỚC 2: CHỜ NẾN RETEST TRÊN TIMEFRAME NHỎ HƠN
    if(!IsNewBarOn(retest_tf, g_scenarios[index].last_retest_bar_time))
        return;

    g_scenarios[index].retest_wait_bars++;
    if(InpMaxRetestBars > 0 && g_scenarios[index].retest_wait_bars > InpMaxRetestBars) {
        g_scenarios[index].status = EXPIRED;
        Print(StringFormat("[BREAKOUT EXPIRED] Kịch bản [%s] quá %d nến %s sau khi phá vỡ mà chưa có retest hợp lệ.",
              g_scenarios[index].id, InpMaxRetestBars, TimeframeToString(retest_tf)));
        return;
    }

    double c_retest = iClose(_Symbol, retest_tf, 1);
    double h_retest = iHigh(_Symbol, retest_tf, 1);
    double l_retest = iLow(_Symbol, retest_tf, 1);

    // Phá vỡ giả: nến đóng ngược qua mức vô hiệu hóa (mặc định SL nếu server không gửi)
    double invalidation = g_scenarios[index].invalidation > 0.0 ? g_scenarios[index].invalidation : g_scenarios[index].sl;
    bool invalidated = is_buy ? (c_retest < invalidation) : (c_retest > invalidation);
    if(invalidated) {
        g_scenarios[index].status = EXPIRED;
        Print(StringFormat("[FALSE BREAKOUT] Kịch bản [%s]: Nến %s đóng %.3f quay ngược qua mức vô hiệu hóa %.3f. Hủy kịch bản.",
              g_scenarios[index].id, TimeframeToString(retest_tf), c_retest, invalidation));
        return;
    }

    bool touched_now = is_sell ? (h_retest >= trigger) : (l_retest <= trigger);
    if(touched_now) {
        if(!g_scenarios[index].retest_touched)
            Print(StringFormat("[BREAKOUT RETEST TOUCH] Kịch bản [%s]: Nến %s đã hồi chạm lại trigger %.3f.",
                  g_scenarios[index].id, TimeframeToString(retest_tf), trigger));
        g_scenarios[index].retest_touched = true;
        g_scenarios[index].touch_bar = g_scenarios[index].retest_wait_bars;
    }

    // Chỉ vào khi lần chạm còn "mới" (ngay nến chạm hoặc nến kế tiếp), tránh vào khi giá đã chạy xa
    bool recent_touch = g_scenarios[index].retest_touched &&
                        (g_scenarios[index].retest_wait_bars - g_scenarios[index].touch_bar) <= 1;
    bool still_broken = is_sell ? (c_retest < trigger) : (c_retest > trigger);
    bool pattern_ok = !InpUseRetestPatterns || RetestPatternOk(index, retest_tf, is_buy);

    if(recent_touch && still_broken && pattern_ok) {
        Print(StringFormat("[BREAKOUT RETEST OK] Kịch bản [%s] retest thành công trên %s. Kích hoạt vào lệnh!",
              g_scenarios[index].id, TimeframeToString(retest_tf)));
        ExecuteBreakoutTrade(index, "RETEST");
    }
}

//+------------------------------------------------------------------+
//| Nến khung nhỏ đảo chiều ngược hướng lệnh                         |
//+------------------------------------------------------------------+
bool IsReversalBar(ENUM_TIMEFRAMES tf, bool position_is_buy) {
    double o = iOpen(_Symbol, tf, 1);
    double c = iClose(_Symbol, tf, 1);
    if(position_is_buy) {
        if(c < o && c < iLow(_Symbol, tf, 2)) return true;
    } else {
        if(c > o && c > iHigh(_Symbol, tf, 2)) return true;
    }
    return CandleMatches("ENGULFING", tf, 1, !position_is_buy);
}

//+------------------------------------------------------------------+
//| Dời SL về hòa vốn + offset (nếu hợp lệ và tốt hơn SL hiện tại)   |
//+------------------------------------------------------------------+
void MoveStopToBreakeven(ulong ticket) {
    if(!PositionSelectByTicket(ticket)) return;
    double open_p = PositionGetDouble(POSITION_PRICE_OPEN);
    double cur_sl = PositionGetDouble(POSITION_SL);
    double cur_tp = PositionGetDouble(POSITION_TP);
    bool is_buy = (PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY);
    double offset = InpBreakevenOffset * _Point;
    double min_stop = (double)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL) * _Point;
    double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

    double target = NormalizeDouble(is_buy ? open_p + offset : open_p - offset, _Digits);
    bool valid = is_buy ? (target < bid - min_stop) : (target > ask + min_stop);
    bool better = is_buy ? (cur_sl < target - _Point) : (cur_sl == 0.0 || cur_sl > target + _Point);
    if(!valid || !better) return;

    if(trade.PositionModify(ticket, target, cur_tp))
        Print("[BREAKEVEN MOVED] Đã dời SL lệnh #", ticket, " về hòa vốn ", DoubleToString(target, _Digits));
    else
        Print("[BREAKEVEN ERROR] Không dời được SL lệnh #", ticket, " | retcode=", trade.ResultRetcode());
}

//+------------------------------------------------------------------+
//| Kết thúc 1 lệnh: tổng kết PnL, cooldown, xin kịch bản mới        |
//+------------------------------------------------------------------+
void FinishTrade() {
    double net = 0.0;
    bool found = false;
    if(g_pos_identifier > 0 && HistorySelectByPosition(g_pos_identifier)) {
        for(int i = 0; i < HistoryDealsTotal(); i++) {
            ulong d = HistoryDealGetTicket(i);
            if(d == 0) continue;
            net += HistoryDealGetDouble(d, DEAL_PROFIT)
                 + HistoryDealGetDouble(d, DEAL_COMMISSION)
                 + HistoryDealGetDouble(d, DEAL_SWAP);
            found = true;
        }
    }
    // Dự phòng khi chưa xác định được position id: cộng các deal của EA từ lúc vào lệnh
    if(!found && g_trade_start_time > 0 && HistorySelect(g_trade_start_time - 5, TimeCurrent() + 60)) {
        for(int i = 0; i < HistoryDealsTotal(); i++) {
            ulong d = HistoryDealGetTicket(i);
            if(d == 0) continue;
            if(HistoryDealGetInteger(d, DEAL_MAGIC) != InpMagicNumber || HistoryDealGetString(d, DEAL_SYMBOL) != _Symbol) continue;
            net += HistoryDealGetDouble(d, DEAL_PROFIT)
                 + HistoryDealGetDouble(d, DEAL_COMMISSION)
                 + HistoryDealGetDouble(d, DEAL_SWAP);
            found = true;
        }
    }
    g_history_dirty = true;

    if(g_active_scenario_index >= 0 && g_active_scenario_index < g_scenarios_count &&
       g_scenarios[g_active_scenario_index].id == g_trade_scenario_id) {
        g_scenarios[g_active_scenario_index].status = COMPLETED;
    }

    Print(StringFormat("[TRADE CLOSED] Kịch bản [%s] kết thúc. PnL ròng: %s", g_trade_scenario_id,
          (found ? DoubleToString(net, 2) + " USD" : "không xác định")));

    if(found && net < 0.0 && InpLossCooldownMinutes > 0) {
        g_last_loss_time = TimeCurrent();
        Print("[COOLDOWN ACTIVATED] Lệnh thua ròng. Nghỉ ", InpLossCooldownMinutes, " phút trước khi giao dịch tiếp.");
    }

    g_is_trading = false;
    g_active_scenario_index = -1;
    g_trade_scenario_id = "";
    g_pos_ticket = 0;
    g_pos_identifier = 0;
    g_trade_start_time = 0;
    g_partial_done = false;
    g_force_scan = true;   // Xin kịch bản mới ngay khi được phép
    UpdateChartDashboard();
}

//+------------------------------------------------------------------+
//| Khôi phục trạng thái khi EA khởi động lại giữa lệnh              |
//+------------------------------------------------------------------+
void RecoverOpenTrade() {
    if(!SelectTradePosition()) return;
    double open_p = PositionGetDouble(POSITION_PRICE_OPEN);
    double cur_sl = PositionGetDouble(POSITION_SL);
    bool is_buy = (PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY);

    g_is_trading = true;
    g_trade_start_time = (datetime)PositionGetInteger(POSITION_TIME);
    g_trade_scenario_id = PositionGetString(POSITION_COMMENT);
    // SL đã ở hòa vốn trở lên => coi như đã chốt một phần
    g_partial_done = (cur_sl > 0.0) && (is_buy ? cur_sl >= open_p : cur_sl <= open_p);
    g_exit_tf = OneStepSmallerTf(StringToTimeframe(InpScanInterval));
    g_exit_bar_time = iTime(_Symbol, g_exit_tf, 0);
    Print("[RECOVER] Phát hiện lệnh đang mở #", g_pos_ticket, " (", g_trade_scenario_id, "). Tiếp tục quản lý lệnh.");
}

//+------------------------------------------------------------------+
//| Quản lý vị thế: chốt một phần, hòa vốn, thoát đảo chiều          |
//+------------------------------------------------------------------+
void ManageOpenPositions() {
    if(!HasOpenPositions()) {
        if(g_is_trading) FinishTrade();
        return;
    }
    if(!g_is_trading) RecoverOpenTrade();
    if(!SelectTradePosition()) return;

    ulong  ticket = g_pos_ticket;
    double volume = PositionGetDouble(POSITION_VOLUME);
    double open_p = PositionGetDouble(POSITION_PRICE_OPEN);
    bool   is_buy = (PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY);
    double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
    double profit_points = (is_buy ? (bid - open_p) : (open_p - ask)) / _Point;

    // 1. Chốt một phần khi lãi >= InpPartialTriggerPoints
    if(!g_partial_done && InpPartialTriggerPoints > 0 && InpPartialClosePercent > 0.0 &&
       profit_points >= InpPartialTriggerPoints) {
        double vmin = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
        if(InpPartialClosePercent >= 100.0) {
            if(trade.PositionClose(ticket))
                Print(StringFormat("[PARTIAL] Lãi %.0f points >= %d: chốt toàn bộ lệnh #%I64u.", profit_points, InpPartialTriggerPoints, ticket));
            return;
        }

        double close_vol = NormalizeVolume(volume * InpPartialClosePercent / 100.0);
        double remain = volume - close_vol;
        if(close_vol >= vmin && remain >= vmin - 1e-8) {
            if(trade.PositionClosePartial(ticket, close_vol)) {
                Print(StringFormat("[PARTIAL] Lãi %.0f points >= %d: đã chốt %.2f lot (%.0f%%) lệnh #%I64u, còn %.2f lot chạy tới TP.",
                      profit_points, InpPartialTriggerPoints, close_vol, InpPartialClosePercent, ticket, remain));
                g_partial_done = true;
            } else {
                Print("[PARTIAL ERROR] Không chốt được một phần lệnh #", ticket, " | retcode=", trade.ResultRetcode());
            }
        } else {
            Print(StringFormat("[PARTIAL] Volume %.2f quá nhỏ để chốt %.0f%% (lot tối thiểu %.2f). Giữ nguyên volume.", volume, InpPartialClosePercent, vmin));
            g_partial_done = true;
        }

        if(g_partial_done && InpMoveSlToBeAfterPartial)
            MoveStopToBreakeven(ticket);
        return;
    }

    // 2. Sau khi chốt một phần: thoát phần còn lại nếu nến khung nhỏ đảo chiều
    if(g_partial_done && InpEnableSmallTfExit && IsNewBarOn(g_exit_tf, g_exit_bar_time)) {
        if(!PositionSelectByTicket(ticket)) return;
        double pnl = PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
        if(IsReversalBar(g_exit_tf, is_buy) && (!InpSmallTfExitInProfitOnly || pnl > 0.0)) {
            if(trade.PositionClose(ticket))
                Print(StringFormat("[SMALL TF EXIT] Nến %s đảo chiều, đóng phần còn lại lệnh #%I64u (PnL %.2f USD).",
                      TimeframeToString(g_exit_tf), ticket, pnl));
        }
    }
}

//+------------------------------------------------------------------+
//| Bảng Dashboard trực quan trên màn hình                           |
//+------------------------------------------------------------------+
void UpdateChartDashboard() {
    string text = "=======================================================\n";
    text += "       🔥 BREAKOUT STRATEGY TRADER v2 (AI / QUANT) 🔥       \n";
    text += "=======================================================\n";
    text += StringFormat("Symbol: %s | Magic: %d | Rủi ro: %.2f USD | Slippage: %d pts\n", _Symbol, InpMagicNumber, InpMaxRiskMoney, InpSlippage);
    text += StringFormat("API Scan: %s (%s, Limit: %d) | Chế độ: [%s / %s]\n", InpApiBaseUrl, InpScanInterval, InpScanLimit, InpScanPatternMode, InpScanScenarioMode);
    text += StringFormat("Chốt một phần: %s\n", (InpPartialTriggerPoints > 0
            ? StringFormat("lãi >= %d pts -> chốt %.0f%% vol%s", InpPartialTriggerPoints, InpPartialClosePercent, (InpMoveSlToBeAfterPartial ? " + dời SL hòa vốn" : ""))
            : "TẮT"));
    text += "-------------------------------------------------------\n";

    double closed_pnl = CalculateDailyClosedPnL();
    double float_pnl = GetFloatingPnL();
    double net_pnl = closed_pnl + float_pnl;

    text += StringFormat("PnL Ngày: %.2f USD (Đã đóng: %.2f | Đang chạy: %.2f)\n", net_pnl, closed_pnl, float_pnl);
    if(InpDailyTargetProfit > 0)
        text += StringFormat("Mục tiêu Lãi Ngày: +%.2f USD %s\n", InpDailyTargetProfit, (g_daily_target_reached ? "✅ [ĐẠT MỤC TIÊU - NGHỈ]" : ""));
    if(InpMaxDailyLoss > 0)
        text += StringFormat("Trần Lỗ Ngày: -%.2f USD %s\n", InpMaxDailyLoss, (g_daily_loss_reached ? "🛑 [CHẠM TRẦN LỖ - DỪNG]" : ""));

    if(IsInLossCooldown()) {
        int rem = (int)((InpLossCooldownMinutes * 60) - (TimeCurrent() - g_last_loss_time));
        text += StringFormat("⏳ Cooldown sau lệnh thua: CÒN %d PHÚT %d GIÂY\n", rem / 60, rem % 60);
    }
    if(g_is_trading)
        text += StringFormat("🔥 Đang trong lệnh: %s | Đã chốt một phần: %s\n", g_trade_scenario_id, (g_partial_done ? "CÓ" : "CHƯA"));

    text += "-------------------------------------------------------\n";
    if(g_scenarios_count == 0) {
        text += "Trạng thái Kịch bản: CHƯA CÓ KỊCH BẢN BREAKOUT NÀO.\n";
        text += "(Đang chờ lệnh Scan hoặc chu kỳ tự động quét từ API...)\n";
    } else {
        string validity = (g_plan_valid_until > 0)
            ? TimeToString((datetime)g_plan_valid_until, TIME_DATE | TIME_MINUTES) + " UTC" + (IsPlanExpired() ? " (HẾT HẠN)" : "")
            : "không giới hạn";
        text += StringFormat("Kịch bản Breakout (%d | Signal: %s | Hiệu lực tới: %s):\n", g_scenarios_count, g_current_signal_id, validity);
        for(int i = 0; i < g_scenarios_count; i++) {
            Scenario sc = g_scenarios[i];
            string type_str = (sc.type == BREAKOUT_BUY) ? "BUY BREAKOUT" : "SELL BREAKOUT";
            string st_str = "PENDING (Chờ nến xác nhận)";
            if(sc.status == ACTIVE) st_str = "🔥 ĐANG TRONG LỆNH";
            else if(sc.status == COMPLETED) st_str = "✅ HOÀN THÀNH";
            else if(sc.status == EXPIRED) st_str = "❌ HẾT HIỆU LỰC / ĐÃ HỦY";
            else if(sc.breakout_confirmed) st_str = "⏳ ĐÃ PHÁ VỠ - CHỜ RETEST";

            text += StringFormat(" [%d] %s - %s (%s)\n", i + 1, sc.id, type_str, (UseMomentumEntry(i) ? "MOMENTUM" : "RETEST"));
            text += StringFormat("     Trigger: %.3f | SL: %.3f | TP: %.3f | Vô hiệu: %.3f\n", sc.trigger_price, sc.sl, sc.tp, sc.invalidation);
            text += StringFormat("     Trạng thái: %s\n", st_str);
        }
    }
    text += "=======================================================";
    Comment(text);
}

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit() {
    trade.SetExpertMagicNumber(InpMagicNumber);
    trade.SetDeviationInPoints(InpSlippage);
    trade.SetTypeFillingBySymbol(_Symbol);

    if(AccountInfoInteger(ACCOUNT_MARGIN_MODE) != ACCOUNT_MARGIN_MODE_RETAIL_HEDGING)
        Print("[INIT] Tài khoản NETTING: EA dùng 1 position/lệnh nên vẫn hoạt động, nhưng không chạy chung symbol với EA/lệnh tay khác.");

    CheckDailyReset();
    CalculateDailyClosedPnL();

    // Khôi phục nếu EA khởi động lại giữa lệnh
    if(HasOpenPositions()) RecoverOpenTrade();

    UpdateChartDashboard();

    if(InpRequestScanOnStart) {
        Print("=> [INIT] Kích hoạt gửi yêu cầu Scan Breakout ban đầu lên API...");
        TriggerBreakoutScanApi();
    }

    CheckAndFetchNewScenario();

    EventSetTimer(1);
    return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason) {
    EventKillTimer();
    Comment("");
}

//+------------------------------------------------------------------+
//| Có giao dịch mới -> đánh dấu cần tính lại PnL ngày               |
//+------------------------------------------------------------------+
void OnTrade() {
    g_history_dirty = true;
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick() {
    CheckDailyRiskThresholds();
    ManageOpenPositions();

    if(IsDayStopped() || IsInLossCooldown()) return;
    if(g_is_trading || HasOpenPositions()) return;

    for(int i = 0; i < g_scenarios_count; i++) {
        if(g_scenarios[i].status == PENDING) {
            ProcessBreakoutScenario(i);
            if(g_is_trading) break;   // OCO: đã vào 1 lệnh thì dừng xét các kịch bản khác
        }
    }
}

//+------------------------------------------------------------------+
//| Timer: poll kịch bản, auto scan, rủi ro ngày, dashboard          |
//+------------------------------------------------------------------+
void OnTimer() {
    datetime now = TimeCurrent();

    // 1. Rủi ro ngày & hết hạn kế hoạch
    CheckDailyRiskThresholds();
    ExpireStalePendingScenarios();

    // 2. Poll kịch bản mới
    if(now - g_last_check_scenario_time >= InpCheckScenarioSec) {
        g_last_check_scenario_time = now;
        CheckAndFetchNewScenario();
    }

    // 3. Tự động xin kịch bản mới: theo chu kỳ, hoặc ngay sau khi đóng lệnh
    if(InpAutoScanIntervalMinutes > 0 && !g_is_trading && !HasOpenPositions() && !IsDayStopped() && !IsInLossCooldown()) {
        bool due = g_force_scan || (now - g_last_scan_request_time >= (InpAutoScanIntervalMinutes * 60));
        if(due && IsAllScenariosFinished() && IsScanAttemptAllowed()) {
            Print("=> [AUTO SCAN] ", (g_force_scan ? "Lệnh vừa đóng" : "Tới chu kỳ quét tự động"), ". Gửi yêu cầu scan kịch bản mới...");
            if(TriggerBreakoutScanApi()) g_force_scan = false;
        }
    }

    // 4. Cập nhật Dashboard
    UpdateChartDashboard();
}
