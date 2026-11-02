# Group B – Triangle Structures

**Nhóm:** Cấu trúc tam giác  
**Mục tiêu:** Nhận diện hai cạnh giá hội tụ và phân biệt ba loại tam giác.

## B1. Symmetrical Triangle

### Hình thái
- Swing High tạo Lower High.
- Swing Low tạo Higher Low.
- Kháng cự dốc xuống và hỗ trợ dốc lên.
- Hai cạnh hội tụ về một vùng đỉnh tam giác.

### Bằng chứng
- Cần nhiều swing points trên cả hai cạnh; không vẽ tam giác từ một đỉnh và một đáy.
- Các điểm xoay sau nằm dần gần nhau.
- Biên độ dao động có xu hướng co hẹp.

### Hướng phá vỡ
Không mặc định tăng hay giảm. Theo dõi cả breakout cạnh trên và breakdown cạnh dưới.

## B2. Ascending Triangle

### Hình thái
- Các đỉnh tập trung quanh một vùng kháng cự tương đối ngang.
- Các đáy sau cao dần.
- Cạnh dưới hướng lên, cạnh trên tương đối phẳng.

### Bằng chứng
- Có nhiều lần kiểm tra vùng kháng cự.
- Có ít nhất các đáy tăng dần được xác nhận.
- Các lần điều chỉnh không phá cấu trúc đáy cao dần.

### Hướng phá vỡ
Ưu tiên theo dõi breakout lên, nhưng vẫn phải giữ kịch bản breakdown xuống nếu cấu trúc bị phá ngược.

## B3. Descending Triangle

### Hình thái
- Các đáy tập trung quanh vùng hỗ trợ tương đối ngang.
- Các đỉnh sau thấp dần.
- Cạnh trên hướng xuống, cạnh dưới tương đối phẳng.

### Bằng chứng
- Có nhiều lần kiểm tra vùng hỗ trợ.
- Có các đỉnh thấp dần được xác nhận.
- Các nhịp hồi không vượt đỉnh trước.

### Hướng phá vỡ
Ưu tiên theo dõi breakdown xuống, nhưng vẫn phải giữ kịch bản breakout lên nếu cấu trúc bị phá ngược.

## Quy tắc phân biệt với Range

- Nếu hai biên tương đối ngang: xem xét Rectangle.
- Nếu hai biên hội tụ: xem xét Triangle.
- Nếu mới có một điểm ở mỗi cạnh: chưa đủ cơ sở kết luận Triangle.
- Không ngoại suy hai đường trendline xa khỏi vùng dữ liệu quan sát để tạo một tam giác giả.

## Kết quả cần trả về
- Loại Triangle.
- Các swing high/low tạo cạnh trên/dưới.
- Số lần chạm mỗi cạnh.
- Vùng hội tụ ước tính nếu có đủ dữ liệu.
- Trạng thái và bằng chứng còn thiếu.
