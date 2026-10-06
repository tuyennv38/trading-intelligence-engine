# KẾ HOẠCH XÂY DỰNG HỆ THỐNG AI TRADING STRATEGIST (100% C# .NET)
**Thời gian dự kiến cho bản MVP: 4 - 6 tuần**

## 1. TỔNG QUAN KIẾN TRÚC HỆ THỐNG (TECH STACK)
*   **Ngôn ngữ Core:** C# .NET 8 (Đồng nhất hệ sinh thái hiện tại).
*   **Web Framework:** ASP.NET Core Web API (`TradingIntelligenceEngine.Api`).
*   **Background Tasks:** `IHostedService` / `BackgroundService` hoặc thư viện `Hangfire` / `Quartz.NET` (quản lý cronjob quét nến).
*   **Database & Cache:** PostgreSQL (qua Entity Framework Core) + Redis (`StackExchange.Redis` cho stateful data).
*   **Quant & Technical:** Thư viện `Skender.Stock.Indicators` (cho các chỉ báo cơ bản) + Custom LINQ/Arrays (tự code để tính Volume Profile và SMC).
*   **AI Engine:** SDK `Azure.AI.OpenAI` (hỗ trợ cả OpenAI gốc) hoặc `Microsoft.SemanticKernel` để giao tiếp với GPT-4o / Claude 3.5.

---

## 2. CÁC GIAI ĐOẠN TRIỂN KHAI VÀ MAPPING VÀO SOURCE CODE CÓ SẴN

### GIAI ĐOẠN 1: CORE QUANT ALGORITHM - "XÂY DỰNG NÃO TRÁI" (Tuần 1-2)
*Mục tiêu: Kéo dữ liệu nến và dùng toán học để tính ra các mốc giá.*
*   **Nạp dữ liệu (Data Ingestion):** Kéo dữ liệu OHLCV liên tục (H1, M15).
    *   *Nơi triển khai:* Có thể đặt tại `TradingIntelligenceEngine.Application` hoặc `Infrastructure` kết nối với API sàn.
*   **Cấu trúc giá (Market Structure):** Xác định Đỉnh/Đáy (Swing), Xu hướng, BoS, ChoCh.
    *   *Nơi triển khai:* Project `TradingIntelligenceEngine.MarketStructure`.
*   **Volume Profile & Cung Cầu:** Tính toán điểm kiểm soát (POC), vùng Volume lớn (HVN), Base Supply/Demand.
    *   *Nơi triển khai:* Project `TradingIntelligenceEngine.TechnicalAnalysis` và `TradingIntelligenceEngine.Liquidity`.
*   **Output:** Object (DTO/JSON) chứa dữ liệu toán học chuẩn xác (Trend, Zones, HVN Levels).

### GIAI ĐOẠN 2: AI INTEGRATION & PROMPT ENGINEERING - "XÂY DỰNG NÃO PHẢI" (Tuần 3)
*Mục tiêu: Đưa dữ liệu thô vào LLM để sinh ra "Bản Kế Hoạch Giao Dịch".*
*   **System Prompt & Structured Output:** Ép AI vào vai Institutional Trader, trả kết quả định dạng JSON.
    *   *Nơi triển khai:* Project `TradingIntelligenceEngine.AI`. Dùng `System.Text.Json` để parse và map vào class `TradingPlanResponse`.
*   **Hàm Validator (Chống Ảo giác):** Filter kiểm tra logic giá trị (Ví dụ: So sánh SL AI sinh ra với DTO của Giai đoạn 1).

### GIAI ĐOẠN 3: STATEFUL ENGINE & EVENT ROUTER - "NGƯỜI ĐIỀU PHỐI" (Tuần 4)
*Mục tiêu: Quét 2 phút/lần nhưng không spam AI, duy trì tính nhất quán.*
*   **Redis Cache Stateful:** Lưu bản kế hoạch AI mới nhất.
*   **Vòng lặp Monitoring (`BackgroundService`):** Background task C# tự động chạy 2 phút/lần. Tải nến mới, merge vào List hiện tại, so sánh với `TradingPlanResponse` trong Redis.
*   **Event Triggers:** Bắn `Action` hoặc dùng MediatR để trigger lệnh gọi AI sinh plan mới khi đóng nến H4 hoặc gãy cấu trúc.

### GIAI ĐOẠN 4: XÂY DỰNG API ENDPOINTS & NOTIFICATION (Tuần 5)
*Mục tiêu: Đóng gói API cho App/Web/Bot.*
*   *Nơi triển khai:* `TradingIntelligenceEngine.Api`
*   `GET /api/v1/plans/latest?symbol=XAUUSD`: Trả kịch bản mới nhất.
*   `GET /api/v1/plans/status?symbol=XAUUSD`: Trả trạng thái hiện tại.
*   **Notification:** Bắn cảnh báo tự động khi giá chạm Zone qua Telegram/Webhook (tại project `TradingIntelligenceEngine.Signal` hoặc `Application`).

### GIAI ĐOẠN 5: TESTING & TỐI ƯU HÓA (Tuần 6)
*Mục tiêu: Backtest và Forward Test.*
*   *Nơi triển khai:* `TradingIntelligenceEngine.Backtesting` để kiểm tra độ tin cậy của bộ xác định Zone và Cấu trúc.

---

## 3. LƯU Ý KHI CODE C# .NET
1. **Hiệu suất xử lý nến:** Dữ liệu nến nên dùng kiểu `struct` (như `Record` hoặc struct OHLCV) thay vì `class` để tối ưu Garbage Collection khi mảng lên tới hàng nghìn nến.
2. **Quản lý Background Task:** Cẩn thận với Scoped Service khi chạy trong `BackgroundService` (Singleton). Phải dùng `IServiceScopeFactory` để resolve DbContext và gọi AI.