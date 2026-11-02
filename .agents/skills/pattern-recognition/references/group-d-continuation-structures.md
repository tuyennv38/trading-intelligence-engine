# Group D – Continuation Structures

**Nhóm:** Cấu trúc tiếp diễn sau một nhịp di chuyển  
**Mục tiêu:** Nhận diện mô hình tạm nghỉ/điều chỉnh sau một impulse leg.

## D1. Bull Flag

### Hình thái
- Có một nhịp tăng mạnh trước đó (flagpole/impulse leg).
- Sau nhịp tăng, giá điều chỉnh hoặc đi ngang trong một vùng tương đối hẹp.
- Cấu trúc điều chỉnh thường có độ dốc xuống nhẹ hoặc đi ngang.

### Bằng chứng
- Impulse leg phải rõ ràng so với biến động nền trước đó.
- Có các swing high/low tạo vùng điều chỉnh.
- Vùng điều chỉnh không xóa bỏ phần lớn nhịp impulse theo tiêu chí cấu hình.
- Chưa có breakout xác nhận thì chỉ gọi là Bull Flag candidate.

## D2. Bear Flag

### Hình thái
- Có một nhịp giảm mạnh trước đó.
- Sau đó giá hồi lên hoặc đi ngang trong vùng điều chỉnh.
- Vùng điều chỉnh thường có độ dốc lên nhẹ hoặc đi ngang.

### Bằng chứng
- Impulse giảm phải rõ.
- Có các swing tạo vùng hồi.
- Vùng hồi không làm mất phần lớn nhịp impulse theo tiêu chí cấu hình.
- Chưa có breakdown xác nhận thì chỉ gọi là Bear Flag candidate.

## D3. Impulse + Consolidation

### Hình thái
- Giá có một nhịp di chuyển mạnh theo một hướng.
- Sau đó hình thành vùng tích lũy/đi ngang.
- Vùng tích lũy có thể là Rectangle, Triangle, Wedge hoặc cấu trúc chưa rõ.

### Quy tắc
- Đây là mô tả cấu trúc tổng quát, không phải tên một pattern cụ thể.
- Phải phân loại riêng hình dạng vùng tích lũy nếu dữ liệu đủ.
- Không mặc định giá sẽ tiếp tục theo hướng impulse; vẫn phải xét breakout hai chiều.

## Quy tắc phân biệt
- Thiếu impulse leg: không gắn nhãn Flag.
- Có impulse nhưng vùng sau đó chưa rõ: dùng `IMPULSE_PLUS_CONSOLIDATION`.
- Chỉ khi hình thái điều chỉnh phù hợp mới gắn nhãn Bull Flag/Bear Flag.

## Kết quả cần trả về
- Hướng impulse.
- Biên độ impulse.
- Vùng điều chỉnh.
- Độ sâu hồi so với impulse.
- Pattern candidate và trạng thái.
