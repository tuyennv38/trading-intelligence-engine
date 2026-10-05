# Yêu cầu logic lập trình EA (MQL4/MQL5) tích hợp AI Trading API

Tài liệu này mô tả chi tiết logic cho AI hoặc Lập trình viên để phát triển một Expert Advisor (EA) tích hợp với hệ thống AI Trading API. EA sẽ hoạt động trên khung thời gian H4, lấy dữ liệu nến, gửi request đến API, nhận quyết định và thực thi lệnh một cách kỷ luật để tránh rủi ro trượt giá (slippage) hoặc độ trễ (latency).

---

## 1. Logic gọi API ra quyết định (AI Decision Request)

### 1.1. Thời điểm gọi API
* **Khung thời gian hoạt động:** H4 (4 giờ).
* **Trigger:** Thay vì đợi đúng giây 00 của nến mới, EA cần đếm ngược thời gian đóng nến.
* **Hành động:** Gửi API request ở thời điểm **trước khi nến H4 đóng cửa 10 giây** (ví dụ: `03:59:50`, `07:59:50` theo giờ server).
* **Lý do:** Giúp API có khoảng 3-5 giây xử lý. Khi kết quả trả về cũng là lúc nến H4 mới vừa mở cửa, EA sẽ vào lệnh tại vạch xuất phát.

### 1.2. Dữ liệu gửi đi (Payload)
* **Số lượng nến:** 120 cây nến gần nhất (tính cả cây nến H4 đang chạy ở giây thứ 59:50).
* **Indicators đi kèm:** Mọi nến trong mảng gửi lên cần được tính sẵn các giá trị indicator: `EmaFast`, `EmaSlow`, `Rsi`, `Adx`, `Atr` bằng các hàm iEMA, iRSI, iADX, iATR có sẵn của MT4/MT5.
* **Endpoint:** `POST /api/v1/ai/decision`

---

## 2. Logic xử lý Response và Vào lệnh (Order Execution)

Khi API trả về JSON chứa các thông số:
```json
{
  "decision": {
    "decision": "BUY", // BUY, SELL, HOLD
    "confidence": 0.85,
    "suggestedType": "LIMIT", // LIMIT, MARKET, STOP
    "suggestedEntry": 1.1050,
    "suggestedStopLoss": 1.1000,
    "suggestedTakeProfit": 1.1150
  }
}
```

### 2.1. Kiểm tra tính hợp lệ (Validation) trước khi vào lệnh
* Bỏ qua nếu `decision` là `HOLD`.
* Đối với `BUY`: Kiểm tra `suggestedEntry` > `suggestedStopLoss` và `suggestedTakeProfit` > `suggestedEntry`.
* Đối với `SELL`: Kiểm tra `suggestedEntry` < `suggestedStopLoss` và `suggestedTakeProfit` < `suggestedEntry`.
* Tính toán Risk (khoảng cách từ Entry đến SL tính bằng pips/points) xem có phù hợp với số Lot định đánh hay không.

### 2.2. Chiến thuật vào lệnh (Market vs Limit)
Không vào lệnh MARKET một cách mù quáng. EA phải so sánh Giá Hiện Tại (`Ask` cho lệnh BUY, `Bid` cho lệnh SELL) với `suggestedEntry` của AI.

**Ví dụ với lệnh BUY (Entry AI đề xuất là 1.1050):**
* **Trường hợp 1 (Giá ở rất gần Entry):**
  Nếu Giá `Ask` hiện tại nằm trong khoảng dung sai cho phép (ví dụ: chênh lệch $\le$ 3 pips, tức là `Ask` nằm khoảng 1.1047 đến 1.1053).
  $\Rightarrow$ EA thực hiện lệnh **MARKET BUY** ngay lập tức, sử dụng `suggestedStopLoss` và `suggestedTakeProfit` do AI cấp.
* **Trường hợp 2 (Giá đã chạy đi quá xa so với Entry mong muốn):**
  Nếu Giá `Ask` hiện tại > `1.1053` (vượt quá 3 pips).
  $\Rightarrow$ EA **TUYỆT ĐỐI KHÔNG MUA ĐUỔI**. Thay vào đó, EA gửi một lệnh **BUY LIMIT** tại đúng giá `suggestedEntry` (1.1050), kèm theo SL và TP của AI.
  
*(Thực hiện tương tự nhưng đảo chiều đối với lệnh SELL).*

### 2.3. Hủy lệnh tự động (Pending Order Expiration)
* Mọi lệnh LIMIT được đặt bởi EA phải được xóa đi (Cancel) nếu nó không khớp trong suốt chu kỳ của 1 cây nến H4.
* **Cách thực hiện:** Set tham số `Expiration` của lệnh Limit bằng `TimeCurrent() + 4 giờ`. Khi H4 đóng cửa, thị trường đã có cấu trúc mới, EA sẽ gửi request mới và lệnh Limit cũ không còn giá trị.

---

## 3. Logic gửi phản hồi (Feedback/Learning Loop)

Để AI học hỏi từ các lệnh thắng/thua, EA cần theo dõi trạng thái các lệnh đã vào.
* Khi EA phát hiện một lệnh (do AI phím) vừa bị đóng (chạm SL hoặc chạm TP).
* EA gọi `POST /api/v1/ai/feedback` với thông tin:
  ```json
  {
    "sessionId": "<Session_Id_nhận_được_từ_lúc_gọi_decision>",
    "outcome": "WIN", // hoặc "LOSS"
    "pnlPips": 100, // Số pips lời/lỗ
    "exitReason": "Take Profit Hit" // "Stop Loss Hit", "Manual Close"...
  }
  ```
* Việc này giúp hệ thống lưu trữ `ai_trade_outcomes` vào ClickHouse để làm giàu dữ liệu (reinforcement learning).