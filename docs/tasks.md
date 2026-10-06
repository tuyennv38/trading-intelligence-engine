# QUẢN LÝ TIẾN ĐỘ DỰ ÁN - AI TRADING STRATEGIST

Dưới đây là danh sách các công việc (Task List) được trích xuất từ Kế hoạch Tổng thể, chia theo từng module.

## GIAI ĐOẠN 1: CORE QUANT ALGORITHM (Nạp Dữ liệu & Tính toán)
### 1.1. Module Data Ingestion (Nạp dữ liệu từ MT5)
- [x] Tạo `CandleDto` và `MarketDataInitializeRequest` (Hỗ trợ Multi-Timeframe).
- [x] Khởi tạo `MarketDataController` với endpoint `POST /initialize`.
- [x] Cài đặt `INotificationService` (Hỗ trợ Google Chat Webhook).
- [x] Đưa Webhook URL thực tế vào cấu hình `launchSettings.json`.
- [x] Đăng ký `GoogleChatNotificationService` vào Dependency Injection trong `Program.cs`.
- [ ] Viết API endpoint `POST /update` (để hứng nến 2 phút/lần).
- [ ] Thiết lập logic lưu trữ mảng nến 1000 cây (Dùng `IMemoryCache` hoặc Redis) để khi `/update` gọi thì append nến mới.

### 1.2. Module Market Structure (Cấu trúc giá)
- [ ] Code thuật toán `SwingPointCalculator` để tìm Đỉnh/Đáy (Swing High / Swing Low).
- [ ] Code logic xác định Xu hướng (Trend) dựa trên Đỉnh/Đáy.
- [ ] Code thuật toán nhận diện phá vỡ cấu trúc (BoS - Break of Structure) và đảo chiều (ChoCh).

### 1.3. Module Volume Profile & Zones (Cung/Cầu)
- [ ] Viết thuật toán tính toán Fixed Range Volume Profile từ dải nến.
- [ ] Trích xuất mốc POC (Point of Control) và HVN (High Volume Nodes).
- [ ] Code logic tìm vùng Base Supply / Demand từ nến Imbalance.

---

## GIAI ĐOẠN 2: AI INTEGRATION (Tích hợp AI & Kịch bản)
- [ ] Tạo DTO `TradingPlanResponse` (Định nghĩa cấu trúc output cho kịch bản).
- [ ] Viết System Prompt chuyên gia cho Institutional Trader.
- [ ] Gọi SDK `Azure.AI.OpenAI` hoặc `SemanticKernel` để truyền dữ liệu JSON thô (từ GĐ 1) cho AI.
- [ ] Code hàm `Validator` kiểm tra mức độ chênh lệch giá của AI sinh ra (chống Ảo giác).

---

## GIAI ĐOẠN 3 & 4: STATEFUL ENGINE & API (Điều phối & Giao diện)
- [ ] Code `BackgroundService` chạy ngầm (hoặc Event Triggers) để check giá hiện tại so với Zone trong Plan.
- [ ] Cấu hình tự động gửi cảnh báo lên Google Chat khi giá chạm Zone.
- [ ] Viết API `GET /api/v1/plans/latest` trả về Plan JSON đẹp cho App/Web hiển thị.
- [ ] Viết API `GET /api/v1/plans/status` trả về trạng thái của giá so với Kế hoạch.

---

## GIAI ĐOẠN 5: TESTING
- [ ] Test kết nối MT5 thực tế gọi vào API `/initialize`.
- [ ] Test MT5 gọi vào API `/update` liên tục 2 phút/lần.
- [ ] So sánh Zone hệ thống vẽ bằng code C# với Zone vẽ tay trên TradingView để tinh chỉnh thuật toán.
