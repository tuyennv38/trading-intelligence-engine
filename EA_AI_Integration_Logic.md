# Hướng dẫn tích hợp AI và EA trên MT5 (JSON & Logic)

Tài liệu này mô tả chi tiết cấu trúc dữ liệu và luồng thuật toán để một EA (Expert Advisor) trên MT5 có thể đọc, hiểu và thực thi các kịch bản giao dịch do AI tạo ra.

## PHẦN 1: CẤU TRÚC JSON KỊCH BẢN TỪ AI

Định dạng JSON tiêu chuẩn mà AI cần xuất ra để EA có thể dễ dàng phân tích (parse):

```json
{
  "signal_id": "20261012_SPX_01",
  "symbol": "SPX500",
  "bias": "Bearish",
  "context": "Xu hướng chính trên khung vĩ mô H4...",
  "scenarios": [
    {
      "id": "scenario_sell_1",
      "type": "REVERSAL_SELL",
      "name": "Kháng cự ngắn hạn M5 CHOCH",
      "entry_zone": {
        "min": 4178.42,
        "max": 4181.42
      },
      "sl": 4186.42,
      "tps": [4167.94, 4143.07, 4126.77],
      "confirmation": {
        "condition": "PRICE_IN_ZONE",
        "timeframe": "M5",
        "patterns": ["BEARISH_PINBAR", "BEARISH_ENGULFING"]
      },
      "status": "PENDING"
    },
    {
      "id": "scenario_buy_1",
      "type": "REVERSAL_BUY",
      "name": "Hỗ trợ vĩ mô H4 CHOCH / H1 BOS",
      "entry_zone": {
        "min": 4143.07,
        "max": 4146.07
      },
      "sl": 4138.07,
      "tps": [4167.94, 4178.42, 4184.65],
      "confirmation": {
        "condition": "PRICE_IN_ZONE",
        "timeframe": "M5",
        "patterns": ["BULLISH_PINBAR", "BULLISH_ENGULFING"]
      },
      "status": "PENDING"
    },
    {
      "id": "scenario_breakout_1",
      "type": "BREAKOUT_SELL",
      "name": "Breakdown SELL",
      "trigger_price": 4167.94,
      "sl": 4175.94,
      "tps": [4143.07, 4126.77, 4103.95],
      "confirmation": {
        "condition": "CLOSE_BELOW_TRIGGER",
        "timeframe": "M15", 
        "patterns": ["STRONG_BEARISH_MARUBOZU", "NO_LONG_LOWER_WICK"]
      },
      "status": "PENDING"
    }
  ]
}
```

**Giải thích các trường quan trọng:**
*   `type`: Phân loại chiến lược (`REVERSAL_BUY`/`REVERSAL_SELL` cho bắt đỉnh đáy, `BREAKOUT_BUY`/`BREAKOUT_SELL` cho phá vỡ).
*   `entry_zone`: Vùng giá canh lệnh (dành cho Reversal).
*   `trigger_price`: Mốc giá cần phá vỡ (dành cho Breakout).
*   `confirmation`: Cấu hình xác nhận nến (Chờ ở khung thời gian nào, cần mô hình nến gì).

---

## PHẦN 2: LOGIC MÔ TẢ ĐỂ CODE EA TRÊN MT5

Luồng thuật toán (Flowchart Logic) dành cho Coder MQL5 để lập trình EA xử lý JSON trên.

### Bước 1: Tiếp nhận và nạp kịch bản (Initialization)
1. EA liên tục gọi API (hoặc đọc file JSON) để lấy kịch bản mới nhất từ AI.
2. Sử dụng thư viện `JAson` (có sẵn cho MQL5) để chuyển JSON thành các mảng Object (Struct) trong bộ nhớ của EA.
3. Lọc các `scenarios` có `status == "PENDING"`.

### Bước 2: Giám sát Giá thực tế (Hàm OnTick)
EA sẽ quét qua mảng các kịch bản đang PENDING mỗi khi có tick giá mới. Sẽ chia làm 2 luồng xử lý tùy theo `type`:

**Luồng A: Xử lý `REVERSAL_BUY` / `REVERSAL_SELL` (Canh vùng giá)**
1. **Kiểm tra vùng chờ:** Giá hiện tại (Bid/Ask) đã rơi vào vùng `entry_zone.min` đến `entry_zone.max` chưa?
   * *Nếu chưa:* Bỏ qua, chờ tiếp.
   * *Nếu rồi:* Kích hoạt trạng thái **"Sẵn sàng xác nhận nến"** (Ready to Confirm).
