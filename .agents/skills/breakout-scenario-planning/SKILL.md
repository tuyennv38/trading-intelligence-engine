---
name: breakout-scenario-planning
description: >-
  Lập các kịch bản hành động giá (breakout lên, breakdown xuống, tiếp tục tích lũy, false breakout) sau khi đã nhận diện mô hình cấu trúc nến hoặc cấu trúc giá. Sử dụng khi đã có kết quả nhận diện mô hình (từ skill pattern-recognition) hoặc khi cần lập kế hoạch kịch bản giao dịch có điều kiện.
---

# Breakout Scenario Planning Skill

## Mục tiêu

Nhận kết quả từ [`pattern-recognition`](../pattern-recognition/SKILL.md) và lập các kịch bản có điều kiện. Không tự nhận diện lại mô hình từ đầu nếu Skill trước đã cung cấp kết quả; chỉ yêu cầu bổ sung dữ liệu khi bằng chứng không đủ.

## Đầu vào

- Mô hình chính/phụ và trạng thái.
- Các vùng hỗ trợ/kháng cự hoặc hai cạnh mô hình.
- Swing points và số lần chạm độc lập.
- Giá hiện tại, timeframe và trạng thái nến cuối.

## Kịch bản bắt buộc xem xét

### A. Breakout lên
- Vùng/cạnh cần vượt.
- Điều kiện xác nhận: nến đóng cửa ngoài biên; đánh giá thân nến, vị trí đóng cửa và động lượng.
- Phương án Momentum nếu cú phá vỡ mạnh và giá chưa chạy quá xa.
- Nếu không đủ lực: chờ retest.
- Mục tiêu tham khảo: swing high/vùng kháng cự kế tiếp đã có trong dữ liệu.

### B. Breakdown xuống
- Vùng/cạnh cần phá.
- Điều kiện xác nhận: nến đóng cửa dưới biên; đánh giá thân nến, vị trí đóng cửa và động lượng.
- Phương án Momentum nếu cú phá vỡ mạnh và giá chưa chạy quá xa.
- Nếu không đủ lực: chờ retest.
- Mục tiêu tham khảo: swing low/vùng hỗ trợ kế tiếp đã có trong dữ liệu.

### C. Tiếp tục tích lũy
- Giá vẫn nằm trong biên.
- Chưa có nến đóng cửa xác nhận phá vỡ.
- Nêu vùng cần tiếp tục theo dõi và bằng chứng cần chờ.

### D. False Breakout
- Giá xuyên biên nhưng không giữ được bên ngoài.
- Nêu điều kiện quay lại vùng và xác nhận cú phá vỡ bị phủ nhận.
- Nêu kịch bản ngược lại nếu có đủ dữ liệu.

## Phương thức xác nhận

### Momentum
Có thể xem xét ngay sau khi nến đóng cửa phá biên nếu:
- Mô hình đã đủ rõ.
- Nến breakout có thân và vị trí đóng cửa thuyết phục.
- Động lượng/volume hỗ trợ nếu volume có ý nghĩa với nguồn dữ liệu.
- Giá chưa cách biên quá xa.
- Phía trước còn khoảng trống hợp lý tới vùng cản kế tiếp.

Nếu giá đã chạy quá xa, ghi `NO_CHASE`.

### Retest
- Breakout lên: vùng vừa phá cần giữ vai trò hỗ trợ.
- Breakdown xuống: vùng vừa phá cần giữ vai trò kháng cự.
- Nêu điều kiện retest thành công/thất bại.
- Retest là lựa chọn, không bắt buộc cho mọi breakout.

## Định dạng báo cáo

```text
SYMBOL:
TIMEFRAME:
PATTERN:
PATTERN GROUP:
PATTERN STATUS:

STRUCTURE:
- Resistance / upper boundary:
- Support / lower boundary:
- Touch count:
- Key swing points:
- Evidence:

SCENARIO A – BREAKOUT UP:
- Trigger:
- Confirmation:
- Momentum option:
- Retest option:
- Next reference zone:
- Invalidation:

SCENARIO B – BREAKDOWN:
- Trigger:
- Confirmation:
- Momentum option:
- Retest option:
- Next reference zone:
- Invalidation:

SCENARIO C – RANGE CONTINUES:
- Conditions:
- What to monitor:

SCENARIO D – FALSE BREAKOUT:
- Warning/confirmation:
- Alternative scenario:

CURRENT STATUS: WAIT / MONITOR / SCENARIO CONFIRMED
REASON:
```

## Quy tắc
- Không đưa ra mức giá không có căn cứ trong dữ liệu.
- Nếu mô hình còn `DEVELOPING` hoặc `POTENTIAL`, phải nói kịch bản chỉ mang tính theo dõi.
- Không thiên vị BUY/SELL khi chưa có bằng chứng.
- Không biến kịch bản thành khẳng định chắc chắn hoặc lệnh tự động.
