//+------------------------------------------------------------------+
//|                                             MarketDataPusher.mq5 |
//|                                                      Tuyen PC    |
//+------------------------------------------------------------------+
#property copyright "Tuyen PC"
#property link      ""
#property version   "1.14"

#include <Trade\Trade.mqh>
#include <JAson.mqh>

//--- Cài đặt API ---
input string BaseUrl         = "https://botai.cscmobicorp.com";
input bool   InpUseAiStructure = true;             // Sử dụng AI Structure (Gửi trường "useAiStructure" trong /initialize)
input bool   InpCallInitialize = false;            // true = gọi /initialize lúc OnInit. false = bỏ qua init
input bool   InpCallUpdate     = true;             // true = gửi dữ liệu /update sau mỗi nến Trigger. false = tắt gửi /update

//--- Quản Lý Rủi Ro & Mục Tiêu Ngày (Daily Target & Loss Protection) ---
input double InpDailyTargetProfit       = 100.0;            // Mục tiêu chốt lời ngày (USD, 0 = tắt). Đạt mức này sẽ chốt hết lệnh và nghỉ ngơi chờ hôm sau reset.
input double InpMaxDailyLoss            = 200.0;            // Ngưỡng cắt lỗ tối đa 1 ngày (USD, 0 = tắt). Lỗ chạm mức này sẽ đóng hết lệnh và nghỉ ngơi chờ hôm sau reset.
input int    InpLossCooldownMinutes     = 30;               // Thời gian nghỉ sau khi bị SL/thua lỗ (phút, 0 = tắt). Tránh tâm lý nhồi lệnh liên tục.
input int    InpMinPlanIntervalMinutes  = 15;               // Giãn cách tối thiểu giữa 2 lần xin kịch bản mới (phút). Tránh gọi AI dồn dập.

//--- Cài đặt Giao Dịch & Quản Lý Kịch Bản AI ---
input double InpMaxRiskMoney     = 100.0;            // Số tiền chịu rủi ro tối đa 1 lần vào lệnh (USD). Lot tính từ SL, không được vượt mức này.
input ulong  InpMagicNumber      = 123456;           // Magic Number
input int    InpSlippage         = 10;               // Slippage
input int    InpBreakevenOffset  = 20;               // Bù trừ hòa vốn (Points) để bù phí khi dời SL sau TP1
input int    InpCheckScenarioSec = 180;              // Thời gian gọi API kiểm tra kịch bản mới (giây) - Mặc định 180s (3 phút)
input int    InpMaxConfirmBars   = 8;                // Tối đa nến confirm sau khi giá vào zone (REVERSAL). 0 = không giới hạn
input int    InpMaxRetestBars    = 12;               // Tối đa nến retest sau khi phá (BREAKOUT). 0 = không giới hạn

//--- Quản lý Chốt lời Bậc thang & Thoát sớm (2 TP + Early BE + Reversal Exit) ---
input bool   InpEnableSmallTfExit       = true;      // Thoát sạch lệnh khi nến khung nhỏ đảo chiều trước khi tới TP2
input bool   InpSmallTfExitInProfitOnly = true;      // Chỉ thoát đảo chiều khung nhỏ khi tổng vị thế đang có lãi dương
input bool   InpLockTp1ToHalfOnTp1Hit   = true;      // Khóa lãi: Kéo SL lệnh TP2 lên mốc 50% TP1 khi TP1 đã cắn

//--- Cài đặt khung thời gian kích hoạt (Trigger) cập nhật ---
input ENUM_TIMEFRAMES InpTriggerTimeframe = PERIOD_M1; // Scalping: mỗi nến M1 đóng thì /update

// Scalping mặc định: H1 bias → M15 xác nhận kịch bản → M5 vào lệnh / retest.
// Số nến đủ BOS/CHOCH, không phình prompt AI.
//--- Cài đặt Timeframe 1 ---
input bool             InpEnableTf1       = true;             // Kích hoạt Timeframe 1
input ENUM_TIMEFRAMES  InpTimeframe1      = PERIOD_H1;        // Bias / cấu trúc khung lớn
input int              InpCandles1        = 96;               // 96 H1 ≈ 4 ngày

//--- Cài đặt Timeframe 2 ---
input bool             InpEnableTf2       = true;             // Kích hoạt Timeframe 2
input ENUM_TIMEFRAMES  InpTimeframe2      = PERIOD_M15;       // Xác nhận kịch bản (confirm)
input int              InpCandles2        = 96;               // 96 M15 ≈ 24 giờ

//--- Cài đặt Timeframe 3 ---
input bool             InpEnableTf3       = true;             // Kích hoạt Timeframe 3
input ENUM_TIMEFRAMES  InpTimeframe3      = PERIOD_M5;        // Vào lệnh / retest
input int              InpCandles3        = 72;               // 72 M5 ≈ 6 giờ (1 phiên)

//--- Cài đặt Timeframe 4 (tắt — bật M1 nếu muốn nhìn siêu ngắn) ---
input bool             InpEnableTf4       = false;            // Tắt mặc định (scalping 3 khung)
input ENUM_TIMEFRAMES  InpTimeframe4      = PERIOD_M1;        // Dự phòng
input int              InpCandles4        = 60;               // 60 M1 ≈ 1 giờ nếu bật

// Biến lưu trữ thời gian mở nến Trigger hiện tại
datetime last_trigger_time = 0;

// Các handles của Indicator cho 4 timeframe
int h_emaFast[4], h_emaSlow[4], h_rsi[4], h_atr[4], h_adx[4];

//====================================================================
// KHAI BÁO CẤU TRÚC VÀ BIẾN CHO GIAO DỊCH KỊCH BẢN AI
//====================================================================
CTrade trade;

enum ENUM_SCENARIO_TYPE {
    REVERSAL_SELL,
    REVERSAL_BUY,
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
    
    double               entry_min;
    double               entry_max;
    
    double               trigger_price;
    double               sl;
    double               max_risk_usd;     // lỗ chấp nhận kịch bản này; EA tính lot từ SL
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
    bool                 ready_to_confirm;
    bool                 breakout_confirmed;   // nến confirm đã đóng phá trigger
    bool                 retest_touched;       // giá đã hồi chạm lại trigger
    int                  confirm_wait_bars;    // số nến confirm đã chờ sau khi vào zone
    int                  retest_wait_bars;     // số nến retest đã chờ sau khi phá
    bool                 reversal_mid_filled;  // đã vào 50% lot khi chạm giữa zone
    bool                 reversal_confirm_filled;
    double               planned_lots;
    double               filled_lots;
    datetime             last_bar_time;
    datetime             last_retest_bar_time;
};

Scenario g_scenarios[];
int      g_scenarios_count = 0;

string   g_current_signal_id = "";        // Lưu signal_id đang xử lý
bool     g_is_trading = false;           // Đang có lệnh chạy hay không
datetime g_trade_start_time = 0;         // Thời điểm bắt đầu vào lệnh
datetime g_last_small_tf_bar = 0;        // Nến khung nhỏ đã kiểm tra tín hiệu đảo chiều
int      g_active_scenario_index = -1;   // Kịch bản nào đang khớp lệnh
bool     g_ea_stopped = false;           // Đã hoàn thành (Win)
datetime g_last_scenario_check = 0;      // Thời điểm kiểm tra API kịch bản gần nhất
datetime g_last_loss_time = 0;           // Thời điểm dính SL/thua lỗ gần nhất (dùng cho Loss Cooldown)
datetime g_last_plan_request_time = 0;   // Thời điểm gửi yêu cầu xin kịch bản mới gần nhất (dùng cho Throttling)
datetime g_current_trading_day = 0;      // Thời điểm bắt đầu ngày giao dịch hiện tại (PERIOD_D1)
bool     g_daily_target_reached = false; // Đã đạt mục tiêu chốt lời ngày hôm nay
bool     g_daily_loss_reached = false;   // Đã chạm ngưỡng cắt lỗ tối đa ngày hôm nay

// Kiểm tra xem EA đã bị dừng giao dịch trong ngày chưa (do đạt lãi hoặc chạm lỗ tối đa)
bool IsDayStopped()
  {
   return (g_daily_target_reached || g_daily_loss_reached);
  }

//+------------------------------------------------------------------+
//| Chuyển chuỗi TF thành ENUM_TIMEFRAMES                            |
//+------------------------------------------------------------------+
ENUM_TIMEFRAMES StringToTimeframe(string tf) {
    if(tf == "M1") return PERIOD_M1;
    if(tf == "M5") return PERIOD_M5;
    if(tf == "M15") return PERIOD_M15;
    if(tf == "M30") return PERIOD_M30;
    if(tf == "H1") return PERIOD_H1;
    if(tf == "H4") return PERIOD_H4;
    if(tf == "D1") return PERIOD_D1;
    return PERIOD_CURRENT;
}

//+------------------------------------------------------------------+
//| 1 bậc nhỏ hơn khung confirm (M15→M5, M5→M1, H1→M15, H4→H1)       |
//+------------------------------------------------------------------+
ENUM_TIMEFRAMES OneStepSmallerTf(ENUM_TIMEFRAMES tf)
  {
   if(tf == PERIOD_M1)  return PERIOD_M1;
   if(tf == PERIOD_M5)  return PERIOD_M1;
   if(tf == PERIOD_M15) return PERIOD_M5;
   if(tf == PERIOD_M30) return PERIOD_M15;
   if(tf == PERIOD_H1)  return PERIOD_M15;
   if(tf == PERIOD_H4)  return PERIOD_H1;
   if(tf == PERIOD_D1)  return PERIOD_H4;
   return PERIOD_M5;
  }

//+------------------------------------------------------------------+
//| Kiểm tra xem còn lệnh đang mở không                              |
//+------------------------------------------------------------------+
bool HasOpenPositions() {
    for(int i = PositionsTotal() - 1; i >= 0; i--) {
        if(PositionGetSymbol(i) == _Symbol) {
            if(PositionGetInteger(POSITION_MAGIC) == InpMagicNumber) return true;
        }
    }
    return false;
}

//+------------------------------------------------------------------+
//| Kiểm tra xem có đang trong thời gian nghỉ sau lệnh thua không    |
//+------------------------------------------------------------------+
bool IsInLossCooldown()
  {
   if(InpLossCooldownMinutes <= 0 || g_last_loss_time == 0) return false;
   if(TimeCurrent() - g_last_loss_time < InpLossCooldownMinutes * 60)
      return true;
   return false;
  }

//+------------------------------------------------------------------+
//| Tính tổng lợi nhuận ròng hôm nay (Closed Deals + Floating PnL)   |
//+------------------------------------------------------------------+
double CalculateTodayNetProfit()
  {
   datetime day_start = iTime(_Symbol, PERIOD_D1, 0);
   if(day_start == 0)
     {
      MqlDateTime dt;
      TimeCurrent(dt);
      dt.hour = 0; dt.min = 0; dt.sec = 0;
      day_start = StructToTime(dt);
     }

   double total = 0.0;

   // 1. Lợi nhuận từ lịch sử deal trong ngày hôm nay (tính cả commission & swap)
   if(HistorySelect(day_start, TimeCurrent() + 60))
     {
      int deals = HistoryDealsTotal();
      for(int i = 0; i < deals; i++)
        {
         ulong ticket = HistoryDealGetTicket(i);
         if(ticket == 0) continue;
         if(HistoryDealGetString(ticket, DEAL_SYMBOL) == _Symbol && 
            HistoryDealGetInteger(ticket, DEAL_MAGIC) == InpMagicNumber)
           {
            total += HistoryDealGetDouble(ticket, DEAL_PROFIT)
                   + HistoryDealGetDouble(ticket, DEAL_COMMISSION)
                   + HistoryDealGetDouble(ticket, DEAL_SWAP);
           }
        }
     }

   // 2. Lợi nhuận thả nổi của các lệnh đang mở (nếu có)
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      if(PositionGetSymbol(i) == _Symbol && 
         PositionGetInteger(POSITION_MAGIC) == InpMagicNumber)
        {
         total += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
        }
     }

   return total;
  }

//+------------------------------------------------------------------+
//| Reset mục tiêu ngày khi bước sang ngày giao dịch mới             |
//+------------------------------------------------------------------+
void CheckAndResetDailyTarget()
  {
   datetime today_start = iTime(_Symbol, PERIOD_D1, 0);
   if(today_start == 0) return;
   
   if(g_current_trading_day == 0)
     {
      g_current_trading_day = today_start;
     }
   else if(today_start != g_current_trading_day)
     {
      Print("==================================================================");
      Print("=> [NEW DAY RESET] Bước sang ngày giao dịch mới (", TimeToString(today_start, TIME_DATE), "). Đã reset mục tiêu ngày!");
      Print("==================================================================");
      g_current_trading_day = today_start;
      g_daily_target_reached = false;
      g_daily_loss_reached = false;
      g_ea_stopped = false;
      g_last_loss_time = 0;
     }
  }