2. **Theo dõi nến xác nhận:** Khi ở trạng thái "Sẵn sàng", EA sẽ không vào lệnh ngay mà chuyển sang theo dõi sự kiện **Đóng nến (IsNewBar)** của khung thời gian quy định (ví dụ: `M5`).
3. Khi một cây nến `M5` vừa đóng lại ngay trong/gần vùng Entry, EA gọi hàm kiểm tra mô hình nến:
   * *Nếu có `BEARISH_PINBAR` hoặc `BEARISH_ENGULFING` (với lệnh SELL):* **-> THỰC THI LỆNH MARKET SELL.**
   * *Nếu không có:* Tiếp tục chờ cây nến tiếp theo đóng cửa (miễn là giá chưa phá thủng SL).

**Luồng B: Xử lý `BREAKOUT_SELL` / `BREAKOUT_BUY` (Canh phá vỡ)**
1. **Theo dõi giá đóng cửa:** Không dùng giá chạy realtime. EA chỉ kiểm tra tại thời điểm **Đóng nến (IsNewBar)** của khung thời gian yêu cầu (VD: `M15` hoặc `H1`).
2. Khi nến M15 đóng cửa, kiểm tra:
   * Giá Close của nến vừa đóng có thấp hơn (xuyên thủng) `trigger_price` không?
   * *Nếu thỏa mãn,* gọi hàm kiểm tra tính chất nến: Nến có phải là `STRONG_BEARISH` (Thân nến đỏ chiếm > 70% toàn bộ chiều dài nến) và không có râu dưới dài không?
   * *Nếu thỏa mãn:* **-> THỰC THI LỆNH MARKET SELL.**

---

### Bước 3: Toán học nhận diện nến cho EA (Candlestick Pattern Logic)

Lập trình viên MQL5 cần định nghĩa toán học chính xác cho các pattern:

1. **Pinbar (Rút chân):**
   * *Chiều dài tổng nến (Total_Length)* = High - Low
   * *Thân nến (Body)* = MathAbs(Open - Close)
   * *Bullish Pinbar (Rút chân dưới):* Lower Wick (Open/Close thấp nhất - Low) > 60% * Total_Length. Body < 25% * Total_Length. Close nằm ở nửa trên của nến.
   * *Bearish Pinbar (Rút chân trên):* Upper Wick (High - Open/Close cao nhất) > 60% * Total_Length. Body < 25% * Total_Length. Close nằm ở nửa dưới của nến.

2. **Engulfing (Nhấn chìm):**
   * Cần kiểm tra 2 nến: Nến trước đó `[2]` và nến vừa đóng `[1]`.
   * *Bullish Engulfing:* Nến `[2]` là nến Đỏ. Nến `[1]` là nến Xanh. Close[1] > Open[2] VÀ Open[1] < Close[2] (Thân xanh bao trùm thân đỏ).
   * *Bearish Engulfing:* Nến `[2]` là nến Xanh. Nến `[1]` là nến Đỏ. Close[1] < Open[2] VÀ Open[1] > Close[2] (Thân đỏ bao trùm thân xanh).

3. **Strong Breakout Candle (Nến xác nhận phá vỡ):**
   * *Breakdown SELL:* Nến Đỏ. Body / Total_Length > 75% (Thân nến rất đặc). Lower Wick / Total_Length < 10% (Gần như không rút râu dưới).

---

### Bước 4: Thực thi lệnh & Quản lý rủi ro (Order Management)

Vì kịch bản có **3 TP (Take Profit)**, EA cần quản lý lệnh theo 1 trong 2 cách:

*   **Cách 1 (Dễ code và an toàn nhất): Chia lệnh ra đánh.**
    * Khi có tín hiệu xác nhận, EA bắn **3 lệnh Market riêng biệt** cùng lúc với khối lượng chia đều (ví dụ: 0.1 Lot mỗi lệnh).
    * Lệnh 1: TP = TP1, SL mặc định.
    * Lệnh 2: TP = TP2, SL mặc định.
    * Lệnh 3: TP = TP3, SL mặc định.
*   **Cách 2: Quản lý đóng lệnh từng phần (Partial Close).**
    * Bắn 1 lệnh duy nhất tổng khối lượng (ví dụ: 0.3 Lot).
    * Khi giá chạm mốc TP1, EA dùng hàm `OrderClose` đóng 1/3 khối lượng và dời SL về Entry (Breakeven).
    * Khi chạm TP2 đóng tiếp 1/3 khối lượng.

### Bước 5: Hủy bỏ kịch bản (Invalidation)
Cần logic hủy kịch bản để tránh treo lệnh:
*   Kịch bản bị vô hiệu (Status chuyển từ `PENDING` sang `EXPIRED`) nếu:
    * Giá đi ngược chiều và chạm mốc SL *trước khi* có nến xác nhận ở vùng Entry.
    * Hết phiên giao dịch ngày (nếu là chiến lược Intraday).
    * AI cập nhật trạng thái mới.