//+------------------------------------------------------------------+
//|                                             BreakoutTrader.mq5   |
//|                                  Trading Intelligence Engine     |
//|               Chuyên Giao Dịch Breakout Theo Kịch Bản API        |
//+------------------------------------------------------------------+
#property copyright "Trading Intelligence Engine"
#property link      "https://github.com/tuyennv38/trading-intelligence-engine"
#property version   "1.00"
#property description "EA chuyên giao dịch Breakout theo kịch bản từ Trading Intelligence Engine API"

#include <Trade\Trade.mqh>
#include <JAson.mqh>

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

//====================================================================
// 2. QUẢN LÝ RỦI RO NGÀY (DAILY RISK MANAGEMENT)
//====================================================================
input group "=== [2] QUẢN LÝ RỦI RO LÃI/LỖ THEO NGÀY ==="
input double InpDailyTargetProfit          = 100.0;            // Mục tiêu chốt lời ngày (USD, 0 = tắt). Đạt mức này sẽ chốt hết lệnh và nghỉ
input double InpMaxDailyLoss               = 200.0;            // Ngưỡng cắt lỗ tối đa 1 ngày (USD, 0 = tắt). Chạm mức này đóng hết lệnh và nghỉ
input int    InpLossCooldownMinutes        = 30;               // Thời gian nghỉ sau khi bị SL/thua lỗ (phút, 0 = tắt). Tránh giao dịch trả thù
input int    InpMinPlanIntervalMinutes     = 10;               // Giãn cách tối thiểu giữa 2 lần xin kịch bản mới (phút)

//====================================================================
// 3. CÀI ĐẶT GIAO DỊCH BREAKOUT
//====================================================================
input group "=== [3] CÀI ĐẶT GIAO DỊCH BREAKOUT ==="
input double InpMaxRiskMoney               = 50.0;             // Rủi ro tối đa 1 lần vào lệnh Breakout (USD). Lot tính chuẩn từ SL
input ulong  InpMagicNumber                = 888999;           // Magic Number riêng cho Breakout Trader
input int    InpSlippage                   = 10;               // Độ trượt giá tối đa (Slippage)
input int    InpBreakevenOffset            = 20;               // Bù trừ hòa vốn (Points) khi dời SL sau TP1
input int    InpCheckScenarioSec           = 60;               // Chu kỳ gọi API kiểm tra kịch bản mới (giây, mặc định 60s = 1 phút)
input int    InpMaxRetestBars              = 12;               // Tối đa số nến chờ retest sau phá vỡ (0 = không giới hạn)
input bool   InpRequireRetest              = true;             // Bắt buộc chờ nến retest pullback về trigger rồi mới vào lệnh
input bool   InpEnableSmallTfExit          = true;             // Thoát sớm khi nến khung nhỏ đảo chiều trước khi tới TP2
input bool   InpSmallTfExitInProfitOnly    = true;             // Chỉ thoát đảo chiều khung nhỏ khi tổng vị thế đang có lãi dương
input bool   InpLockTp1ToHalfOnTp1Hit      = true;             // Khóa lãi: Kéo SL lệnh TP2 lên mốc 50% TP1 khi TP1 đã cắn

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
    double               max_risk_usd;
    double               tps[3];
    int                  tps_count;
    
    string               confirm_condition;
    ENUM_TIMEFRAMES      confirm_timeframe;
    string               confirm_patterns[5];
    int                  patterns_count;

    ENUM_TIMEFRAMES      retest_timeframe;
    string               retest_condition;
    string               retest_patterns[5];
    int                  retest_patterns_count;
    
    ENUM_SCENARIO_STATUS status;
    bool                 breakout_confirmed;   // Nến confirm đã đóng phá qua trigger
    bool                 retest_touched;       // Giá đã hồi chạm lại trigger
    int                  retest_wait_bars;     // Số nến đã chờ retest
    datetime             last_bar_time;
    datetime             last_retest_bar_time;
    double               filled_lots;
    bool                 tp1_hit;
    bool                 sl_moved_to_be;
};