//+------------------------------------------------------------------+
//| Kiểm tra ngưỡng chốt lời / cắt lỗ tối đa ngày                    |
//+------------------------------------------------------------------+
void CheckDailyTargetProfit()
  {
   if(IsDayStopped()) return;

   double today_profit = CalculateTodayNetProfit();

   // 1. Kiểm tra mục tiêu chốt lời ngày (Daily Target Profit)
   if(InpDailyTargetProfit > 0.0)
     {
      if(today_profit >= InpDailyTargetProfit)
        {
         g_daily_target_reached = true;
         g_ea_stopped = true;
         
         string reason = StringFormat("ĐẠT MỤC TIÊU LỢI NHUẬN NGÀY (Lãi hôm nay: %.2f USD >= Mục tiêu: %.2f USD)", today_profit, InpDailyTargetProfit);
         Print("==================================================================");
         Print("=> [DAILY TARGET HIT] ", reason);
         Print("=> Đang chốt hết toàn bộ lệnh đang mở và tạm dừng giao dịch chờ ngày mai reset.");
         Print("==================================================================");
         
         CloseAllPositions(reason);
         
         for(int i = 0; i < g_scenarios_count; i++)
           {
            if(g_scenarios[i].status == PENDING)
               g_scenarios[i].status = EXPIRED;
           }
         
         g_is_trading = false;
         g_active_scenario_index = -1;
         g_trade_start_time = 0;
         return;
        }
     }

   // 2. Kiểm tra ngưỡng cắt lỗ tối đa 1 ngày (Max Daily Loss)
   if(InpMaxDailyLoss > 0.0)
     {
      if(today_profit <= -MathAbs(InpMaxDailyLoss))
        {
         g_daily_loss_reached = true;
         g_ea_stopped = true;
         
         string reason = StringFormat("CHẠM NGƯỠNG CẮT LỖ TỐI ĐA NGÀY (Lỗ hôm nay: %.2f USD <= Giới hạn: -%.2f USD)", today_profit, InpMaxDailyLoss);
         Print("==================================================================");
         Print("=> [MAX DAILY LOSS HIT] ", reason);
         Print("=> Đang đóng toàn bộ lệnh và tạm dừng giao dịch ngày hôm nay để bảo vệ vốn!");
         Print("==================================================================");
         
         CloseAllPositions(reason);
         
         for(int i = 0; i < g_scenarios_count; i++)
           {
            if(g_scenarios[i].status == PENDING)
               g_scenarios[i].status = EXPIRED;
           }
         
         g_is_trading = false;
         g_active_scenario_index = -1;
         g_trade_start_time = 0;
        }
     }
  }

//+------------------------------------------------------------------+
//| Hiển thị Dashboard trạng thái EA lên Chart                       |
//+------------------------------------------------------------------+
void UpdateChartDashboard()
  {
   double today_pnl = CalculateTodayNetProfit();
   string status_str = "";
   
   if(g_daily_loss_reached)
     {
      status_str = StringFormat("🔴 ĐÃ CHẠM NGƯỠNG LỖ TỐI ĐA NGÀY (-%.2f USD) - NGHỈ ĐẾN HÔM SAU", InpMaxDailyLoss);
     }
   else if(g_daily_target_reached)
     {
      status_str = StringFormat("🟢 ĐÃ ĐẠT MỤC TIÊU LÃI NGÀY (+%.2f USD) - NGHỈ ĐẾN HÔM SAU", InpDailyTargetProfit);
     }
   else if(IsInLossCooldown())
     {
      int rem_sec = (int)((InpLossCooldownMinutes * 60) - (TimeCurrent() - g_last_loss_time));
      if(rem_sec < 0) rem_sec = 0;
      status_str = StringFormat("🟠 ĐANG NGHỈ SAU LỖ (LOSS COOLDOWN) - CÒN %d PHÚT %d GIÂY", rem_sec / 60, rem_sec % 60);
     }
   else if(g_is_trading)
     {
      status_str = "🔵 ĐANG TRONG LỆNH (ACTIVE TRADING)";
     }
   else
     {
      status_str = "⚪ ĐANG GIÁM SÁT KỊCH BẢN (MONITORING)";
     }
     
   string info = "==================================================\n";
   info += "🤖 AI TRADING BOT - " + _Symbol + "\n";
   info += "Trạng thái: " + status_str + "\n";
   info += StringFormat("Lợi nhuận hôm nay: %.2f USD", today_pnl);
   if(InpDailyTargetProfit > 0.0)
      info += StringFormat(" / Mục tiêu lãi: %.2f USD (%.1f%%)", InpDailyTargetProfit, (today_pnl / InpDailyTargetProfit) * 100.0);
   else
      info += " / Mục tiêu lãi: Không giới hạn";
   info += "\n";
   
   if(InpMaxDailyLoss > 0.0)
      info += StringFormat("Giới hạn lỗ tối đa ngày: -%.2f USD\n", InpMaxDailyLoss);
      
   info += StringFormat("Signal ID hiện tại: %s | Số kịch bản: %d\n", (g_current_signal_id == "" ? "Chưa có" : g_current_signal_id), g_scenarios_count);
   if(IsInLossCooldown())
     {
      info += StringFormat("Thời gian nghỉ sau SL: %d phút (Tránh nhồi lệnh liên tục)\n", InpLossCooldownMinutes);
     }
   info += "==================================================";
   
   Comment(info);
  }

//+------------------------------------------------------------------+
//| Kiểm tra xem toàn bộ các kịch bản đã kết thúc/hết hiệu lực chưa   |
//+------------------------------------------------------------------+
bool IsAllScenariosFinished() {
    // Nếu đã dừng giao dịch trong ngày (đạt lãi hoặc chạm ngưỡng lỗ tối đa), hoặc đang trong thời gian nghỉ sau lỗ -> Không xin plan mới
    if(IsDayStopped()) return false;
    if(IsInLossCooldown()) return false;

    // Nếu đang có lệnh hoặc đang trong chu trình giao dịch -> Chưa kết thúc
    if(HasOpenPositions() || g_is_trading) return false;
    
    // Nếu chưa từng có kịch bản nào được nạp -> Chưa có kịch bản để hoàn thành
    if(g_scenarios_count == 0) return false;
    
    // Nếu EA đã dừng vì chốt lời (WIN) -> Toàn bộ kịch bản đã xong
    if(g_ea_stopped) return true;
    
    int pending_count = 0;
    for(int i = 0; i < g_scenarios_count; i++) {
        if(g_scenarios[i].status == PENDING || g_scenarios[i].status == ACTIVE)
            pending_count++;
    }
    
    return (pending_count == 0);
}

//+------------------------------------------------------------------+
//| Hàm định dạng số liệu Indicator (tránh bị lỗi EMPTY_VALUE)       |
//+------------------------------------------------------------------+
string FormatIndicator(double val, int digits)
  {
   if(val == EMPTY_VALUE || MathIsValidNumber(val) == false || val == 0.0) return "0.0";
   return DoubleToString(val, digits);
  }

//+------------------------------------------------------------------+
//| Hàm lấy tên String của Timeframe (Vd: PERIOD_H4 -> "H4")         |
//+------------------------------------------------------------------+
string TimeframeToString(ENUM_TIMEFRAMES tf)
  {
   string name = EnumToString(tf);
   if(StringFind(name, "PERIOD_") == 0)
      name = StringSubstr(name, 7); // Bỏ chữ "PERIOD_" để giữ lại ví dụ "H4", "M15"
   return name;
  }

//+------------------------------------------------------------------+
//| Hàm lấy URL hoàn chỉnh để gọi API                                |
//+------------------------------------------------------------------+
string GetApiUrl(string endpoint)
  {
   string base = BaseUrl;
   while(StringLen(base) > 0 && StringSubstr(base, StringLen(base)-1, 1) == "/")
      base = StringSubstr(base, 0, StringLen(base)-1);
      
   if(StringLen(endpoint) > 0 && StringSubstr(endpoint, 0, 1) != "/")
      endpoint = "/" + endpoint;
      
   return base + endpoint;
  }

//+------------------------------------------------------------------+
//| Hàm core để gọi HTTP POST                                        |
//+------------------------------------------------------------------+
bool SendHttpRequest(string json_data, string target_url, bool show_message)
  {
   char post_data[];
   char result_data[];
   string result_headers;
   
   // Chuyển string sang mảng byte chuẩn UTF-8
   int size = StringToCharArray(json_data, post_data, 0, WHOLE_ARRAY, CP_UTF8);
   if(size > 0)
      ArrayResize(post_data, size - 1); // Loại bỏ ký tự null '\0' ở cuối
      
   string headers = "Content-Type: application/json\r\nAccept: application/json\r\n";
   
   Print("--------------------------------------------------");
   Print("[API CALL] Bắt đầu gọi POST request tới: ", target_url);
   
   // Gọi HTTP POST, đặt timeout 15 giây
   int res = WebRequest("POST", target_url, headers, 15000, post_data, result_data, result_headers);
   
   // HTTP code 200 (OK) hoặc 201 (Created) đều được xem là thành công
   if(res == 200 || res == 201)
     {
      string response_str = CharArrayToString(result_data, 0, WHOLE_ARRAY, CP_UTF8);
      Print("[API SUCCESS] HTTP Code: ", res);
      Print("[API SUCCESS] Response: ", response_str);
      Print("--------------------------------------------------");
      return true;
     }
   else
     {
      Print("[API ERROR] HTTP Code: ", res, " | Mã lỗi nội bộ MT5: ", GetLastError());
      
      // Báo lỗi riêng nếu chưa cho phép WebRequest trong cấu hình
      if(res == -1 && GetLastError() == 4014)
        {
         Print("[API ERROR 4014] LỖI BẢO MẬT MT5: Bạn chưa Allow WebRequest cho URL này.");
         if(show_message)
            MessageBox("Lỗi chưa cho phép WebRequest!\nVui lòng vào Options -> Expert Advisors -> Allow WebRequest cho Base URL.", "Lỗi", MB_OK | MB_ICONERROR);
        }
      else
        {
         string response_str = CharArrayToString(result_data, 0, WHOLE_ARRAY, CP_UTF8);
         Print("[API ERROR SERVER] Trả về chi tiết: ", response_str);
        }
        
      Print("--------------------------------------------------");
      return false;
     }
  }

//+------------------------------------------------------------------+
//| Build JSON string cho từng Timeframe (dành cho API initialize)   |
//+------------------------------------------------------------------+
string BuildTimeframeData(int tf_index, string tf_name, ENUM_TIMEFRAMES tf, int count, bool has_next)
  {
   string result = "    \"" + tf_name + "\": [\n";
   
   MqlRates rates[];
   ArraySetAsSeries(rates, true); // Index 0 là nến hiện tại (mới nhất)
   
   int copied = CopyRates(_Symbol, tf, 0, count, rates);
   if(copied <= 0)
     {
      Print("Lỗi khi lấy dữ liệu cho ", tf_name, ", mã lỗi MT5: ", GetLastError());
      return "    \"" + tf_name + "\": []" + (has_next ? ",\n" : "\n");
     }
     
   double emaFast[], emaSlow[], rsi[], atr[], adx[];
   ArraySetAsSeries(emaFast, true);
   ArraySetAsSeries(emaSlow, true);
   ArraySetAsSeries(rsi, true);
   ArraySetAsSeries(atr, true);
   ArraySetAsSeries(adx, true);
   
   CopyBuffer(h_emaFast[tf_index], 0, 0, copied, emaFast);
   CopyBuffer(h_emaSlow[tf_index], 0, 0, copied, emaSlow);
   CopyBuffer(h_rsi[tf_index], 0, 0, copied, rsi);
   CopyBuffer(h_atr[tf_index], 0, 0, copied, atr);
   CopyBuffer(h_adx[tf_index], 0, 0, copied, adx);
     
   // Chạy từ nến cũ nhất (copied - 1) về nến mới nhất (0)
   for(int i = copied - 1; i >= 0; i--)
     {
      double v_emaFast = (i < ArraySize(emaFast)) ? emaFast[i] : 0.0;
      double v_emaSlow = (i < ArraySize(emaSlow)) ? emaSlow[i] : 0.0;
      double v_rsi     = (i < ArraySize(rsi))     ? rsi[i] : 0.0;
      double v_atr     = (i < ArraySize(atr))     ? atr[i] : 0.0;
      double v_adx     = (i < ArraySize(adx))     ? adx[i] : 0.0;
     
      string obj = "      {\n";
      obj += "        \"time\": " + IntegerToString(rates[i].time) + ",\n";
      obj += "        \"open\": " + DoubleToString(rates[i].open, _Digits) + ",\n";
      obj += "        \"high\": " + DoubleToString(rates[i].high, _Digits) + ",\n";
      obj += "        \"low\": " + DoubleToString(rates[i].low, _Digits) + ",\n";
      obj += "        \"close\": " + DoubleToString(rates[i].close, _Digits) + ",\n";
      obj += "        \"tickVolume\": " + IntegerToString(rates[i].tick_volume) + ",\n";
      obj += "        \"emaFast\": " + FormatIndicator(v_emaFast, _Digits) + ",\n";
      obj += "        \"emaSlow\": " + FormatIndicator(v_emaSlow, _Digits) + ",\n";
      obj += "        \"rsi\": " + FormatIndicator(v_rsi, 2) + ",\n";
      obj += "        \"atr\": " + FormatIndicator(v_atr, _Digits) + ",\n";
      obj += "        \"adx\": " + FormatIndicator(v_adx, 2) + "\n";
      obj += "      }";
      
      if(i > 0)
         obj += ","; // Thêm phẩy nếu chưa tới phần tử cuối của mảng
      obj += "\n";
         
      result += obj;
     }
   
   if(has_next)
      result += "    ],\n";
   else
      result += "    ]\n";
      
   return result;
  }

