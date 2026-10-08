# HƯỚNG DẪN KỸ NĂNG CHO AI TRADING STRATEGIST (AI_TRADING_SKILL)

File này định nghĩa các quy tắc vàng (Golden Rules) để tránh các lỗi ngớ ngẩn (Hallucination, Xung đột Kịch bản, Thiếu logic hành động giá) khi AI tạo Kế hoạch Giao dịch.

## 1. QUY TẮC TRÁNH XUNG ĐỘT KỊCH BẢN (Contradiction Avoidance)
- **Lỗi thường gặp:** AI cho lệnh `MUA Breakout` ở giá 4166 (Target 4185). Nhưng đồng thời lại cho lệnh `BÁN Counter-trend` ở 4169. Hai lệnh này đánh nhau chan chát! Nếu Breakout thành công, nó sẽ đâm thủng luôn Stop Loss của lệnh BÁN.
- **Cách khắc phục:**
  - Nếu BIAS chính là Bullish (Tăng): **Chỉ ưu tiên** Kịch bản Canh Mua ở Hỗ trợ và Mua Breakout. Bỏ qua hoàn toàn các kịch bản Bán Counter-trend (Bán ngược sóng) ở các vùng Kháng cự yếu để tránh gây nhiễu cho Trader.
  - Ngược lại với Bearish (Giảm).

## 2. QUY TẮC BREAKOUT LOGIC (Tính cụ thể)
- **Lỗi thường gặp:** AI viết "Nếu giá phá vỡ vùng hỗ trợ, hãy vào lệnh...". Viết như vậy rất chung chung, Trader không biết khi nào là "phá vỡ".
- **Cách khắc phục:** 
  - Phải chỉ định RÕ RÀNG KHUNG THỜI GIAN và MỐC GIÁ.
  - Ví dụ sai: *Đóng nến vượt qua BuySide M5.*
  - **Ví dụ đúng:** *Cần nến M5 đóng cửa dứt khoát vượt qua mốc giá 4165.00. Sau đó chờ giá retest (pullback) về lại 4165.00 để vào lệnh Mua.*

## 3. QUY TẮC REJECTION LOGIC (Mô hình nến xác nhận)
- **Lỗi thường gặp:** AI viết "Bắt nhịp pullback nếu giá bị từ chối tại cụm thanh khoản". "Bị từ chối" là một khái niệm trừu tượng.
- **Cách khắc phục:**
  - Bắt buộc phải gắn kèm các điều kiện MÔ HÌNH NẾN ĐẢO CHIỀU (Price Action Patterns).
  - **Ví dụ đúng:** *Canh Mua tại vùng 4151-4154. Tuyệt đối không Limit. Bắt buộc chờ xuất hiện nến Pinbar rút chân dài hoặc nến Bullish Engulfing (Nhấn chìm tăng) trên khung M5 tại vùng này mới được phép kích hoạt lệnh.*

## 5. QUY TẮC CẤM ĐÁNH XUNG ĐỘT TẠI CÙNG 1 VÙNG GIÁ (Anti-Hedging)
- **Lỗi thường gặp:** AI cho kịch bản BÁN ở vùng `4123 - 4127`, Stop Loss `4131`. Sau đó lại cho kịch bản Breakout MUA tại `4127`. => Nếu giá lên `4128`, nó vừa kích hoạt Breakout Mua, vừa đang âm trạng thái của lệnh Bán (vì chưa cắn SL 4131). Hành vi tự đấm vào mặt nhau (Hedging) này là đại kỵ trong giao dịch!
- **Cách khắc phục:** 
  - Chỉ chọn **MỘT hướng giao dịch** tại một vùng giá.
  - Hệ thống code C# đã có chức năng **TỰ ĐỘNG GỌI LẠI AI khi cắn Stop Loss**. Do đó, bạn không cần phải đưa ra kịch bản Breakout "dự phòng" cho lệnh Limit. Nếu bạn đánh giá cản đó cứng, hãy chỉ lên Kịch bản BÁN. Nếu cản đó vỡ, giá đâm thủng Stop Loss, Bot sẽ gọi bạn dậy để tính toán nhịp Breakout sau!
  - Tuyệt đối Mốc kích hoạt Breakout (Trigger Price) **KHÔNG BAO GIỜ** được nằm bên trong khoảng cách từ Entry đến Stop Loss của một lệnh ngược chiều.
## 4. QUY TẮC UPDATE THÔNG BÁO CẮN STOP LOSS
Hệ thống C# (MarketDataController) đã được lập trình để tự động bắt lỗi cắn Stop Loss và in ra thông báo rõ ràng.
Ví dụ: `🚨 CẬP NHẬT KHẨN CẤP: Giá (4140) đã cắn Stop Loss (4141) của kịch bản MUA [M15/M5 Aggressive Pullback Support]. Đang tính toán lại...`
=> Từ đó, AI ở lượt chạy tiếp theo phải biết rằng vùng MUA đó đã thủng (Failed), không được phép xúi người dùng mua lại ở vùng đó nữa.

## 6. QUY TẮC CẤU TRÚC KỊCH BẢN (Zone & SL Spacing)
- **Lỗi thường gặp:** AI thường đưa ra các vùng Entry quá hẹp (ví dụ: `4108.65 - 4108.99` - chênh nhau có 0.3 giá), hoặc đặt Stop Loss quá sát, hoặc Stop Loss của kịch bản 1 lại chính là Entry của kịch bản 2. Điều này dẫn đến nhiễu (Noise) cực lớn khi giá giật (Whipsaw).
- **Cách khắc phục bắt buộc:**
  1. **Độ rộng Entry (Zone Width):** Vùng Entry (`EntryTop` trừ `EntryBottom`) phải rộng **tối thiểu 3 giá (points)**. (Ví dụ: `4105.00 - 4108.00`).
  2. **Khoảng cách Stop Loss:** SL phải cách mép ngoài cùng của vùng Entry **tối thiểu 5 giá (points)** để chịu được độ giật (Ví dụ: Buy Zone `4105 - 4108` => SL thấp nhất phải là `4100`).
  3. **Khoảng cách Take Profit (TP Spacing) & Tránh làm tròn số:** 
     - **Không làm tròn (Zero Hallucination):** Tuyệt đối KHÔNG tự sáng tác ra các con số tròn (ví dụ: `4135.00`, `4120.00`) làm mục tiêu chốt lời. Bắt buộc phải sử dụng chính xác mốc giá trị thập phân (ví dụ: `4135.25`) từ các mức Thanh khoản (BuySide/SellSide) hoặc Cấu trúc do hệ thống cung cấp.
     - **Khoảng cách tối thiểu:** Khoảng cách giữa các mốc chốt lời (TP1 -> TP2 -> TP3) phải cách nhau **tối thiểu 5 giá (points)**. Nếu 2 vùng thanh khoản nằm quá sát nhau (ví dụ: `4142.45` và `4143.40`), hãy gộp chúng lại thành 1 mốc TP duy nhất và tìm một cản/fibonacci xa hơn cho mốc TP tiếp theo.
  4. **Không xếp chồng mốc giá (Anti-Stacking):** KHÔNG BAO GIỜ được lấy Stop Loss của Kịch bản A làm Entry cho Kịch bản B. Hãy để cho mỗi vùng giao dịch một khoảng "không gian thở" (Breathing room) rõ rệt để tránh hiệu ứng domino cắn Stop Loss liên hoàn.