// Quản lý kịch bản
Scenario g_scenarios[];
int      g_scenarios_count = 0;
string   g_current_signal_id = "";
string   g_current_bias = "";
long     g_current_plan_created_at = 0;
datetime g_last_check_scenario_time = 0;
datetime g_last_scan_request_time = 0;
bool     g_is_trading = false;
datetime g_trade_start_time = 0;
int      g_active_scenario_index = -1;

// Quản lý lãi/lỗ theo ngày
datetime g_current_day_start = 0;
double   g_daily_closed_pnl = 0.0;
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
        g_daily_target_reached = false;
        g_daily_loss_reached = false;
        Print(StringFormat("[DAILY RESET] Bắt đầu ngày giao dịch mới: %s. Reset chỉ số PnL ngày.", TimeToString(today_start, TIME_DATE)));
    }
}

//+------------------------------------------------------------------+
//| Tính PnL các lệnh đã đóng trong ngày hôm nay                    |
//+------------------------------------------------------------------+
double CalculateDailyClosedPnL() {
    CheckDailyReset();
    HistorySelect(g_current_day_start, TimeCurrent() + 60);
    double total = 0.0;
    for(int i = 0; i < HistoryDealsTotal(); i++) {
        ulong ticket = HistoryDealGetTicket(i);
        if(HistoryDealGetInteger(ticket, DEAL_MAGIC) == InpMagicNumber && HistoryDealGetString(ticket, DEAL_SYMBOL) == _Symbol) {
            long entry = HistoryDealGetInteger(ticket, DEAL_ENTRY);
            if(entry == DEAL_ENTRY_OUT || entry == DEAL_ENTRY_INOUT) {
                total += HistoryDealGetDouble(ticket, DEAL_PROFIT) 
                       + HistoryDealGetDouble(ticket, DEAL_COMMISSION) 
                       + HistoryDealGetDouble(ticket, DEAL_SWAP);
            }
        }
    }
    g_daily_closed_pnl = total;
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
bool TriggerBreakoutScanApi() {
    if(IsDayStopped() || IsInLossCooldown() || HasOpenPositions()) {
        return false;
    }

    string scan_url = GetApiUrl(StringFormat(
        "/api/v1/breakout/scan?symbol=%s&interval=%s&limit=%d&maxRiskUsd=%.2f&patternMode=%s&scenarioMode=%s&notify=%s",
        _Symbol, InpScanInterval, InpScanLimit, InpMaxRiskMoney, InpScanPatternMode, InpScanScenarioMode, (InpNotifyGoogleChat ? "true" : "false")));

    Print("--------------------------------------------------");
    Print("=> [BREAKOUT SCAN API] Đang gửi yêu cầu Scan Breakout lên API: ", scan_url);

    string resp = "";
    if(SendHttpPostRequest(scan_url, "{}", resp)) {
        Print("[BREAKOUT SCAN SUCCESS] Đã kích hoạt quét nến và lập kịch bản Breakout mới thành công!");
        g_last_scan_request_time = TimeCurrent();
        return true;
    }
    return false;
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

    CJAVal* sc_array = json["scenarios"];
    if(sc_array == NULL || sc_array.Size() == 0) {
        Print("[SCENARIO] Mảng 'scenarios' rỗng. Tiếp tục theo dõi...");
        g_scenarios_count = 0;
        ArrayResize(g_scenarios, 0);
        g_current_bias = "";
        UpdateChartDashboard();
        return;
    }

    g_current_signal_id = json["signal_id"].ToStr();
    g_current_bias = json["bias"].ToStr();
    CJAVal* ca_node = json.FindKey("created_at");
    if(ca_node != NULL) g_current_plan_created_at = (long)ca_node.ToInt();
    else g_current_plan_created_at = (long)TimeCurrent();

    int raw_count = sc_array.Size();
    int accepted_count = 0;
    Scenario temp_scenarios[];
    ArrayResize(temp_scenarios, raw_count);

    Print("=== [BẮT ĐẦU NẠP KỊCH BẢN BREAKOUT] Signal ID: ", g_current_signal_id, " | Bias: ", g_current_bias, " ===");

    for(int i = 0; i < raw_count; i++) {
        CJAVal* item = sc_array[i];
        string type_str = item["type"].ToStr();

        // BỘ LỌC ĐẶC BIỆT: CHỈ CHẤP NHẬN KỊCH BẢN BREAKOUT
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

        double plan_risk = json["max_risk_usd"].ToDbl();
        if(plan_risk <= 0.0) plan_risk = InpMaxRiskMoney;
        double sc_risk = item["max_risk_usd"].ToDbl();
        if(sc_risk <= 0.0) sc_risk = plan_risk;
        if(sc_risk > InpMaxRiskMoney) sc_risk = InpMaxRiskMoney;
        temp_scenarios[idx].max_risk_usd = sc_risk;

        // Take Profits (tối đa 2 TP)
        CJAVal* tps = item["tps"];
        temp_scenarios[idx].tps_count = 0;
        if(tps != NULL && tps.type == jtARRAY) {
            int tp_size = MathMin(2, tps.Size());
            for(int t = 0; t < tp_size; t++) {
                temp_scenarios[idx].tps[t] = tps[t].ToDbl();
                temp_scenarios[idx].tps_count++;
            }
        }

        // Confirmation
        CJAVal* confirm = item.FindKey("confirmation");
        temp_scenarios[idx].confirm_timeframe = StringToTimeframe(InpScanInterval);
        temp_scenarios[idx].patterns_count = 0;
        if(confirm != NULL && confirm.type == jtOBJ) {
            temp_scenarios[idx].confirm_condition = confirm["condition"].ToStr();
            string c_tf_str = confirm["timeframe"].ToStr();
            if(StringLen(c_tf_str) > 0)
                temp_scenarios[idx].confirm_timeframe = StringToTimeframe(c_tf_str);

            CJAVal* patterns = confirm.FindKey("patterns");
            if(patterns != NULL && patterns.type == jtARRAY) {
                int p_size = MathMin(5, patterns.Size());
                for(int p = 0; p < p_size; p++) {
                    string pat = patterns[p].ToStr();
                    StringToUpper(pat);
                    temp_scenarios[idx].confirm_patterns[p] = pat;
                    temp_scenarios[idx].patterns_count++;
                }
            }
        }

        // Retest
        CJAVal* retest = item.FindKey("retest");
        temp_scenarios[idx].retest_timeframe = OneStepSmallerTf(temp_scenarios[idx].confirm_timeframe);
        temp_scenarios[idx].retest_patterns_count = 0;
        if(retest != NULL && retest.type == jtOBJ) {
            temp_scenarios[idx].retest_condition = retest["condition"].ToStr();
            string r_tf_str = retest["timeframe"].ToStr();
            if(StringLen(r_tf_str) > 0)
                temp_scenarios[idx].retest_timeframe = StringToTimeframe(r_tf_str);

            CJAVal* r_patterns = retest.FindKey("patterns");
            if(r_patterns != NULL && r_patterns.type == jtARRAY) {
                int rp_size = MathMin(5, r_patterns.Size());
                for(int rp = 0; rp < rp_size; rp++) {
                    string rpat = r_patterns[rp].ToStr();
                    StringToUpper(rpat);
                    temp_scenarios[idx].retest_patterns[rp] = rpat;
                    temp_scenarios[idx].retest_patterns_count++;
                }
            }
        }

        // Khởi tạo trạng thái ban đầu
        temp_scenarios[idx].status = PENDING;
        temp_scenarios[idx].breakout_confirmed = false;
        temp_scenarios[idx].retest_touched = false;
        temp_scenarios[idx].retest_wait_bars = 0;
        temp_scenarios[idx].last_bar_time = 0;
        temp_scenarios[idx].last_retest_bar_time = 0;
        temp_scenarios[idx].filled_lots = 0.0;
        temp_scenarios[idx].tp1_hit = false;
        temp_scenarios[idx].sl_moved_to_be = false;

        accepted_count++;
    }

    g_scenarios_count = accepted_count;
    ArrayResize(g_scenarios, g_scenarios_count);
    for(int k = 0; k < g_scenarios_count; k++) {
        g_scenarios[k] = temp_scenarios[k];
    }

    Print(StringFormat("=> [NẠP KỊCH BẢN THÀNH CÔNG] Đã lọc và nạp %d kịch bản BREAKOUT sẵn sàng giám sát!", g_scenarios_count));
    UpdateChartDashboard();
}

//+------------------------------------------------------------------+
//| Kiểm tra và tải kịch bản mới từ API (Poll latest-id mỗi phút)   |
//+------------------------------------------------------------------+
void CheckAndFetchNewScenario() {
    if(IsDayStopped()) {
        Print("[SCENARIO SKIP] Đã chạm ngưỡng mục tiêu/lỗ ngày. Tạm dừng kiểm tra kịch bản.");
        return;
    }
    if(IsInLossCooldown()) {
        int rem = (int)((InpLossCooldownMinutes * 60) - (TimeCurrent() - g_last_loss_time));
        Print(StringFormat("[SCENARIO SKIP] Đang trong thời gian nghỉ sau lệnh thua (còn %d phút %ds).", rem / 60, rem % 60));
        return;
    }
    if(HasOpenPositions() || g_is_trading) {
        return;
    }

    string latest_url = GetApiUrl("/api/v1/scenarios/latest-id?symbol=" + _Symbol);
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

    if(!has_scenarios || no_trade) {
        if(g_scenarios_count > 0 && new_signal_id != g_current_signal_id) {
            g_scenarios_count = 0;
            ArrayResize(g_scenarios, 0);
            g_current_signal_id = "";
            UpdateChartDashboard();
        }
        return;
    }

    long new_created_at = 0;
    CJAVal* ca_node = json_latest.FindKey("created_at");
    if(ca_node != NULL) new_created_at = (long)ca_node.ToInt();

    bool is_new_plan = (new_signal_id != g_current_signal_id) || 
                       (new_created_at > 0 && new_created_at != g_current_plan_created_at) || 
                       (g_scenarios_count == 0);

    if(is_new_plan) {
        Print("--------------------------------------------------");
        Print(StringFormat("[SCENARIO NEW] Phát hiện kịch bản mới: [%s]. Đang tải chi tiết...", new_signal_id));
        string detail_url = GetApiUrl("/api/v1/scenarios/" + new_signal_id + "?symbol=" + _Symbol);
        string detail_resp = "";
        if(SendHttpGetRequest(detail_url, detail_resp)) {
            ParseAndLoadScenarioJson(detail_resp);
            if(new_created_at > 0) g_current_plan_created_at = new_created_at;
        }
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
    double step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
    if(step <= 0.0) step = 0.01;

    double lots = MathFloor((max_risk / per_lot) / step) * step;
    lots = NormalizeDouble(lots, 2);

    if(lots < vmin) return 0.0;
    if(lots > vmax) lots = vmax;
    return lots;
}

//+------------------------------------------------------------------+
//| Thực thi vào lệnh Breakout (2 TP Model)                          |
//+------------------------------------------------------------------+
void ExecuteBreakoutTrade(int index) {
    Scenario sc = g_scenarios[index];
    bool is_buy = (sc.type == BREAKOUT_BUY);
    ENUM_ORDER_TYPE ot = is_buy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL;
    double entry = is_buy ? SymbolInfoDouble(_Symbol, SYMBOL_ASK) : SymbolInfoDouble(_Symbol, SYMBOL_BID);
    double sl = sc.sl;

    if((is_buy && sl >= entry) || (!is_buy && sl <= entry)) {
        Print("CẢNH BÁO: SL không hợp lệ với vị thế. Bỏ qua kịch bản [", sc.id, "]");
        g_scenarios[index].status = EXPIRED;
        return;
    }

    double max_risk = sc.max_risk_usd > 0.0 ? sc.max_risk_usd : InpMaxRiskMoney;
    if(max_risk > InpMaxRiskMoney) max_risk = InpMaxRiskMoney;

    double total_lots = CalcLotsForRisk(ot, entry, sl, max_risk);
    double vmin = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    if(total_lots < vmin) {
        Print("CẢNH BÁO: Không vào lệnh [", sc.id, "] vì lot tính được < lot tối thiểu của sàn.");
        g_scenarios[index].status = EXPIRED;
        return;
    }

    double tp1 = (sc.tps_count > 0) ? sc.tps[0] : 0.0;
    double tp2 = (sc.tps_count > 1) ? sc.tps[1] : 0.0;
    bool has_tp2 = (tp2 > 0.0);

    // Chia volume 2 TP: 50% TP1, 50% TP2
    double lot_tp1 = total_lots;
    double lot_tp2 = 0.0;
    if(has_tp2 && total_lots >= 2.0 * vmin) {
        lot_tp1 = NormalizeDouble(MathFloor((total_lots * 0.5) / vmin) * vmin, 2);
        lot_tp2 = NormalizeDouble(total_lots - lot_tp1, 2);
    }

    Print(StringFormat("=> [VÀO LỆNH BREAKOUT] %s %s | Total Lot: %.2f (TP1: %.2f, TP2: %.2f) | Entry: %.3f | SL: %.3f | Risk: %.2f USD",
          sc.id, (is_buy ? "BUY" : "SELL"), total_lots, lot_tp1, lot_tp2, entry, sl, max_risk));

    int orders_sent = 0;
    // Lệnh 1: Chốt tại TP1
    if(lot_tp1 >= vmin) {
        string comment1 = "BO_" + sc.id + "_TP1";
        bool r1 = is_buy ? trade.Buy(lot_tp1, _Symbol, 0, sl, tp1, comment1)
                         : trade.Sell(lot_tp1, _Symbol, 0, sl, tp1, comment1);
        if(r1) orders_sent++;
        else Print("Lỗi gửi lệnh TP1: ", GetLastError());
    }

    // Lệnh 2: Chốt tại TP2
    if(has_tp2 && lot_tp2 >= vmin) {
        string comment2 = "BO_" + sc.id + "_TP2";
        bool r2 = is_buy ? trade.Buy(lot_tp2, _Symbol, 0, sl, tp2, comment2)
                         : trade.Sell(lot_tp2, _Symbol, 0, sl, tp2, comment2);
        if(r2) orders_sent++;
        else Print("Lỗi gửi lệnh TP2: ", GetLastError());
    }

    if(orders_sent > 0) {
        g_scenarios[index].status = ACTIVE;
        g_scenarios[index].filled_lots = total_lots;
        g_is_trading = true;
        g_trade_start_time = TimeCurrent();
        g_active_scenario_index = index;
        UpdateChartDashboard();
    }
}

//+------------------------------------------------------------------+
//| Xử lý Logic Kịch Bản Breakout Chuyên Biệt                        |
//+------------------------------------------------------------------+
void ProcessBreakoutScenario(int index, double bid, double ask) {
    if(g_scenarios[index].status != PENDING) return;

    double trigger = g_scenarios[index].trigger_price;
    bool is_sell = (g_scenarios[index].type == BREAKOUT_SELL);
    bool is_buy = !is_sell;
    ENUM_TIMEFRAMES confirm_tf = g_scenarios[index].confirm_timeframe;
    ENUM_TIMEFRAMES retest_tf  = g_scenarios[index].retest_timeframe;
    double min_break = MinBreakDistance();

    // BƯỚC 1: NẾN CONFIRM ĐÓNG PHÁ DỨT KHOÁT QUA TRIGGER
    if(!g_scenarios[index].breakout_confirmed) {
        if(IsNewBarOn(confirm_tf, g_scenarios[index].last_bar_time)) {
            double c1 = iClose(_Symbol, confirm_tf, 1);
            double o1 = iOpen(_Symbol, confirm_tf, 1);
            bool broke = is_sell ? (c1 < trigger - min_break) : (c1 > trigger + min_break);
            if(!broke) return;

            // Kiểm tra nến đóng thuận chiều
            bool dir_ok = is_buy ? (c1 > o1) : (c1 < o1);
            if(!dir_ok) return;

            g_scenarios[index].breakout_confirmed = true;
            g_scenarios[index].retest_touched = false;
            g_scenarios[index].retest_wait_bars = 0;
            g_scenarios[index].last_retest_bar_time = iTime(_Symbol, retest_tf, 0);

            Print(StringFormat("[BREAKOUT CONFIRMED] Kịch bản [%s]: Nến %s đóng phá qua trigger (Close: %.3f, Trigger: %.3f). %s",
                  g_scenarios[index].id, TimeframeToString(confirm_tf), c1, trigger,
                  (InpRequireRetest ? "Chờ nến retest tiếp theo..." : "Vào lệnh ngay lập tức!")));

            if(!InpRequireRetest) {
                ExecuteBreakoutTrade(index);
                return;
            }
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

    // Kiểm tra giá đã hồi chạm lại vùng trigger chưa
    if(!g_scenarios[index].retest_touched) {
        bool touched = is_sell ? (h_retest >= trigger) : (l_retest <= trigger);
        if(touched) {
            g_scenarios[index].retest_touched = true;
            Print(StringFormat("[BREAKOUT RETEST TOUCH] Kịch bản [%s]: Nến %s đã hồi chạm lại trigger %.3f.",
                  g_scenarios[index].id, TimeframeToString(retest_tf), trigger));
        }
    }

    // Nến retest đóng lại đúng chiều phá
    bool still_broken = is_sell ? (c_retest < trigger) : (c_retest > trigger);
    if(g_scenarios[index].retest_touched && still_broken) {
        Print(StringFormat("[BREAKOUT RETEST OK] Kịch bản [%s] retest thành công trên %s. Kích hoạt vào lệnh!",
              g_scenarios[index].id, TimeframeToString(retest_tf)));
        ExecuteBreakoutTrade(index);
    }
}

//+------------------------------------------------------------------+
//| Quản lý vị thế đang mở (Dời Breakeven & Khóa lãi)                |
//+------------------------------------------------------------------+
void ManageOpenPositions() {
    if(!HasOpenPositions()) {
        if(g_is_trading) {
            g_is_trading = false;
            g_trade_start_time = 0;
            g_active_scenario_index = -1;
            // Kiểm tra lệnh đóng lỗ để bật cooldown
            HistorySelect(TimeCurrent() - 300, TimeCurrent() + 60);
            for(int i = HistoryDealsTotal() - 1; i >= 0; i--) {
                ulong ticket = HistoryDealGetTicket(i);
                if(HistoryDealGetInteger(ticket, DEAL_MAGIC) == InpMagicNumber) {
                    double profit = HistoryDealGetDouble(ticket, DEAL_PROFIT);
                    if(profit < -0.01) {
                        g_last_loss_time = TimeCurrent();
                        Print("[COOLDOWN ACTIVATED] Phát hiện lệnh bị cắt lỗ. Bắt đầu thời gian nghỉ ", InpLossCooldownMinutes, " phút.");
                        break;
                    }
                }
            }
            UpdateChartDashboard();
        }
        return;
    }

    // Kiểm tra xem lệnh TP1 đã chốt lời thành công chưa
    int profitable_deals = 0;
    if(g_trade_start_time > 0) {
        HistorySelect(g_trade_start_time, TimeCurrent() + 60);
        for(int i = 0; i < HistoryDealsTotal(); i++) {
            ulong t = HistoryDealGetTicket(i);
            if(HistoryDealGetInteger(t, DEAL_MAGIC) == InpMagicNumber && HistoryDealGetString(t, DEAL_SYMBOL) == _Symbol) {
                long entry_type = HistoryDealGetInteger(t, DEAL_ENTRY);
                if(entry_type == DEAL_ENTRY_OUT || entry_type == DEAL_ENTRY_INOUT) {
                    if(HistoryDealGetDouble(t, DEAL_PROFIT) > 0) profitable_deals++;
                }
            }
        }
    }

    int ai = g_active_scenario_index;
    double tp1 = (ai >= 0 && ai < g_scenarios_count && g_scenarios[ai].tps_count > 0) ? g_scenarios[ai].tps[0] : 0.0;
    double point  = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
    double offset = InpBreakevenOffset * point;

    for(int i = PositionsTotal() - 1; i >= 0; i--) {
        if(PositionGetSymbol(i) != _Symbol || PositionGetInteger(POSITION_MAGIC) != InpMagicNumber)
            continue;

        ulong pos_ticket = PositionGetTicket(i);
        double open_p    = PositionGetDouble(POSITION_PRICE_OPEN);
        double current_sl = PositionGetDouble(POSITION_SL);
        double current_tp = PositionGetDouble(POSITION_TP);
        long   pos_type   = PositionGetInteger(POSITION_TYPE);

        if(profitable_deals >= 1) {
            // Khi TP1 đã cắn, dời SL của lệnh còn lại về Hòa vốn + Bù phí
            double target_sl = (pos_type == POSITION_TYPE_BUY) ? (open_p + offset) : (open_p - offset);
            bool need_mod = false;
            if(pos_type == POSITION_TYPE_BUY && current_sl < target_sl - point) need_mod = true;
            if(pos_type == POSITION_TYPE_SELL && (current_sl > target_sl + point || current_sl == 0)) need_mod = true;

            if(need_mod) {
                if(trade.PositionModify(pos_ticket, NormalizeDouble(target_sl, _Digits), current_tp)) {
                    Print("[BREAKEVEN MOVED] TP1 đã cắn! Đã dời SL lệnh #", pos_ticket, " về mức hòa vốn ", target_sl);
                }
            }
        }
    }
}

//+------------------------------------------------------------------+
//| Bảng Dashboard trực quan trên màn hình                           |
//+------------------------------------------------------------------+
void UpdateChartDashboard() {
    string text = "=======================================================\n";
    text += "       🔥 BREAKOUT STRATEGY TRADER (AI / QUANT) 🔥       \n";
    text += "=======================================================\n";
    text += StringFormat("Symbol: %s | Magic: %d | Rủi ro: %.2f USD | SL Trượt: %d pts\n", _Symbol, InpMagicNumber, InpMaxRiskMoney, InpSlippage);
    text += StringFormat("API Scan: %s (%s, Limit: %d) | Chế độ: [%s / %s]\n", InpApiBaseUrl, InpScanInterval, InpScanLimit, InpScanPatternMode, InpScanScenarioMode);
    text += "-------------------------------------------------------\n";
    
    // Thống kê ngày
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
        text += StringFormat("⏳ Cooldown sau SL: CÒN %d PHÚT %d GIÂY (Tránh nhồi lệnh)\n", rem / 60, rem % 60);
    }

    text += "-------------------------------------------------------\n";
    if(g_scenarios_count == 0) {
        text += "Trạng thái Kịch bản: CHƯA CÓ KỊCH BẢN BREAKOUT NÀO.\n";
        text += "(Đang chờ lệnh Scan hoặc chu kỳ tự động quét từ API...)\n";
    } else {
        text += StringFormat("Kịch bản Breakout đang giám sát (%d kịch bản | Signal: %s):\n", g_scenarios_count, g_current_signal_id);
        for(int i = 0; i < g_scenarios_count; i++) {
            Scenario sc = g_scenarios[i];
            string type_str = (sc.type == BREAKOUT_BUY) ? "BUY BREAKOUT" : "SELL BREAKOUT";
            string st_str = "PENDING (Chờ nến xác nhận)";
            if(sc.status == ACTIVE) st_str = "🔥 ĐANG TRONG LỆNH";
            else if(sc.breakout_confirmed) st_str = "⏳ ĐÃ PHÁ VỠ - CHỜ RETEST";
            else if(sc.status == COMPLETED) st_str = "✅ HOÀN THÀNH";
            else if(sc.status == EXPIRED) st_str = "❌ HẾT HIỆU LỰC";

            double tp1 = (sc.tps_count > 0) ? sc.tps[0] : 0.0;
            double tp2 = (sc.tps_count > 1) ? sc.tps[1] : 0.0;

            text += StringFormat(" [%d] %s - %s\n", i + 1, sc.id, type_str);
            text += StringFormat("     Trigger: %.3f | SL: %.3f | TP1: %.3f | TP2: %.3f\n", sc.trigger_price, sc.sl, tp1, tp2);
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

    CheckDailyReset();
    CalculateDailyClosedPnL();
    UpdateChartDashboard();

    // 1. Nếu cấu hình InpRequestScanOnStart = true -> Gọi scan ban đầu
    if(InpRequestScanOnStart) {
        Print("=> [INIT] Kích hoạt gửi yêu cầu Scan Breakout ban đầu lên API...");
        TriggerBreakoutScanApi();
    }

    // 2. Tải kịch bản mới nhất hiện có từ API
    CheckAndFetchNewScenario();

    // Kích hoạt Timer mỗi 1 giây để kiểm tra thời gian và rủi ro
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
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick() {
    CheckDailyRiskThresholds();
    ManageOpenPositions();

    double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);

    if(!IsDayStopped() && !IsInLossCooldown()) {
        for(int i = 0; i < g_scenarios_count; i++) {
            if(g_scenarios[i].status == PENDING) {
                ProcessBreakoutScenario(i, bid, ask);
            }
        }
    }
}

//+------------------------------------------------------------------+
//| Timer function: Định kỳ mỗi 1 phút poll kịch bản & quản lý rủi ro|
//+------------------------------------------------------------------+
void OnTimer() {
    datetime now = TimeCurrent();

    // 1. Kiểm tra rủi ro ngày
    CheckDailyRiskThresholds();

    // 2. Poll kịch bản mới mỗi InpCheckScenarioSec (mặc định 60 giây = 1 phút)
    if(now - g_last_check_scenario_time >= InpCheckScenarioSec) {
        g_last_check_scenario_time = now;
        CheckAndFetchNewScenario();
    }

    // 3. Tự động gửi scan mới nếu bật InpAutoScanIntervalMinutes
    if(InpAutoScanIntervalMinutes > 0 && !HasOpenPositions() && !IsDayStopped() && !IsInLossCooldown()) {
        if(now - g_last_scan_request_time >= (InpAutoScanIntervalMinutes * 60)) {
            if(IsAllScenariosFinished()) {
                Print("=> [AUTO SCAN] Đã tới chu kỳ quét tự động (", InpAutoScanIntervalMinutes, " phút). Gửi yêu cầu scan kịch bản mới...");
                TriggerBreakoutScanApi();
            }
        }
    }

    // 4. Cập nhật Dashboard hiển thị
    UpdateChartDashboard();
}