//+------------------------------------------------------------------+
//| Hợp đồng cặp hiện tại — lot/rủi ro phụ thuộc tick value          |
//+------------------------------------------------------------------+
string BuildContractJson()
  {
   string s = "  \"contract\": {\n";
   s += "    \"digits\": " + IntegerToString((int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)) + ",\n";
   s += "    \"point\": " + DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_POINT), 8) + ",\n";
   s += "    \"tickSize\": " + DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE), 8) + ",\n";
   s += "    \"tickValue\": " + DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE), 4) + ",\n";
   s += "    \"contractSize\": " + DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_TRADE_CONTRACT_SIZE), 2) + ",\n";
   s += "    \"volumeMin\": " + DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN), 4) + ",\n";
   s += "    \"volumeStep\": " + DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP), 4) + ",\n";
   s += "    \"volumeMax\": " + DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX), 2) + ",\n";
   s += "    \"stopsLevel\": " + IntegerToString((int)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL)) + ",\n";
   s += "    \"spreadPoints\": " + IntegerToString((int)SymbolInfoInteger(_Symbol, SYMBOL_SPREAD)) + ",\n";
   s += "    \"spread\": " + DoubleToString(
         SymbolInfoDouble(_Symbol, SYMBOL_ASK) - SymbolInfoDouble(_Symbol, SYMBOL_BID), 8) + "\n";
   s += "  },\n";
   return s;
  }

//+------------------------------------------------------------------+
//| Hàm gửi dữ liệu Init                                             |
//+------------------------------------------------------------------+
void SendMarketDataInitialize(bool show_message)
  {
   Print("=> Đang chuẩn bị dữ liệu thị trường (Initialize)...");
   
   // 1. Khởi tạo và gom nhóm dữ liệu JSON cho /initialize
   string json = "{\n";
   json += "  \"symbol\": \"" + _Symbol + "\",\n";
   json += "  \"useAiStructure\": " + (InpUseAiStructure ? "true" : "false") + ",\n";
   json += "  \"maxRiskUsd\": " + DoubleToString(InpMaxRiskMoney, 2) + ",\n";
   json += BuildContractJson();
   json += "  \"timeframes\": {\n";
   
   string tf_blocks[];
   if(InpEnableTf1)
     {
      int s = ArraySize(tf_blocks); ArrayResize(tf_blocks, s + 1);
      tf_blocks[s] = BuildTimeframeData(0, TimeframeToString(InpTimeframe1), InpTimeframe1, InpCandles1, false);
     }
   if(InpEnableTf2)
     {
      int s = ArraySize(tf_blocks); ArrayResize(tf_blocks, s + 1);
      tf_blocks[s] = BuildTimeframeData(1, TimeframeToString(InpTimeframe2), InpTimeframe2, InpCandles2, false);
     }
   if(InpEnableTf3)
     {
      int s = ArraySize(tf_blocks); ArrayResize(tf_blocks, s + 1);
      tf_blocks[s] = BuildTimeframeData(2, TimeframeToString(InpTimeframe3), InpTimeframe3, InpCandles3, false);
     }
   if(InpEnableTf4)
     {
      int s = ArraySize(tf_blocks); ArrayResize(tf_blocks, s + 1);
      tf_blocks[s] = BuildTimeframeData(3, TimeframeToString(InpTimeframe4), InpTimeframe4, InpCandles4, false);
     }
     
   for(int i = 0; i < ArraySize(tf_blocks); i++)
     {
      json += tf_blocks[i];
      if(i < ArraySize(tf_blocks) - 1) json += ",\n";
      else json += "\n";
     }
   
   json += "  }\n";
   json += "}";
   
   Print("Chuẩn bị dữ liệu xong. Bắt đầu gửi qua API /initialize...");
   
   // 2. Gửi dữ liệu JSON qua Web API
   string url = GetApiUrl("/api/v1/market-data/initialize");
   if(SendHttpRequest(json, url, show_message))
     {
      Print("Gửi dữ liệu initialize thành công!");
      if(show_message)
         MessageBox("Khởi tạo API thành công!", "Thành công", MB_OK | MB_ICONINFORMATION);
     }
  }

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
  {
   Print("=== KHỞI CHẠY MARKET DATA PUSHER ===");
   
   string tf1_name = TimeframeToString(InpTimeframe1);
   string tf2_name = TimeframeToString(InpTimeframe2);
   string tf3_name = TimeframeToString(InpTimeframe3);
   string tf4_name = TimeframeToString(InpTimeframe4);
   
   Print("Các khung thời gian đã cấu hình: ", tf1_name, ", ", tf2_name, ", ", tf3_name, ", ", tf4_name);
   Print("Khung thời gian Trigger Update: ", TimeframeToString(InpTriggerTimeframe));
   
   // Khởi tạo các Handle cho Indicator (Chỉ tạo handle cho khung nào được kích hoạt)
   ENUM_TIMEFRAMES tfs[4] = {InpTimeframe1, InpTimeframe2, InpTimeframe3, InpTimeframe4};
   bool enables[4] = {InpEnableTf1, InpEnableTf2, InpEnableTf3, InpEnableTf4};
   for(int i = 0; i < 4; i++)
     {
      if(enables[i])
        {
         h_emaFast[i] = iMA(_Symbol, tfs[i], 34, 0, MODE_EMA, PRICE_CLOSE);
         h_emaSlow[i] = iMA(_Symbol, tfs[i], 89, 0, MODE_EMA, PRICE_CLOSE);
         h_rsi[i]     = iRSI(_Symbol, tfs[i], 14, PRICE_CLOSE);
         h_atr[i]     = iATR(_Symbol, tfs[i], 14);
         h_adx[i]     = iADX(_Symbol, tfs[i], 14);
        }
      else
        {
         h_emaFast[i] = INVALID_HANDLE;
         h_emaSlow[i] = INVALID_HANDLE;
         h_rsi[i]     = INVALID_HANDLE;
         h_atr[i]     = INVALID_HANDLE;
         h_adx[i]     = INVALID_HANDLE;
        }
     }
   
   if(InpCallInitialize)
      SendMarketDataInitialize(true);
   else
      Print("Bỏ qua /initialize (InpCallInitialize=false). Vẫn poll kịch bản từ API.");
   
   // Khởi tạo thời gian của nến Trigger hiện tại để bắt đầu theo dõi ở OnTick
   last_trigger_time = iTime(_Symbol, InpTriggerTimeframe, 0);
   
   // Cấu hình CTrade
   trade.SetExpertMagicNumber(InpMagicNumber);
   trade.SetDeviationInPoints(InpSlippage);
   trade.SetTypeFillingBySymbol(_Symbol);

   // Kiểm tra và khởi tạo trạng thái ngày giao dịch & mục tiêu lợi nhuận
   CheckAndResetDailyTarget();
   CheckDailyTargetProfit();
   UpdateChartDashboard();

   // Kiểm tra kịch bản AI ngay lúc khởi động (nếu chưa chạm mục tiêu/ngưỡng dừng ngày)
   if(!IsDayStopped())
      CheckAndFetchNewScenario();
   
   return(INIT_SUCCEEDED);
  }

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
  {
   Comment(""); // Xóa dashboard chart

   // Giải phóng Handle Indicator
   for(int i = 0; i < 4; i++)
     {
      IndicatorRelease(h_emaFast[i]);
      IndicatorRelease(h_emaSlow[i]);
      IndicatorRelease(h_rsi[i]);
      IndicatorRelease(h_atr[i]);
      IndicatorRelease(h_adx[i]);
     }
  }

//+------------------------------------------------------------------+
//| Build JSON cho 1 nến duy nhất (dành cho API update)              |
//+------------------------------------------------------------------+
string BuildSingleCandleJson(int tf_index, ENUM_TIMEFRAMES tf)
  {
   MqlRates rates[];
   ArraySetAsSeries(rates, true); // Index 0 là nến hiện tại đang chạy (hoặc mới đóng)
   
   int copied = CopyRates(_Symbol, tf, 0, 1, rates);
   if(copied <= 0) return "{}"; // Trả về object rỗng nếu có lỗi
   
   double emaFast[], emaSlow[], rsi[], atr[], adx[];
   ArraySetAsSeries(emaFast, true);
   ArraySetAsSeries(emaSlow, true);
   ArraySetAsSeries(rsi, true);
   ArraySetAsSeries(atr, true);
   ArraySetAsSeries(adx, true);
   
   CopyBuffer(h_emaFast[tf_index], 0, 0, 1, emaFast);
   CopyBuffer(h_emaSlow[tf_index], 0, 0, 1, emaSlow);
   CopyBuffer(h_rsi[tf_index], 0, 0, 1, rsi);
   CopyBuffer(h_atr[tf_index], 0, 0, 1, atr);
   CopyBuffer(h_adx[tf_index], 0, 0, 1, adx);
   
   double v_emaFast = (ArraySize(emaFast) > 0) ? emaFast[0] : 0.0;
   double v_emaSlow = (ArraySize(emaSlow) > 0) ? emaSlow[0] : 0.0;
   double v_rsi     = (ArraySize(rsi) > 0)     ? rsi[0] : 0.0;
   double v_atr     = (ArraySize(atr) > 0)     ? atr[0] : 0.0;
   double v_adx     = (ArraySize(adx) > 0)     ? adx[0] : 0.0;
   
   string obj = "{\n";
   obj += "      \"time\": " + IntegerToString(rates[0].time) + ",\n";
   obj += "      \"open\": " + DoubleToString(rates[0].open, _Digits) + ",\n";
   obj += "      \"high\": " + DoubleToString(rates[0].high, _Digits) + ",\n";
   obj += "      \"low\": " + DoubleToString(rates[0].low, _Digits) + ",\n";
   obj += "      \"close\": " + DoubleToString(rates[0].close, _Digits) + ",\n";
   obj += "      \"tickVolume\": " + IntegerToString(rates[0].tick_volume) + ",\n";
   obj += "      \"emaFast\": " + FormatIndicator(v_emaFast, _Digits) + ",\n";
   obj += "      \"emaSlow\": " + FormatIndicator(v_emaSlow, _Digits) + ",\n";
   obj += "      \"rsi\": " + FormatIndicator(v_rsi, 2) + ",\n";
   obj += "      \"atr\": " + FormatIndicator(v_atr, _Digits) + ",\n";
   obj += "      \"adx\": " + FormatIndicator(v_adx, 2) + "\n";
   obj += "    }";
   
   return obj;
  }

