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

## 4. QUY TẮC UPDATE THÔNG BÁO CẮN STOP LOSS
Hệ thống C# (MarketDataController) đã được lập trình để tự động bắt lỗi cắn Stop Loss và in ra thông báo rõ ràng.
Ví dụ: `🚨 CẬP NHẬT KHẨN CẤP: Giá (4140) đã cắn Stop Loss (4141) của kịch bản MUA [M15/M5 Aggressive Pullback Support]. Đang tính toán lại...`
=> Từ đó, AI ở lượt chạy tiếp theo phải biết rằng vùng MUA đó đã thủng (Failed), không được phép xúi người dùng mua lại ở vùng đó nữa.