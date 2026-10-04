# Kế hoạch triển khai Trading Intelligence Engine (V1)

Dựa trên tài liệu đặc tả hệ thống `trading-intelligence-engine-spec.md`, hệ thống sẽ được xây dựng theo hướng module hóa chặt chẽ. Trọng tâm của phiên bản V1 là hoàn thiện **Market Analysis Layer** bao gồm các Phase từ 1 đến 4, đảm bảo Market Structure Engine hoạt động chuẩn xác trước khi mở rộng sang Strategy, AI, Risk hay Execution.

## Giai đoạn 1: Khởi tạo & Nền tảng (Phase 1 - Foundation)
**Mục tiêu:** Xây dựng khung kiến trúc thư mục, khởi tạo Solution, các project phân lớp (Domain, Application, Api, Infrastructure) và định nghĩa các Domain core.

1. **Thiết lập Solution và Cấu trúc Project:**
   - Tạo `TradingIntelligenceEngine.sln`.
   - Tạo các projects như thiết kế: `.Api`, `.Application`, `.Domain`, `.MarketData`, `.MarketStructure`, `.TechnicalAnalysis`, `.Liquidity`, v.v...
   - Khởi tạo thư mục `tests/` với các project unit test tương ứng.
2. **Định nghĩa Core Domain Models (Market Data):**
   - Implement `Candle` record, kèm theo validation rules (High >= Open/Close/Low, Low <= Open/Close, Volume >= 0).
   - Implement `MarketInstrument` (không hard-code, hỗ trợ XAUUSD, BTCUSD...).
   - Implement enum `Timeframe` (M1, M5, M15, M30, H1, H4, D1).
3. **Cấu hình hệ thống (Configuration):**
   - Thiết lập cấu trúc `appsettings.json` cho cấu hình MarketStructure (FractalStrength, MinAtrMultiplier) và Technical Indicators (EmaFast, EmaSlow, RSI, ADX, v.v.).

## Giai đoạn 2: Market Structure (Phase 2 - Cốt lõi)
**Mục tiêu:** Xây dựng module nhận diện cấu trúc thị trường, bao gồm Swings và Structure Breaks. Đây là phần cần unit test kỹ nhất.

1. **Indicator Phụ trợ:**
   - Implement thuật toán tính `ATR` (Average True Range) để hỗ trợ Adaptive Swing.
2. **Swing Detection (Nhận diện đỉnh đáy):**
   - Xây dựng thuật toán xác định Swing High / Swing Low dựa trên Fractal Strength.
   - Implement `Adaptive Swing` dựa trên `ATR × MinAtrMultiplier`.
   - Phân biệt rõ trạng thái `Potential`, `Confirmed`, `Invalidated` để chống repainting.
3. **Structure Labeling:**
   - Đánh dấu `HH` (Higher High), `HL` (Higher Low), `LH` (Lower High), `LL` (Lower Low).
   - Xác định xu hướng (Bullish / Bearish Structure) dựa trên chuỗi Swings.
4. **Break of Structure (BOS) & Change of Character (CHOCH):**
   - Phát hiện BOS (tiếp diễn xu hướng) thông qua giá Close vượt Swing point.
   - Phát hiện CHOCH (thay đổi tính chất xu hướng) từ các tín hiệu đảo chiều sớm.
   - Viết Unit Tests bao phủ mọi test-cases, đảm bảo Wick-only break không bị nhận diện nhầm.

## Giai đoạn 3: Phân tích bối cảnh (Phase 3 - Market Context)
**Mục tiêu:** Thêm các yếu tố kỹ thuật và thanh khoản để làm phong phú dữ liệu bối cảnh (Context).

1. **Technical Indicators:**
   - Tích hợp `EMA` (đường trung bình động: 34, 89) để xác định xu hướng phụ.
   - Tích hợp `RSI` và `ADX` để đánh giá sức mạnh xu hướng/động lượng.
2. **Liquidity (Thanh khoản) & S/R:**
   - Nhận diện Equal Highs / Equal Lows dựa trên ATR tolerance.
   - Phát hiện tín hiệu Liquidity Sweep (quét thanh khoản) so với các vùng đã xác định.
   - Tính toán Support / Resistance zones dựa trên Swing clusters.
3. **Market Regime (Trạng thái thị trường):**
   - Xây dựng logic phân loại thị trường: `TrendingUp`, `TrendingDown`, `Ranging`, `Transition`, `HighVolatility`, `LowVolatility` dựa trên sự kết hợp giữa Structure, EMA, ADX...

## Giai đoạn 4: Market State & API (Phase 4 - Tích hợp)
**Mục tiêu:** Đóng gói toàn bộ phân tích thành một object `MarketState` duy nhất, chuẩn bị API endpoint đầu tiên và xử lý đa khung thời gian.

1. **Tổng hợp Market State:**
   - Tạo class `MarketState` chứa tất cả các phân tích từ Structure, Events, Liquidity, Technical, và Regime.
   - Xây dựng cơ chế explainability (giải thích quyết định phân tích) đi kèm trong object.
2. **Multi-Timeframe Architecture:**
   - Thiết kế Interface cho phép tổng hợp `MarketState` từ nhiều Timeframes (VD: H4, H1, M15, M5) thay vì chỉ một khung đơn lẻ.
3. **API Layer:**
   - Xây dựng endpoint `POST /api/v1/market-analysis/analyze`.
   - Đảm bảo nhận Request `OHLCV` mảng nến và trả về JSON chuẩn hóa của `MarketState`.
4. **Tối ưu hóa (Incremental Processing):**
   - Hỗ trợ cơ chế phân tích lũy tiến: Chỉ xử lý các nến mới (New candle) dựa trên State cũ thay vì tính toán lại từ đầu hàng nghìn nến.

---
## Ghi chú cho quá trình code (Dành cho Agent)
* Bám sát nguyên tắc **"Không Look-Ahead Bias"** và **"Không Repaint"**.
* Mọi dependencies phải tuân thủ luồng: `API -> Application -> Domain`.
* Hoàn thành triệt để V1 theo tiêu chí (Definition of Done) trước khi tiến hành các Phase tương lai như Strategy, AI, Risk, Execution hay Backtesting.
* Bắt đầu làm việc với Phase 1: Tạo Solution và Structure.