//+------------------------------------------------------------------+
//| Hàm gửi dữ liệu update sau mỗi lần đóng nến Trigger              |
//+------------------------------------------------------------------+
void SendMarketDataUpdate()
  {
   bool refresh_plan = false;
   if(!IsDayStopped() && !IsInLossCooldown())
     {
      if(IsAllScenariosFinished())
        {
         if(g_last_plan_request_time == 0 || (TimeCurrent() - g_last_plan_request_time >= InpMinPlanIntervalMinutes * 60))
           {
            refresh_plan = true;
            g_last_plan_request_time = TimeCurrent();
            Print("=> [REFRESH PLAN] Tất cả kịch bản đã đi qua / hết hiệu lực. Đang gửi kèm \"refreshPlan\": true trong body update (Giãn cách tối thiểu: ", InpMinPlanIntervalMinutes, " phút).");
           }
         else
           {
            int remaining_sec = (int)((InpMinPlanIntervalMinutes * 60) - (TimeCurrent() - g_last_plan_request_time));
            Print("=> [REFRESH PLAN SKIP] Đang trong thời gian giãn cách giữa các lần xin kịch bản (còn ", remaining_sec / 60, " phút ", remaining_sec % 60, "s). Không gửi refreshPlan.");
           }
        }
     }
   else if(g_daily_loss_reached)
     {
      Print("=> [UPDATE] Đã chạm ngưỡng cắt lỗ tối đa ngày (-", InpMaxDailyLoss, " USD). Chỉ gửi nến, không xin kịch bản (refreshPlan=false).");
     }
   else if(g_daily_target_reached)
     {
      Print("=> [UPDATE] Đã đạt mục tiêu lợi nhuận ngày (+", InpDailyTargetProfit, " USD). Chỉ gửi nến, không xin kịch bản (refreshPlan=false).");
     }
   else if(IsInLossCooldown())
     {
      int rem_sec = (int)((InpLossCooldownMinutes * 60) - (TimeCurrent() - g_last_loss_time));
      Print("=> [UPDATE] Đang trong thời gian nghỉ sau lệnh thua (còn ", rem_sec / 60, " phút ", rem_sec % 60, "s). Không xin kịch bản.");
     }

   string json = "{\n";
   json += "  \"symbol\": \"" + _Symbol + "\",\n";
   json += "  \"refreshPlan\": " + (refresh_plan ? "true" : "false") + ",\n";
   json += "  \"maxRiskUsd\": " + DoubleToString(InpMaxRiskMoney, 2) + ",\n";
   json += BuildContractJson();
   json += "  \"timeframes\": {\n";
   
   string tf_updates[];
   if(InpEnableTf1)
     {
      int s = ArraySize(tf_updates); ArrayResize(tf_updates, s + 1);
      tf_updates[s] = "    \"" + TimeframeToString(InpTimeframe1) + "\": " + BuildSingleCandleJson(0, InpTimeframe1);
     }
   if(InpEnableTf2)
     {
      int s = ArraySize(tf_updates); ArrayResize(tf_updates, s + 1);
      tf_updates[s] = "    \"" + TimeframeToString(InpTimeframe2) + "\": " + BuildSingleCandleJson(1, InpTimeframe2);
     }
   if(InpEnableTf3)
     {
      int s = ArraySize(tf_updates); ArrayResize(tf_updates, s + 1);
      tf_updates[s] = "    \"" + TimeframeToString(InpTimeframe3) + "\": " + BuildSingleCandleJson(2, InpTimeframe3);
     }
   if(InpEnableTf4)
     {
      int s = ArraySize(tf_updates); ArrayResize(tf_updates, s + 1);
      tf_updates[s] = "    \"" + TimeframeToString(InpTimeframe4) + "\": " + BuildSingleCandleJson(3, InpTimeframe4);
     }
     
   for(int i = 0; i < ArraySize(tf_updates); i++)
     {
      json += tf_updates[i];
      if(i < ArraySize(tf_updates) - 1) json += ",\n";
      else json += "\n";
     }
   
   json += "  }\n";
   json += "}";
   
   string url = GetApiUrl("/api/v1/market-data/update");
   
   Print("=> Phát hiện đóng nến ", TimeframeToString(InpTriggerTimeframe), ". Chuẩn bị gọi API update...");
   if(!SendHttpRequest(json, url, false))
     {
      // Log lỗi đã được xử lý bên trong hàm SendHttpRequest
     }
  }

//+------------------------------------------------------------------+
//| Expert tick function (Chạy theo mỗi biến động giá)               |
//+------------------------------------------------------------------+
void OnTick()
  {
   datetime current_time = TimeLocal();

   // 0. Quản lý ngày giao dịch, kiểm tra ngưỡng mục tiêu ngày & cập nhật dashboard
   CheckAndResetDailyTarget();
   CheckDailyTargetProfit();
   UpdateChartDashboard();

   // 1. Xử lý Update theo nến
   // Lấy thời gian mở cửa của nến Trigger hiện tại
   datetime current_trigger_time = iTime(_Symbol, InpTriggerTimeframe, 0);
   
   // Kiểm tra nến mới mở để gửi Update
   if(last_trigger_time == 0)
     {
      last_trigger_time = current_trigger_time;
     }
   else if(current_trigger_time != last_trigger_time)
     {
      last_trigger_time = current_trigger_time;
      if(InpCallUpdate)
         SendMarketDataUpdate();
     }

   // 2. Định kỳ kiểm tra kịch bản mới từ API (nếu chưa có lệnh nào đang chạy và chưa dừng giao dịch ngày)
   if(current_time - g_last_scenario_check >= InpCheckScenarioSec)
     {
      g_last_scenario_check = current_time;
      if(!IsDayStopped() && !IsInLossCooldown() && !HasOpenPositions() && !g_is_trading)
        {
         Print("=> [TICK TRIGGER] Đến chu kỳ kiểm tra kịch bản (mỗi ", InpCheckScenarioSec, "s). Đang gọi CheckAndFetchNewScenario()...");
         CheckAndFetchNewScenario();
        }
     }

   // 3. Giám sát giá và thực thi kế hoạch kịch bản
   MonitorScenarios();
  }
  
//+------------------------------------------------------------------+
//| Hàm gửi HTTP GET Request                                         |
//+------------------------------------------------------------------+
bool SendHttpGetRequest(string target_url, string &response_str)
  {
   char post_data[];
   char result_data[];
   string result_headers;
   string headers = "Accept: application/json\r\n";
   
   Print("--------------------------------------------------");
   Print("[API GET CALL] Bắt đầu gọi GET tới: ", target_url);
   
   int res = WebRequest("GET", target_url, headers, 10000, post_data, result_data, result_headers);
   if(res == 200)
     {
      response_str = CharArrayToString(result_data, 0, WHOLE_ARRAY, CP_UTF8);
      Print("[API GET SUCCESS] HTTP Code: 200 | Response: ", response_str);
      Print("--------------------------------------------------");
      return true;
     }
   else
     {
      Print("[API GET ERROR] HTTP Code: ", res, " | URL: ", target_url, " | Lỗi MT5: ", GetLastError());
      if(ArraySize(result_data) > 0)
        {
         string err_body = CharArrayToString(result_data, 0, WHOLE_ARRAY, CP_UTF8);
         Print("[API GET ERROR BODY] ", err_body);
        }
      Print("--------------------------------------------------");
      return false;
     }
  }

//+------------------------------------------------------------------+
//| Kiểm tra và tải kịch bản mới từ API                             |
//+------------------------------------------------------------------+
void CheckAndFetchNewScenario()
  {
   // CHỐT CHẶN AN TOÀN 1: Nếu đã dừng giao dịch trong ngày (đạt lãi hoặc chạm lỗ tối đa) -> Nghỉ ngơi
   if(g_daily_loss_reached)
     {
      Print("[SCENARIO SKIP] Đã chạm ngưỡng cắt lỗ tối đa ngày (-", InpMaxDailyLoss, " USD). Tạm dừng xin kịch bản và giao dịch cho đến ngày mai.");
      return;
     }
   if(g_daily_target_reached)
     {
      Print("[SCENARIO SKIP] Đã đạt mục tiêu lợi nhuận trong ngày (+", InpDailyTargetProfit, " USD). Tạm dừng kiểm tra/xin kịch bản cho đến ngày mai.");
      return;
     }

   // CHỐT CHẶN AN TOÀN 2: Nếu đang trong thời gian nghỉ sau lệnh thua -> Tạm hoãn để tránh nhồi lệnh
   if(IsInLossCooldown())
     {
      int remaining_sec = (int)((InpLossCooldownMinutes * 60) - (TimeCurrent() - g_last_loss_time));
      Print(StringFormat("[SCENARIO SKIP] Đang trong thời gian nghỉ sau lệnh thua để tránh nhồi lệnh (còn %d phút %d giây).", remaining_sec / 60, remaining_sec % 60));
      return;
     }

   // CHỐT CHẶN AN TOÀN 3: Nếu đang có bất kỳ lệnh nào đang mở (hoặc đang trong chu trình giao dịch của kịch bản),
   // TUYỆT ĐỐI KHÔNG GỌI API KIỂM TRA HAY TẢI KỊCH BẢN MỚI
   if(HasOpenPositions())
     {
      Print("[SCENARIO SKIP] Đang có lệnh mở trên chart (Magic: ", InpMagicNumber, "). Tạm hoãn kiểm tra kịch bản mới để ưu tiên quản lý lệnh.");
      return;
     }
   if(g_is_trading)
     {
      Print("[SCENARIO SKIP] Đang trong chu trình giao dịch của kịch bản trước. Tạm hoãn kiểm tra kịch bản mới.");
      return;
     }
     
   string latest_url = GetApiUrl("/api/v1/scenarios/latest-id?symbol=" + _Symbol);
   string latest_resp = "";
   
   if(!SendHttpGetRequest(latest_url, latest_resp)) return;
   
   CJAVal json_latest;
   if(!json_latest.Deserialize(latest_resp))
     {
      Print("[SCENARIO API] Không thể parse JSON từ latest-id: ", latest_resp);
      return;
     }
     
   string new_signal_id = json_latest["signal_id"].ToStr();
   if(StringLen(new_signal_id) == 0)
     {
      Print("[SCENARIO API] API trả về signal_id rỗng!");
      return;
     }
   
   // Kiểm tra cờ has_scenarios và no_trade từ API
   CJAVal* hs_node = json_latest.FindKey("has_scenarios");
   bool has_scenarios = (hs_node != NULL) ? hs_node.ToBool() : true;
   
   CJAVal* nt_node = json_latest.FindKey("no_trade");
   bool no_trade = (nt_node != NULL) ? nt_node.ToBool() : false;
   
   // NẾU KHÔNG CÓ KỊCH BẢN (has_scenarios == false HOẶC no_trade == true):
   if(!has_scenarios || no_trade)
     {
      // Nếu có Signal mới mà báo no_trade/has_scenarios=false thì hủy các kịch bản cũ còn sót (nếu có)
      if(g_scenarios_count > 0 && new_signal_id != g_current_signal_id)
        {
         Print("[SCENARIO] Thị trường có Signal mới [", new_signal_id, "] (has_scenarios=false, no_trade=", (no_trade ? "true" : "false"), "). Đã xóa các kịch bản cũ đang chờ.");
         g_scenarios_count = 0;
         ArrayResize(g_scenarios, 0);
         g_current_signal_id = "";
        }
      Print("[SCENARIO] Signal ID [", new_signal_id, "] chưa có kịch bản (has_scenarios=false, no_trade=", (no_trade ? "true" : "false"), "). Tiếp tục gọi kiểm tra ở chu kỳ sau...");
      return;
     }
   
   // NẾU CÓ KỊCH BẢN: So sánh với signal_id đang lưu giữ HOẶC chưa nạp kịch bản (g_scenarios_count == 0)
   if(new_signal_id != g_current_signal_id || g_scenarios_count == 0)
     {
      Print("--------------------------------------------------");
      Print("[SCENARIO NEW] Phát hiện Signal ID có kịch bản sẵn sàng: [", new_signal_id, "] (Signal cũ: [", g_current_signal_id, "])");
      Print("[SCENARIO CALL] Đang tải chi tiết kịch bản từ server...");
      
      string detail_url = GetApiUrl("/api/v1/scenarios/" + new_signal_id + "?symbol=" + _Symbol);
      string detail_resp = "";
      
      if(SendHttpGetRequest(detail_url, detail_resp))
        {
         ParseAndLoadScenarioJson(detail_resp);
        }
     }
   else
     {
      Print("[SCENARIO] Signal ID không đổi [", g_current_signal_id, "] (đang giám sát ", g_scenarios_count, " kịch bản), tiếp tục theo dõi.");
     }
  }

