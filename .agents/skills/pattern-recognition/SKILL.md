---
name: pattern-recognition
description: >-
  Nhận diện các mô hình cấu trúc giá và mô hình nến từ dữ liệu nến OHLCV (nhóm ngang, tam giác, trendline/channel, mô hình tiếp diễn). Sử dụng khi cần phân tích cấu trúc thị trường, xác định mô hình nến/giá trước khi lập kịch bản breakout.
---

# Pattern Recognition Skill

## Mục tiêu

Đọc dữ liệu nến và nhận diện cấu trúc/mô hình giá. Skill này chỉ trả lời:
- Thị trường đang có cấu trúc gì?
- Mô hình thuộc nhóm nào?
- Mô hình đã hình thành đến đâu?
- Các vùng biên và điểm xoay nằm ở đâu?
- Cần thêm bằng chứng gì để xác nhận?

Không tự lập lệnh BUY/SELL và không thực hiện giao dịch. Sau khi nhận diện, chuyển kết quả sang `breakout-scenario-planning`.

## Đầu vào

- Nến OHLCV theo thứ tự thời gian.
- Symbol, timeframe.
- Trạng thái nến cuối: đã đóng hay chưa đóng, nếu được cung cấp.
- Giá hiện tại, nếu có.

Không tự giả định timeframe hoặc volume khi dữ liệu không cung cấp.

## Quy trình nhận diện

### Bước 1 – Xác định điểm xoay

Tìm Swing High và Swing Low có ý nghĩa. Phân biệt:
- `CONFIRMED_SWING`: đã có đủ nến xác nhận theo phương pháp đang dùng.
- `PENDING_SWING`: có thể là điểm xoay nhưng chưa đủ xác nhận.

Không dùng dữ liệu tương lai để xác nhận một swing tại thời điểm quá khứ.

### Bước 2 – Theo dõi các lần chạm

- Một lần phản ứng: điểm quan sát ban đầu.
- Hai lần phản ứng độc lập: cấu trúc tiềm năng.
- Từ ba lần phản ứng độc lập: có thêm bằng chứng vùng được thị trường tôn trọng.

Một lần chạm chỉ được tính mới khi giá đã rời vùng rõ ràng rồi quay lại. Không đếm nhiều nến liên tiếp trong cùng một lần tiếp xúc thành nhiều lần chạm.

Ghi nhận cho từng lần:
- Mức giá/vùng giá.
- Thời điểm.
- Hướng tiếp cận.
- Độ sâu xuyên vùng.
- Khoảng giá bật ra khỏi vùng.
- Swing được hình thành sau lần chạm.
- Phản ứng mạnh lên hay yếu đi.

### Bước 3 – Phân loại theo nhóm

Đọc lần lượt bốn tài liệu tham khảo:
- [Group A – Horizontal Structures](./references/group-a-horizontal-structures.md)
- [Group B – Triangle Structures](./references/group-b-triangle-structures.md)
- [Group C – Trendline & Channel Structures](./references/group-c-trendline-structures.md)
- [Group D – Continuation Structures](./references/group-d-continuation-structures.md)

Chỉ gán nhãn khi đủ tiêu chí hình học và swing points. Có thể trả về nhiều mô hình ứng viên nếu chúng cùng tồn tại, nhưng phải chỉ rõ mô hình chính và phụ.

### Bước 4 – Xác định trạng thái

- `NOT_FOUND`: chưa thấy cấu trúc phù hợp.
- `DEVELOPING`: đang hình thành, còn thiếu điểm xoay hoặc lần chạm.
- `POTENTIAL`: có dấu hiệu, chưa đủ xác nhận.
- `CONFIRMED`: đáp ứng tiêu chí nhận diện của nhóm.
- `INVALIDATED`: cấu trúc bị phá theo cách làm mô hình không còn hợp lệ.

`CONFIRMED` chỉ có nghĩa là mô hình đã được nhận diện; không có nghĩa breakout đã xảy ra.

## Quy tắc chống nhận diện sai

- Không kết luận kháng cự mạnh từ một đỉnh đơn lẻ.
- Không kết luận hỗ trợ mạnh từ một đáy đơn lẻ.
- Không gọi Range là Triangle nếu không có hai cạnh hội tụ.
- Không gọi Channel nếu không có hai biên tương đối song song.
- Không gọi Flag nếu không có impulse leg trước đó.
- Không gọi Double/Multiple Top hoặc Bottom nếu các swing không có nhịp tách biệt.
- Nếu chưa đủ bằng chứng, phải nói rõ thiếu điểm nào; không tự vẽ thêm cấu trúc.

## Đầu ra bắt buộc

```json
{
  "symbol": "XAUUSD",
  "timeframe": "M15",
  "market_state": "RANGE",
  "primary_pattern": {
    "group": "A",
    "name": "RECTANGLE",
    "status": "DEVELOPING",
    "confidence": "MEDIUM"
  },
  "other_candidates": [],
  "zones": {
    "resistance": {
      "low": null,
      "high": null,
      "touch_count": 0
    },
    "support": {
      "low": null,
      "high": null,
      "touch_count": 0
    }
  },
  "swing_points": [],
  "evidence": [],
  "missing_evidence": []
}
```

Các giá trị trong ví dụ là placeholder. Không có dữ liệu thì dùng `null`, không tự điền mức giá.

## Báo cáo ngắn cho người dùng

1. Mô hình chính và nhóm.
2. Trạng thái hình thành.
3. Vùng giá/đường biên.
4. Các swing và số lần chạm độc lập.
5. Bằng chứng ủng hộ mô hình.
6. Bằng chứng còn thiếu hoặc điều kiện invalidation.