//+------------------------------------------------------------------+
//| Parse chuỗi JSON kịch bản chi tiết nạp vào bộ nhớ EA             |
//+------------------------------------------------------------------+
void ParseAndLoadScenarioJson(string json_str)
  {
   CJAVal json;
   if(!json.Deserialize(json_str))
     {
      Print("[SCENARIO ERROR] Lỗi phân tích cú pháp JSON kịch bản chi tiết!");
      return;
     }
     
   string symbol_target = json["symbol"].ToStr();
   if(symbol_target != _Symbol && StringFind(_Symbol, symbol_target) < 0)
     {
      Print("[SCENARIO REJECT] Kịch bản không dành cho cặp tiền này! Cần: ", symbol_target, " | Chart hiện tại: ", _Symbol);
      return;
     }
     
   CJAVal* sc_array = json["scenarios"];
   if(sc_array == NULL || sc_array.type != jtARRAY)
     {
      Print("[SCENARIO ERROR] Không tìm thấy mảng 'scenarios' hợp lệ trong JSON.");
      return;
     }
     
   if(sc_array.Size() == 0)
     {
      Print("[SCENARIO] Mảng 'scenarios' rỗng (0 kịch bản). Tiếp tục đợi kịch bản ở chu kỳ sau...");
      g_scenarios_count = 0;
      ArrayResize(g_scenarios, 0);
      return;
     }
     
   g_current_signal_id = json["signal_id"].ToStr();
   g_scenarios_count = sc_array.Size();
   ArrayResize(g_scenarios, g_scenarios_count);
   
   Print("=== [BẮT ĐẦU NẠP KỊCH BẢN] Signal ID: ", g_current_signal_id, " | Bias: ", json["bias"].ToStr(), " ===");
   
   for(int i = 0; i < g_scenarios_count; i++)
     {
      CJAVal* item = sc_array[i];
      
      g_scenarios[i].id = item["id"].ToStr();
      g_scenarios[i].name = item["name"].ToStr();
      
      string type_str = item["type"].ToStr();
      if(type_str == "REVERSAL_SELL") g_scenarios[i].type = REVERSAL_SELL;
      else if(type_str == "REVERSAL_BUY") g_scenarios[i].type = REVERSAL_BUY;
      else if(type_str == "BREAKOUT_SELL") g_scenarios[i].type = BREAKOUT_SELL;
      else if(type_str == "BREAKOUT_BUY") g_scenarios[i].type = BREAKOUT_BUY;
      else g_scenarios[i].type = UNKNOWN_TYPE;
      
      if(item["entry_zone"] != NULL)
        {
         g_scenarios[i].entry_min = item["entry_zone"]["min"].ToDbl();
         g_scenarios[i].entry_max = item["entry_zone"]["max"].ToDbl();
        }
        
      g_scenarios[i].trigger_price = item["trigger_price"].ToDbl();
      g_scenarios[i].sl = item["sl"].ToDbl();

      double plan_risk = json["max_risk_usd"].ToDbl();
      if(plan_risk <= 0.0)
         plan_risk = InpMaxRiskMoney;
      double sc_risk = item["max_risk_usd"].ToDbl();
      if(sc_risk <= 0.0)
         sc_risk = plan_risk;
      if(sc_risk > InpMaxRiskMoney)
         sc_risk = InpMaxRiskMoney;
      g_scenarios[i].max_risk_usd = sc_risk;
      
      CJAVal* tps = item["tps"];
      g_scenarios[i].tps_count = 0;
      if(tps != NULL && tps.type == jtARRAY)
        {
         if(tps.Size() > 2)
            Print("[SCENARIO] [", g_scenarios[i].id, "] có ", tps.Size(), " TP — áp dụng mô hình 2 TP tối ưu (bỏ qua TP3).");
         int tp_size = MathMin(2, tps.Size());
         for(int t=0; t<tp_size; t++)
           {
            g_scenarios[i].tps[t] = tps[t].ToDbl();
            g_scenarios[i].tps_count++;
           }
        }
        
      CJAVal* confirm = item.FindKey("confirmation");
      g_scenarios[i].patterns_count = 0;
      g_scenarios[i].confirm_timeframe = PERIOD_M15;
      if(confirm != NULL && confirm.type == jtOBJ)
        {
         g_scenarios[i].confirm_condition = confirm["condition"].ToStr();
         ENUM_TIMEFRAMES ctf = StringToTimeframe(confirm["timeframe"].ToStr());
         if(ctf != PERIOD_CURRENT)
            g_scenarios[i].confirm_timeframe = ctf;
         
         CJAVal* patterns = confirm.FindKey("patterns");
         if(patterns != NULL && patterns.type == jtARRAY)
           {
            int pat_size = MathMin(5, patterns.Size());
            for(int p=0; p<pat_size; p++)
              {
               g_scenarios[i].confirm_patterns[p] = patterns[p].ToStr();
               g_scenarios[i].patterns_count++;
              }
           }
        }

      g_scenarios[i].retest_patterns_count = 0;
      g_scenarios[i].retest_timeframe = OneStepSmallerTf(g_scenarios[i].confirm_timeframe);
      g_scenarios[i].retest_condition = "RETEST_THEN_CONTINUE";
      CJAVal* retest = item.FindKey("retest");
      if(retest != NULL && retest.type == jtOBJ)
        {
         g_scenarios[i].retest_condition = retest["condition"].ToStr();
         ENUM_TIMEFRAMES rtf = StringToTimeframe(retest["timeframe"].ToStr());
         if(rtf != PERIOD_CURRENT)
            g_scenarios[i].retest_timeframe = rtf;
         else
            g_scenarios[i].retest_timeframe = OneStepSmallerTf(g_scenarios[i].confirm_timeframe);

         CJAVal* rpatterns = retest.FindKey("patterns");
         if(rpatterns != NULL && rpatterns.type == jtARRAY)
           {
            int rpat_size = MathMin(5, rpatterns.Size());
            for(int p=0; p<rpat_size; p++)
              {
               g_scenarios[i].retest_patterns[p] = rpatterns[p].ToStr();
               g_scenarios[i].retest_patterns_count++;
              }
           }
        }
        
      string st = item["status"].ToStr();
      if(st == "PENDING") g_scenarios[i].status = PENDING;
      else g_scenarios[i].status = EXPIRED;
      
      g_scenarios[i].ready_to_confirm = false;
      g_scenarios[i].breakout_confirmed = false;
      g_scenarios[i].retest_touched = false;
      g_scenarios[i].confirm_wait_bars = 0;
      g_scenarios[i].retest_wait_bars = 0;
      g_scenarios[i].reversal_mid_filled = false;
      g_scenarios[i].reversal_confirm_filled = false;
      g_scenarios[i].planned_lots = 0.0;
      g_scenarios[i].filled_lots = 0.0;
      g_scenarios[i].last_bar_time = 0;
      g_scenarios[i].last_retest_bar_time = 0;
      
      Print(StringFormat(" -> [%d] ID: %s | Loại: %s | SL: %.3f | Risk: %.2f USD | TPs: %d | Confirm: %s | Retest: %s",
            i+1, g_scenarios[i].id, type_str, g_scenarios[i].sl, g_scenarios[i].max_risk_usd, g_scenarios[i].tps_count,
            TimeframeToString(g_scenarios[i].confirm_timeframe),
            TimeframeToString(g_scenarios[i].retest_timeframe)));
     }
     
   g_is_trading = false;
   g_active_scenario_index = -1;
   g_ea_stopped = false;
   g_trade_start_time = 0;
   
   Print("=== [NẠP HOÀN TẤT] Tổng cộng ", g_scenarios_count, " kịch bản đã sẵn sàng giám sát! ===");
   Print("--------------------------------------------------");
  }

//+------------------------------------------------------------------+
//| Tính tổng lợi nhuận của kịch bản sau khi các lệnh đóng           |
//+------------------------------------------------------------------+
double CheckScenarioProfit(datetime start_time) {
    HistorySelect(start_time, TimeCurrent() + 60);
    double total_profit = 0;
    for(int i = 0; i < HistoryDealsTotal(); i++) {
        ulong ticket = HistoryDealGetTicket(i);
        if(HistoryDealGetInteger(ticket, DEAL_MAGIC) == InpMagicNumber && HistoryDealGetString(ticket, DEAL_SYMBOL) == _Symbol) {
            total_profit += HistoryDealGetDouble(ticket, DEAL_PROFIT) + HistoryDealGetDouble(ticket, DEAL_COMMISSION) + HistoryDealGetDouble(ticket, DEAL_SWAP);
        }
    }
    return total_profit;
}

//+------------------------------------------------------------------+
//| Dời SL về hòa vốn thực tế (Entry +/- Offset) khi đạt 50% TP1     |
//| Khóa lãi lên mốc 50% TP1 khi TP1 đã cắn xong                      |
//+------------------------------------------------------------------+
void CheckAndMoveBreakeven(datetime start_time) {
    int profitable_deals = 0;
    HistorySelect(start_time, TimeCurrent() + 60);
    for(int i = 0; i < HistoryDealsTotal(); i++) {
        ulong ticket = HistoryDealGetTicket(i);
        if(HistoryDealGetInteger(ticket, DEAL_MAGIC) == InpMagicNumber && HistoryDealGetString(ticket, DEAL_SYMBOL) == _Symbol) {
            if(HistoryDealGetInteger(ticket, DEAL_ENTRY) == DEAL_ENTRY_OUT || HistoryDealGetInteger(ticket, DEAL_ENTRY) == DEAL_ENTRY_INOUT) {
                if(HistoryDealGetDouble(ticket, DEAL_PROFIT) > 0) {
                    profitable_deals++;
                }
            }
        }
    }
    
    int ai = g_active_scenario_index;
    double tp1 = (ai >= 0 && ai < g_scenarios_count && g_scenarios[ai].tps_count > 0) ? g_scenarios[ai].tps[0] : 0.0;
    
    double point  = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
    double offset = InpBreakevenOffset * point;
    double bid    = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    double ask    = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
    
    for(int i = PositionsTotal() - 1; i >= 0; i--) {
        ulong pos_ticket = PositionGetTicket(i);
        if(PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber) {
            double open_price = PositionGetDouble(POSITION_PRICE_OPEN);
            double current_sl = PositionGetDouble(POSITION_SL);
            double current_tp = PositionGetDouble(POSITION_TP);
            long   type       = PositionGetInteger(POSITION_TYPE);
            
            double tp_half = 0.0;
            if(tp1 > 0.0) {
                if(type == POSITION_TYPE_BUY)
                    tp_half = open_price + (tp1 - open_price) * 0.5;
                else
                    tp_half = open_price - (open_price - tp1) * 0.5;
            }
            
            bool reached_half = (profitable_deals >= 1);
            if(!reached_half && tp_half > 0.0) {
                if(type == POSITION_TYPE_BUY && bid >= tp_half) reached_half = true;
                else if(type == POSITION_TYPE_SELL && ask <= tp_half) reached_half = true;
            }
            
            if(!reached_half) continue;
            
            double target_sl = 0;
            string log_action = "";
            
            // Nếu đã chốt từ 2 lệnh trở lên (tức cả 50% TP1 và TP1 đều đã cắn)
            // Khóa lãi lệnh TP2 còn lại lên mốc 50% TP1
            if(InpLockTp1ToHalfOnTp1Hit && profitable_deals >= 2 && tp_half > 0.0) {
                target_sl = tp_half;
                log_action = "TP1 đã cắn 100%. Khóa lãi lệnh TP2 tại mốc 50% TP1 (" + DoubleToString(tp_half, _Digits) + ")";
            } else {
                // Đạt 50% TP1: Dời SL về hòa vốn (Entry +/- Offset)
                if(type == POSITION_TYPE_BUY)
                    target_sl = open_price + offset;
                else if(type == POSITION_TYPE_SELL)
                    target_sl = open_price - offset;
                log_action = "Đạt 50% TP1. Dời SL về hòa vốn (" + DoubleToString(target_sl, _Digits) + ")";
            }
            
            bool need_modify = false;
            if(type == POSITION_TYPE_BUY) {
                if(current_sl < target_sl - point) need_modify = true;
            } else if(type == POSITION_TYPE_SELL) {
                if(current_sl > target_sl + point || current_sl == 0) need_modify = true;
            }
            
            if(need_modify) {
                if(trade.PositionModify(pos_ticket, NormalizeDouble(target_sl, _Digits), current_tp)) {
                    Print(log_action, " cho lệnh #", pos_ticket);
                }
            }
        }
    }
}

//+------------------------------------------------------------------+
//| Đóng sạch toàn bộ vị thế của EA                                  |
//+------------------------------------------------------------------+
void CloseAllPositions(string reason) {
    Print("=> ĐANG THỰC HIỆN ĐÓNG SẠCH TOÀN BỘ VỊ THẾ: ", reason);
    for(int i = PositionsTotal() - 1; i >= 0; i--) {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0) continue;
        if(PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber) {
            if(!trade.PositionClose(ticket))
                Print("Lỗi đóng vị thế #", ticket, " | Error=", GetLastError());
        }
    }
}

//+------------------------------------------------------------------+
//| Kiểm tra nến khung nhỏ có tín hiệu ĐẢO CHIỀU ngược vị thế không  |
//| is_buy: Vị thế BUY -> tìm nến đảo chiều GIẢM                     |
//|         Vị thế SELL -> tìm nến đảo chiều TĂNG                    |
//+------------------------------------------------------------------+
bool DetectReversalSignalOnSmallTf(ENUM_TIMEFRAMES small_tf, bool is_buy) {
    double O1 = iOpen(_Symbol, small_tf, 1);
    double H1 = iHigh(_Symbol, small_tf, 1);
    double L1 = iLow(_Symbol, small_tf, 1);
    double C1 = iClose(_Symbol, small_tf, 1);

    double O2 = iOpen(_Symbol, small_tf, 2);
    double H2 = iHigh(_Symbol, small_tf, 2);
    double L2 = iLow(_Symbol, small_tf, 2);
    double C2 = iClose(_Symbol, small_tf, 2);

    double total_len = H1 - L1;
    if(total_len <= 0) total_len = _Point;

    double body = MathAbs(O1 - C1);
    double upper_wick = H1 - MathMax(O1, C1);
    double lower_wick = MathMin(O1, C1) - L1;

    if(is_buy) {
        // Đang BUY -> Tìm tín hiệu đảo chiều GIẢM
        bool bear_pin    = (upper_wick > 0.6 * total_len && body < 0.25 * total_len && C1 <= (H1 + L1) / 2.0);
        bool bear_eng    = (C2 > O2 && C1 < O1 && C1 < O2 && O1 > C2);
        bool strong_bear = (C1 < O1 && body / total_len > 0.70 && lower_wick / total_len < 0.15);
        bool break_low_2 = (C1 < L2 && C1 < O1 && body > 0.5 * total_len);

        if(bear_pin || bear_eng || strong_bear || break_low_2) {
            Print("=> [CẢNH BÁO ĐẢO CHIỀU GIẢM] Khung ", TimeframeToString(small_tf),
                  " phát hiện nến đảo chiều! (Pinbar=", bear_pin, ", Engulfing=", bear_eng,
                  ", StrongBear=", strong_bear, ", BreakLow=", break_low_2, ")");
            return true;
        }
    } else {
        // Đang SELL -> Tìm tín hiệu đảo chiều TĂNG
        bool bull_pin    = (lower_wick > 0.6 * total_len && body < 0.25 * total_len && C1 >= (H1 + L1) / 2.0);
        bool bull_eng    = (C2 < O2 && C1 > O1 && C1 > O2 && O1 < C2);
        bool strong_bull = (C1 > O1 && body / total_len > 0.70 && upper_wick / total_len < 0.15);
        bool break_high_2 = (C1 > H2 && C1 > O1 && body > 0.5 * total_len);

        if(bull_pin || bull_eng || strong_bull || break_high_2) {
            Print("=> [CẢNH BÁO ĐẢO CHIỀU TĂNG] Khung ", TimeframeToString(small_tf),
                  " phát hiện nến đảo chiều! (Pinbar=", bull_pin, ", Engulfing=", bull_eng,
                  ", StrongBull=", strong_bull, ", BreakHigh=", break_high_2, ")");
            return true;
        }
    }

    return false;
}

//+------------------------------------------------------------------+
//| Kiểm tra thoát sạch sớm khi khung nhỏ đảo chiều trước khi tới TP2|
//+------------------------------------------------------------------+
void CheckSmallTfReversalExit(int active_scenario_idx) {
    if(!InpEnableSmallTfExit) return;
    if(active_scenario_idx < 0 || active_scenario_idx >= g_scenarios_count) return;

    ENUM_TIMEFRAMES confirm_tf = g_scenarios[active_scenario_idx].confirm_timeframe;
    ENUM_TIMEFRAMES small_tf   = OneStepSmallerTf(confirm_tf);

    if(!IsNewBarOn(small_tf, g_last_small_tf_bar))
        return;

    bool has_positions = false;
    bool is_buy = false;
    double floating_profit = 0.0;

    for(int i = PositionsTotal() - 1; i >= 0; i--) {
        if(PositionGetString(POSITION_SYMBOL) == _Symbol && PositionGetInteger(POSITION_MAGIC) == InpMagicNumber) {
            has_positions = true;
            is_buy = (PositionGetInteger(POSITION_TYPE) == POSITION_TYPE_BUY);
            floating_profit += PositionGetDouble(POSITION_PROFIT) + PositionGetDouble(POSITION_SWAP);
        }
    }

    if(!has_positions) return;

    // Nếu chỉ thoát khi có lãi dương
    if(InpSmallTfExitInProfitOnly && floating_profit <= 0.0)
        return;

    if(DetectReversalSignalOnSmallTf(small_tf, is_buy)) {
        string reason = "Khung nhỏ " + TimeframeToString(small_tf) + 
                        " đảo chiều trước khi tới TP2 (Lãi mở: " + 
                        DoubleToString(floating_profit, 2) + " USD)";
        CloseAllPositions(reason);
    }
}

//+------------------------------------------------------------------+
//| Check Invalidation (Giá quét SL trước khi vào lệnh)              |
//+------------------------------------------------------------------+
bool CheckInvalidation(int index, double bid, double ask) {
    double sl = g_scenarios[index].sl;
    ENUM_SCENARIO_TYPE type = g_scenarios[index].type;
    
    bool invalid = false;
    if(type == REVERSAL_BUY || type == BREAKOUT_BUY) {
        if(bid <= sl) invalid = true;
    }
    else if(type == REVERSAL_SELL || type == BREAKOUT_SELL) {
        if(ask >= sl) invalid = true;
    }
    
    if(invalid) {
        g_scenarios[index].status = EXPIRED;
        Print("Kịch bản [", g_scenarios[index].id, "] bị EXPIRED vì giá chạm SL trước khi có xác nhận.");
        return true;
    }
    return false;
}

//+------------------------------------------------------------------+
//| Check Missed Opportunity (Giá chạm TP1 mà chưa vào lệnh)         |
//+------------------------------------------------------------------+
bool CheckMissedOpportunity(int index, double bid, double ask) {
    if(g_scenarios[index].tps_count == 0) return false;
    
    double tp1 = g_scenarios[index].tps[0];
    ENUM_SCENARIO_TYPE type = g_scenarios[index].type;
    
    bool missed = false;
    if(type == REVERSAL_SELL || type == BREAKOUT_SELL) {
        if(tp1 > 0 && bid <= tp1) missed = true;
    }
    else if(type == REVERSAL_BUY || type == BREAKOUT_BUY) {
        if(tp1 > 0 && ask >= tp1) missed = true;
    }
    
    if(missed) {
        g_scenarios[index].status = EXPIRED;
        Print("Kịch bản [", g_scenarios[index].id, "] bị EXPIRED vì giá đã chạm TP1 (", DoubleToString(tp1, _Digits), ") mà không có tín hiệu vào lệnh (Bỏ lỡ cơ hội).");
        return true;
    }
    return false;
}

//+------------------------------------------------------------------+
//| Nến mới trên khung bất kỳ (last_bar truyền theo từng phase)      |
//+------------------------------------------------------------------+
bool IsNewBarOn(ENUM_TIMEFRAMES tf, datetime &last_bar)
  {
   datetime current_time = iTime(_Symbol, tf, 0);
   if(current_time != last_bar)
     {
      if(last_bar != 0)
        {
         last_bar = current_time;
         return true;
        }
      last_bar = current_time;
     }
   return false;
  }

bool IsNewBar(int index)
  {
   return IsNewBarOn(g_scenarios[index].confirm_timeframe, g_scenarios[index].last_bar_time);
  }

//+------------------------------------------------------------------+
//| Pattern nến đóng [1] trên khung tf                               |
//+------------------------------------------------------------------+
bool CheckPatternOn(ENUM_TIMEFRAMES tf, string &patterns[], int count, bool is_buy)
  {
   if(count <= 0)
      return false;

   double O1 = iOpen(_Symbol, tf, 1);
   double H1 = iHigh(_Symbol, tf, 1);
   double L1 = iLow(_Symbol, tf, 1);
   double C1 = iClose(_Symbol, tf, 1);

   double O2 = iOpen(_Symbol, tf, 2);
   double C2 = iClose(_Symbol, tf, 2);

   double total_len = H1 - L1;
   if(total_len == 0)
      total_len = 0.00001;
   double body = MathAbs(O1 - C1);
   double upper_wick = H1 - MathMax(O1, C1);
   double lower_wick = MathMin(O1, C1) - L1;

   bool bull_pin = (lower_wick > 0.6 * total_len && body < 0.25 * total_len && C1 >= (H1 + L1) / 2.0);
   bool bear_pin = (upper_wick > 0.6 * total_len && body < 0.25 * total_len && C1 <= (H1 + L1) / 2.0);
   bool bull_eng = (C2 < O2 && C1 > O1 && C1 > O2 && O1 < C2);
   bool bear_eng = (C2 > O2 && C1 < O1 && C1 < O2 && O1 > C2);

   for(int p = 0; p < count; p++)
     {
      string pat = patterns[p];
      StringToUpper(pat);

      if(pat == "PINBAR")
        {
         if(is_buy ? bull_pin : bear_pin)
            return true;
        }
      else if(pat == "BULLISH_PINBAR")
        {
         if(is_buy && bull_pin)
            return true;
        }
      else if(pat == "BEARISH_PINBAR")
        {
         if(!is_buy && bear_pin)
            return true;
        }
      else if(pat == "ENGULFING")
        {
         if(is_buy ? bull_eng : bear_eng)
            return true;
        }
      else if(pat == "BULLISH_ENGULFING")
        {
         if(is_buy && bull_eng)
            return true;
        }
      else if(pat == "BEARISH_ENGULFING")
        {
         if(!is_buy && bear_eng)
            return true;
        }
      else if(pat == "STRONG_BEARISH_MARUBOZU" || pat == "STRONG_BEARISH" || (pat == "MARUBOZU" && !is_buy))
        {
         if(!is_buy && C1 < O1 && body / total_len > 0.75 && lower_wick / total_len < 0.10)
            return true;
        }
      else if(pat == "STRONG_BULLISH_MARUBOZU" || pat == "STRONG_BULLISH" || (pat == "MARUBOZU" && is_buy))
        {
         if(is_buy && C1 > O1 && body / total_len > 0.75 && upper_wick / total_len < 0.10)
            return true;
        }
      else if(pat == "NO_LONG_LOWER_WICK")
        {
         if(lower_wick / total_len < 0.15)
            return true;
        }
     }
   return false;
  }

bool CheckPattern(int index)
  {
   bool is_buy = (g_scenarios[index].type == REVERSAL_BUY || g_scenarios[index].type == BREAKOUT_BUY);
   return CheckPatternOn(g_scenarios[index].confirm_timeframe,
                         g_scenarios[index].confirm_patterns,
                         g_scenarios[index].patterns_count,
                         is_buy);
  }

double CurrentSpreadPrice()
  {
   return SymbolInfoDouble(_Symbol, SYMBOL_ASK) - SymbolInfoDouble(_Symbol, SYMBOL_BID);
  }

double MinBreakDistance()
  {
   double tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(tick <= 0.0)
      tick = _Point;
   double spread = CurrentSpreadPrice();
   return MathMax(3.0 * spread, 8.0 * tick);
  }

double ZonePad(double min_z, double max_z)
  {
   double width = MathAbs(max_z - min_z);
   return MathMax(width, MinBreakDistance() * 3.0);
  }

bool PriceTaggedZone(ENUM_TIMEFRAMES tf, double min_z, double max_z, double bid, double ask)
  {
   if((bid >= min_z && bid <= max_z) || (ask >= min_z && ask <= max_z))
      return true;
   double h = iHigh(_Symbol, tf, 0);
   double l = iLow(_Symbol, tf, 0);
   return !(h < min_z || l > max_z);
  }

// Zone mỏng (FVG): nến confirm được đóng NGOÀI zone. Chỉ cần phản ứng gần zone, không xuyên thủng.
bool CandleReactedFromZone(ENUM_TIMEFRAMES tf, double min_z, double max_z, bool is_buy)
  {
   double h = iHigh(_Symbol, tf, 1);
   double l = iLow(_Symbol, tf, 1);
   double c = iClose(_Symbol, tf, 1);
   double pad = ZonePad(min_z, max_z);

   if(h < min_z - pad || l > max_z + pad)
      return false;
   if(is_buy && c < min_z - pad)
      return false;
   if(!is_buy && c > max_z + pad)
      return false;
   return true;
  }

bool PriceTouchedMid(ENUM_TIMEFRAMES tf, double min_z, double max_z, double bid, double ask, bool is_buy)
  {
   double mid = (min_z + max_z) / 2.0;
   if(is_buy)
     {
      if(bid <= mid && bid >= min_z)
         return true;
      if(ask <= mid && ask >= min_z)
         return true;
     }
   else
     {
      if(ask >= mid && ask <= max_z)
         return true;
      if(bid >= mid && bid <= max_z)
         return true;
     }

   double h = iHigh(_Symbol, tf, 0);
   double l = iLow(_Symbol, tf, 0);
   if(l > mid || h < mid)
      return false;
   return is_buy ? (l >= min_z) : (h <= max_z);
  }

bool ClosedCandleTouched(ENUM_TIMEFRAMES tf, double trigger, bool is_sell)
  {
   double h = iHigh(_Symbol, tf, 1);
   double l = iLow(_Symbol, tf, 1);
   return is_sell ? (h >= trigger) : (l <= trigger);
  }

bool DirectionalClose(ENUM_TIMEFRAMES tf, bool is_buy)
  {
   double o = iOpen(_Symbol, tf, 1);
   double c = iClose(_Symbol, tf, 1);
   return is_buy ? (c > o) : (c < o);
  }

bool ConfirmPatternOrDirection(ENUM_TIMEFRAMES tf, string &patterns[], int count, bool is_buy)
  {
   if(count > 0)
      return CheckPatternOn(tf, patterns, count, is_buy);
   return DirectionalClose(tf, is_buy);
  }

//+------------------------------------------------------------------+
//| Làm tròn lot THEO BƯỚC SÀN, làm tròn xuống để không vượt rủi ro  |
//+------------------------------------------------------------------+
double NormalizeVolumeFloor(double lots)
  {
   double vmin = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double vmax = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   if(step <= 0.0)
      step = 0.01;
   lots = MathFloor(lots / step + 1e-12) * step;
   lots = NormalizeDouble(lots, 8);
   if(lots > vmax)
      lots = vmax;
   if(lots > 0.0 && lots < vmin)
      lots = 0.0;
   return lots;
  }

//+------------------------------------------------------------------+
//| Lot tối đa sao cho lỗ khi cắn SL < InpMaxRiskMoney               |
//+------------------------------------------------------------------+
double CalcMaxLotsForRisk(ENUM_ORDER_TYPE order_type, double entry, double sl, double max_risk)
  {
   if(max_risk <= 0.0)
      return 0.0;
   double sl_dist = MathAbs(entry - sl);
   if(sl_dist < SymbolInfoDouble(_Symbol, SYMBOL_POINT))
      return 0.0;

   double loss_1lot = 0.0;
   if(!OrderCalcProfit(order_type, _Symbol, 1.0, entry, sl, loss_1lot))
     {
      double tick_size  = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
      double tick_value = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
      if(tick_size <= 0.0)
         return 0.0;
      loss_1lot = -(sl_dist / tick_size) * tick_value;
     }

   double per_lot = MathAbs(loss_1lot);
   if(per_lot <= 0.0)
      return 0.0;

   double lots = NormalizeVolumeFloor(max_risk / per_lot);
   double step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
   double vmin = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   if(step <= 0.0)
      step = 0.01;

   double actual = 0.0;
   while(lots >= vmin)
     {
      if(!OrderCalcProfit(order_type, _Symbol, lots, entry, sl, actual))
         break;
      if(MathAbs(actual) <= max_risk + 0.01)
         break;
      lots = NormalizeVolumeFloor(lots - step);
     }

   if(lots < vmin)
      return 0.0;
   return lots;
  }

//+------------------------------------------------------------------+
//| Tổng lỗ USD nếu các lệnh đang mở cùng magic cắn SL               |
//+------------------------------------------------------------------+
double SumOpenPositionsSlRisk(double sl)
  {
   double total = 0.0;
   for(int i = PositionsTotal() - 1; i >= 0; i--)
     {
      if(PositionGetSymbol(i) != _Symbol)
         continue;
      if(PositionGetInteger(POSITION_MAGIC) != InpMagicNumber)
         continue;
      double vol  = PositionGetDouble(POSITION_VOLUME);
      double open = PositionGetDouble(POSITION_PRICE_OPEN);
      double pos_sl = PositionGetDouble(POSITION_SL);
      double sl_use = (pos_sl > 0.0) ? pos_sl : sl;
      long ptype = PositionGetInteger(POSITION_TYPE);
      ENUM_ORDER_TYPE ot = (ptype == POSITION_TYPE_BUY) ? ORDER_TYPE_BUY : ORDER_TYPE_SELL;
      double profit = 0.0;
      if(!OrderCalcProfit(ot, _Symbol, vol, open, sl_use, profit))
         continue;
      total += MathAbs(profit);
     }
   return total;
  }

//+------------------------------------------------------------------+
//| Chia volume theo mô hình 2 TP tối ưu:                           |
//| - Có TP2: 35% tại 50% TP1, 35% tại TP1, 30% tại TP2              |
//| - Chỉ TP1: 50% tại 50% TP1, 50% tại TP1                          |
//+------------------------------------------------------------------+
void Split2TpVolumes(double total_lots, bool has_tp2, double &vols[], double &targets[], 
                     string &comments[], string scenario_id, string wave,
                     double entry, double tp1, double tp2, bool is_buy)
  {
   double vmin = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double tp_half = 0.0;
   if(tp1 > 0.0)
     {
      if(is_buy)
         tp_half = NormalizeDouble(entry + (tp1 - entry) * 0.5, _Digits);
      else
         tp_half = NormalizeDouble(entry - (entry - tp1) * 0.5, _Digits);
     }

   if(has_tp2 && tp2 > 0.0)
     {
      ArrayResize(vols, 3);
      ArrayResize(targets, 3);
      ArrayResize(comments, 3);
      ArrayInitialize(vols, 0.0);

      targets[0] = tp_half;
      targets[1] = tp1;
      targets[2] = tp2;

      comments[0] = "AI_" + scenario_id + "_TP1_HALF_" + wave;
      comments[1] = "AI_" + scenario_id + "_TP1_" + wave;
      comments[2] = "AI_" + scenario_id + "_TP2_" + wave;

      // 35% - 35% - 30%
      vols[0] = NormalizeVolumeFloor(total_lots * 0.35);
      vols[1] = NormalizeVolumeFloor(total_lots * 0.35);
      vols[2] = NormalizeDouble(total_lots - vols[0] - vols[1], 8);

      if(total_lots < 2.0 * vmin)
        {
         vols[0] = total_lots;
         vols[1] = 0.0;
         vols[2] = 0.0;
         targets[0] = tp1;
         comments[0] = "AI_" + scenario_id + "_TP1_" + wave;
        }
      else if(total_lots < 3.0 * vmin || vols[0] < vmin || vols[1] < vmin || vols[2] < vmin)
        {
         // Chỉ đủ 2 lệnh: 1 lệnh 50% TP1, 1 lệnh TP2
         vols[0] = NormalizeVolumeFloor(total_lots * 0.50);
         if(vols[0] < vmin) vols[0] = vmin;
         vols[1] = NormalizeDouble(total_lots - vols[0], 8);
         vols[2] = 0.0;
         targets[0] = tp_half;
         targets[1] = tp2;
         comments[0] = "AI_" + scenario_id + "_TP1_HALF_" + wave;
         comments[1] = "AI_" + scenario_id + "_TP2_" + wave;
        }
     }
   else
     {
      ArrayResize(vols, 2);
      ArrayResize(targets, 2);
      ArrayResize(comments, 2);
      ArrayInitialize(vols, 0.0);

      targets[0] = tp_half;
      targets[1] = tp1;

      comments[0] = "AI_" + scenario_id + "_TP1_HALF_" + wave;
      comments[1] = "AI_" + scenario_id + "_TP1_" + wave;

      vols[0] = NormalizeVolumeFloor(total_lots * 0.50);
      vols[1] = NormalizeDouble(total_lots - vols[0], 8);

      if(total_lots < 2.0 * vmin || vols[0] < vmin || vols[1] < vmin)
        {
         vols[0] = total_lots;
         vols[1] = 0.0;
         targets[0] = tp1;
         comments[0] = "AI_" + scenario_id + "_TP1_" + wave;
        }
     }
  }

//+------------------------------------------------------------------+
//| wave: "FULL" = 100% | "MID" = 50% | "REST" = phần lot còn lại    |
//+------------------------------------------------------------------+
void ExecuteTradeWave(int index, string wave)
  {
   ENUM_SCENARIO_TYPE type = g_scenarios[index].type;
   double sl = g_scenarios[index].sl;
   bool is_buy = (type == REVERSAL_BUY || type == BREAKOUT_BUY);
   bool is_sell = (type == REVERSAL_SELL || type == BREAKOUT_SELL);
   if(!is_buy && !is_sell)
      return;

   double entry = is_buy ? SymbolInfoDouble(_Symbol, SYMBOL_ASK)
                         : SymbolInfoDouble(_Symbol, SYMBOL_BID);
   ENUM_ORDER_TYPE order_type = is_buy ? ORDER_TYPE_BUY : ORDER_TYPE_SELL;

   if((is_buy && sl >= entry) || (is_sell && sl <= entry))
     {
      Print("CẢNH BÁO: SL không đúng phía so với giá vào. Bỏ kịch bản [", g_scenarios[index].id, "].");
      g_scenarios[index].status = EXPIRED;
      return;
     }

   double max_risk = g_scenarios[index].max_risk_usd;
   if(max_risk <= 0.0 || max_risk > InpMaxRiskMoney)
      max_risk = InpMaxRiskMoney;
   double vmin = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);

   double wave_risk = max_risk;
   if(wave == "MID")
      wave_risk = max_risk * 0.5;
   else if(wave == "REST")
     {
      double used_risk = SumOpenPositionsSlRisk(sl);
      wave_risk = max_risk - used_risk;
      if(wave_risk > max_risk * 0.5)
         wave_risk = max_risk * 0.5;
      if(wave_risk < 0.01)
        {
         Print("Reversal [", g_scenarios[index].id, "] hết ngân sách rủi ro (đã dùng ",
               DoubleToString(used_risk, 2), "/", DoubleToString(max_risk, 2),
               " USD). Không vào nửa confirm.");
         g_scenarios[index].reversal_confirm_filled = true;
         g_scenarios[index].status = COMPLETED;
         return;
        }
     }

   double lots = CalcMaxLotsForRisk(order_type, entry, sl, wave_risk);
   if(lots < vmin)
     {
      Print("CẢNH BÁO: Không vào lệnh [", g_scenarios[index].id, "] wave ", wave,
            " vì lot tối thiểu đã vượt rủi ro còn lại ", DoubleToString(wave_risk, 2),
            " USD (SL=", DoubleToString(sl, _Digits), " Entry=", DoubleToString(entry, _Digits), ").");
      if(wave == "REST")
        {
         g_scenarios[index].reversal_confirm_filled = true;
         g_scenarios[index].status = COMPLETED;
        }
      return;
     }

   double tp1 = (g_scenarios[index].tps_count > 0) ? g_scenarios[index].tps[0] : 0.0;
   double tp2 = (g_scenarios[index].tps_count > 1) ? g_scenarios[index].tps[1] : 0.0;
   bool has_tp2 = (g_scenarios[index].tps_count > 1 && tp2 > 0.0);

   double vols[];
   double targets[];
   string comments[];
   Split2TpVolumes(lots, has_tp2, vols, targets, comments, g_scenarios[index].id, wave, entry, tp1, tp2, is_buy);

   double est_risk = 0.0;
   if(!OrderCalcProfit(order_type, _Symbol, lots, entry, sl, est_risk))
      est_risk = 0.0;

   Print("=> [RISK] ", _Symbol, " wave=", wave,
         " tick=", DoubleToString(SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE), 4),
         " USD | Trần=", DoubleToString(max_risk, 2),
         " | Ngân sách wave=", DoubleToString(wave_risk, 2),
         " USD | Lỗ ước lượng wave này=", DoubleToString(MathAbs(est_risk), 2),
         " USD | Lot=", DoubleToString(lots, 2),
         " | Entry=", DoubleToString(entry, _Digits),
         " | SL=", DoubleToString(sl, _Digits));

   int success_orders = 0;
   double filled_now = 0.0;
   int num_orders = ArraySize(vols);
   for(int t = 0; t < num_orders; t++)
     {
      if(vols[t] < vmin)
         continue;
      double tp = targets[t];
      string comment = comments[t];

      bool res = false;
      if(is_buy)
         res = trade.Buy(vols[t], _Symbol, 0, sl, tp, comment);
      else
         res = trade.Sell(vols[t], _Symbol, 0, sl, tp, comment);
      if(res)
        {
         success_orders++;
         filled_now += vols[t];
        }
      else
         Print("Lỗi gửi lệnh ", comment, " lot=", vols[t], " | ", GetLastError());
     }

   if(success_orders > 0)
     {
      g_scenarios[index].filled_lots = NormalizeDouble(g_scenarios[index].filled_lots + filled_now, 8);
      g_is_trading = true;
      if(g_trade_start_time == 0)
         g_trade_start_time = TimeCurrent();
      g_active_scenario_index = index;

      if(wave == "MID")
        {
         g_scenarios[index].reversal_mid_filled = true;
         g_scenarios[index].status = ACTIVE;
         Print("Đã vào NỬA ĐẦU [", g_scenarios[index].id, "] tại giữa zone. SL đầy đủ ",
               DoubleToString(sl, _Digits), " | lot=", DoubleToString(filled_now, 2),
               " | rủi ro wave ≈ ", DoubleToString(MathAbs(est_risk), 2),
               " USD. Nửa confirm sẽ tính lot theo rủi ro còn lại tại giá mới, không copy lot.");
        }
      else
        {
         g_scenarios[index].reversal_confirm_filled = true;
         g_scenarios[index].status = COMPLETED;
         Print("Đã thực thi [", g_scenarios[index].id, "] wave ", wave, " với ", success_orders,
               " lệnh. Lot cộng dồn=", DoubleToString(g_scenarios[index].filled_lots, 2),
               ". Theo dõi lệnh...");
        }
     }
   else
     {
      Print("CẢNH BÁO: Không thể gửi lệnh cho kịch bản [", g_scenarios[index].id, "] wave ",
            wave, ". Mã lỗi: ", GetLastError());
     }
  }

void ExecuteTrade(int index)
  {
   ExecuteTradeWave(index, "FULL");
  }

//+------------------------------------------------------------------+
//| Xử lý Luồng REVERSAL                                             |
//+------------------------------------------------------------------+
void ProcessReversalScenario(int index, double bid, double ask) {
    double min_z = g_scenarios[index].entry_min;
    double max_z = g_scenarios[index].entry_max;
    bool is_buy = (g_scenarios[index].type == REVERSAL_BUY);
    ENUM_TIMEFRAMES tf = g_scenarios[index].confirm_timeframe;
    double mid = (min_z + max_z) / 2.0;

    if(!g_scenarios[index].reversal_mid_filled)
      {
       if(!PriceTouchedMid(tf, min_z, max_z, bid, ask, is_buy))
          return;

       g_scenarios[index].ready_to_confirm = true;
       g_scenarios[index].confirm_wait_bars = 0;
       g_scenarios[index].last_bar_time = iTime(_Symbol, tf, 0);
       Print("Kịch bản [", g_scenarios[index].id, "] chạm giữa zone ",
             DoubleToString(mid, _Digits), " (", DoubleToString(min_z, _Digits), "-",
             DoubleToString(max_z, _Digits), "). Vào 50% lot, SL đầy đủ.");
       ExecuteTradeWave(index, "MID");
       return;
      }

    if(g_scenarios[index].reversal_confirm_filled)
       return;

    if(!IsNewBar(index))
        return;

    g_scenarios[index].confirm_wait_bars++;
    if(InpMaxConfirmBars > 0 && g_scenarios[index].confirm_wait_bars > InpMaxConfirmBars)
      {
       g_scenarios[index].reversal_confirm_filled = true;
       g_scenarios[index].status = COMPLETED;
       Print("Kịch bản [", g_scenarios[index].id, "] hết chờ confirm (", InpMaxConfirmBars,
             " nến ", TimeframeToString(tf), "). Giữ nửa lot đã vào, không vào nửa còn lại.");
       return;
      }

    if(!CandleReactedFromZone(tf, min_z, max_z, is_buy))
      {
       Print("Reversal [", g_scenarios[index].id, "] nến confirm không còn phản ứng gần zone (pad=",
             DoubleToString(ZonePad(min_z, max_z), _Digits), "). Chờ nến tiếp.");
       return;
      }

    if(!ConfirmPatternOrDirection(tf, g_scenarios[index].confirm_patterns,
                                  g_scenarios[index].patterns_count, is_buy))
      {
       Print("Reversal [", g_scenarios[index].id, "] gần zone nhưng chưa khớp pattern/nến thuận. Chờ nến tiếp.");
       return;
      }

    Print("Kịch bản [", g_scenarios[index].id, "] Confirm OK. Vào nửa lot còn lại, SL đầy đủ.");
    ExecuteTradeWave(index, "REST");
}

//+------------------------------------------------------------------+
//| Xử lý Luồng BREAKOUT: confirm → chờ retest → nến tiếp diễn       |
//+------------------------------------------------------------------+
void ProcessBreakoutScenario(int index, double bid, double ask)
  {
   double trigger = g_scenarios[index].trigger_price;
   bool is_sell = (g_scenarios[index].type == BREAKOUT_SELL);
   bool is_buy  = !is_sell;
   ENUM_TIMEFRAMES confirm_tf = g_scenarios[index].confirm_timeframe;
   ENUM_TIMEFRAMES retest_tf  = g_scenarios[index].retest_timeframe;
   if(retest_tf == PERIOD_CURRENT)
      retest_tf = OneStepSmallerTf(confirm_tf);
   double min_break = MinBreakDistance();

   // 1) Nến confirm ĐÓNG phá dứt khoát (không đuổi, không phá bằng râu)
   if(!g_scenarios[index].breakout_confirmed)
     {
      if(IsNewBarOn(confirm_tf, g_scenarios[index].last_bar_time))
        {
         double close1 = iClose(_Symbol, confirm_tf, 1);
         bool broke = is_sell ? (close1 < trigger - min_break)
                              : (close1 > trigger + min_break);
         if(!broke)
            return;

         if(!DirectionalClose(confirm_tf, is_buy))
           {
            Print("Breakout [", g_scenarios[index].id, "] đóng qua trigger nhưng nến không thuận chiều. Chờ nến ",
                  TimeframeToString(confirm_tf), " tiếp.");
            return;
           }

         if(g_scenarios[index].patterns_count > 0
            && !CheckPatternOn(confirm_tf, g_scenarios[index].confirm_patterns, g_scenarios[index].patterns_count, is_buy))
           {
            Print("Breakout [", g_scenarios[index].id, "] đóng phá trigger nhưng chưa khớp pattern confirm. Chờ nến ",
                  TimeframeToString(confirm_tf), " tiếp.");
            return;
           }

         g_scenarios[index].breakout_confirmed = true;
         g_scenarios[index].retest_touched = false;
         g_scenarios[index].retest_wait_bars = 0;
         g_scenarios[index].last_retest_bar_time = iTime(_Symbol, retest_tf, 0);
         Print("Breakout [", g_scenarios[index].id, "] CONFIRMED: ", TimeframeToString(confirm_tf),
               " đóng ", DoubleToString(close1, _Digits), " qua trigger ", DoubleToString(trigger, _Digits),
               " (min break ", DoubleToString(min_break, _Digits), "). Không đuổi. Chờ nến ",
               TimeframeToString(retest_tf), " đóng retest+tiếp diễn.");
        }
      return;
     }

   // 2) Nến retest ĐÓNG: chạm trigger + vẫn đúng phía phá + pattern/nến thuận → vào lệnh
   if(!IsNewBarOn(retest_tf, g_scenarios[index].last_retest_bar_time))
      return;

   g_scenarios[index].retest_wait_bars++;
   if(InpMaxRetestBars > 0 && g_scenarios[index].retest_wait_bars > InpMaxRetestBars)
     {
      g_scenarios[index].status = EXPIRED;
      Print("Breakout [", g_scenarios[index].id, "] EXPIRED: quá ", InpMaxRetestBars,
            " nến ", TimeframeToString(retest_tf), " sau phá mà chưa retest hợp lệ.");
      return;
     }

   double close1 = iClose(_Symbol, retest_tf, 1);
   bool still_broken = is_sell ? (close1 < trigger) : (close1 > trigger);
   if(!still_broken)
     {
      Print("Breakout [", g_scenarios[index].id, "] retest FAIL: nến ", TimeframeToString(retest_tf),
            " đóng ngược lại trigger. Reset, chờ phá lại.");
      g_scenarios[index].breakout_confirmed = false;
      g_scenarios[index].retest_touched = false;
      g_scenarios[index].retest_wait_bars = 0;
      return;
     }

   if(!ClosedCandleTouched(retest_tf, trigger, is_sell))
     {
      Print("Breakout [", g_scenarios[index].id, "] nến ", TimeframeToString(retest_tf),
            " chưa chạm lại trigger. Chờ hồi.");
      return;
     }

   g_scenarios[index].retest_touched = true;
   if(!ConfirmPatternOrDirection(retest_tf, g_scenarios[index].retest_patterns,
                                 g_scenarios[index].retest_patterns_count, is_buy))
     {
      Print("Breakout [", g_scenarios[index].id, "] đã chạm trigger nhưng chưa có nến tiếp diễn. Chờ nến ",
            TimeframeToString(retest_tf), " tiếp.");
      return;
     }

   Print("Breakout [", g_scenarios[index].id, "] RETEST CONTINUE trên ", TimeframeToString(retest_tf),
         " (nến đóng chạm trigger + thuận chiều). Vào lệnh.");
   ExecuteTrade(index);
  }

//+------------------------------------------------------------------+
//| Giám sát và thực thi kế hoạch giao dịch                          |
//+------------------------------------------------------------------+
void MonitorScenarios() {
    if(IsDayStopped()) return;
    if(g_ea_stopped && !g_is_trading) return;
    
    double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
    
    // NẾU ĐANG CÓ LỆNH -> Theo dõi lệnh & dời SL về hòa vốn / thoát đảo chiều
    if(g_is_trading) {
        if(HasOpenPositions()) {
            CheckAndMoveBreakeven(g_trade_start_time);
            CheckSmallTfReversalExit(g_active_scenario_index);
            
            if(!HasOpenPositions()) {
                // Đã đóng sạch lệnh (ví dụ do phát hiện đảo chiều khung nhỏ) -> rơi xuống xử lý PnL bên dưới
            } else {
                int ai = g_active_scenario_index;
                if(ai >= 0 && ai < g_scenarios_count
                   && (g_scenarios[ai].type == REVERSAL_BUY || g_scenarios[ai].type == REVERSAL_SELL)
                   && g_scenarios[ai].reversal_mid_filled
                   && !g_scenarios[ai].reversal_confirm_filled)
                  {
                   ProcessReversalScenario(ai, bid, ask);
                  }
                return;
            }
        }
        
        // Lệnh đã đóng hết -> Kiểm tra kết quả Lời/Lỗ
        double net_profit = CheckScenarioProfit(g_trade_start_time);
        int ai = g_active_scenario_index;
        string sid = (ai >= 0 && ai < g_scenarios_count) ? g_scenarios[ai].id : "?";
        if(net_profit > 0) {
            if(ai >= 0 && ai < g_scenarios_count)
              {
               g_scenarios[ai].status = COMPLETED;
               g_scenarios[ai].reversal_confirm_filled = true;
              }
            Print("Kịch bản [", sid, "] đã chốt lời (WIN) với profit: ", net_profit, ". Hoàn thành mục tiêu chu kỳ này.");
            g_ea_stopped = true;
            g_is_trading = false;
            g_active_scenario_index = -1;
            g_trade_start_time = 0;
            g_last_small_tf_bar = 0;

            CheckDailyTargetProfit();
        } else {
            if(ai >= 0 && ai < g_scenarios_count)
              {
               g_scenarios[ai].status = EXPIRED;
               g_scenarios[ai].reversal_confirm_filled = true;
              }
            Print("Kịch bản [", sid, "] bị SL/lỗ (LOSS) profit: ", net_profit,
                  ". Hủy các kịch bản đang chờ để tránh nhồi lệnh.");

            for(int k = 0; k < g_scenarios_count; k++)
              {
               if(g_scenarios[k].status == PENDING)
                  g_scenarios[k].status = EXPIRED;
              }

            g_is_trading = false;
            g_active_scenario_index = -1;
            g_trade_start_time = 0;
            g_last_small_tf_bar = 0;

            if(InpLossCooldownMinutes > 0)
              {
               g_last_loss_time = TimeCurrent();
               Print(StringFormat("=> [LOSS COOLDOWN] Kịch bản [%s] bị lỗ (%.2f USD). Kích hoạt thời gian nghỉ %d phút để tránh nhồi lệnh liên tục (nghỉ đến %s).",
                     sid, net_profit, InpLossCooldownMinutes,
                     TimeToString(g_last_loss_time + InpLossCooldownMinutes * 60, TIME_DATE|TIME_MINUTES)));
              }

            CheckDailyTargetProfit();
        }
        return;
    }
    
    // NẾU KHÔNG CÓ LỆNH:
    // Nếu đang trong thời gian nghỉ sau lỗ -> không kích hoạt bất kỳ kịch bản nào
    if(IsInLossCooldown()) return;

    // Giám sát đồng thời các kịch bản đang PENDING
    for(int i = 0; i < g_scenarios_count; i++) {
        if(g_scenarios[i].status != PENDING) continue;
        
        if(CheckInvalidation(i, bid, ask)) continue;
        if(CheckMissedOpportunity(i, bid, ask)) continue;
        
        if(g_scenarios[i].type == REVERSAL_BUY || g_scenarios[i].type == REVERSAL_SELL) {
            ProcessReversalScenario(i, bid, ask);
        }
        else if(g_scenarios[i].type == BREAKOUT_BUY || g_scenarios[i].type == BREAKOUT_SELL) {
            ProcessBreakoutScenario(i, bid, ask);
        }
        
        if(g_is_trading) {
            g_active_scenario_index = i;
            break;
        }
    }
}