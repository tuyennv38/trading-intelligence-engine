# Group C – Trendline & Channel Structures

**Nhóm:** Cấu trúc trendline, wedge và channel  
**Mục tiêu:** Nhận diện các đường biên dốc và cấu trúc giá di chuyển trong kênh.

## C1. Rising Wedge

### Hình thái
- Cả đỉnh và đáy đều tăng dần.
- Hai cạnh cùng hướng lên.
- Cạnh dưới thường dốc hơn cạnh trên, khiến biên độ hội tụ.

### Theo dõi
- Các swing high và swing low tạo hai cạnh.
- Số lần phản ứng trên mỗi cạnh.
- Mức độ hội tụ và chất lượng các điểm chạm.

Không kết luận Rising Wedge nếu hai cạnh không hội tụ.

## C2. Falling Wedge

### Hình thái
- Cả đỉnh và đáy đều giảm dần.
- Hai cạnh cùng hướng xuống.
- Cạnh trên thường dốc hơn cạnh dưới, khiến biên độ hội tụ.

### Theo dõi
- Các swing high/low tạo hai cạnh.
- Số lần phản ứng trên mỗi cạnh.
- Mức độ hội tụ.

## C3. Ascending Channel

### Hình thái
- Giá tạo các đỉnh cao dần và đáy cao dần.
- Hai đường biên tương đối song song và hướng lên.
- Giá dao động bên trong kênh.

## C4. Descending Channel

### Hình thái
- Giá tạo các đỉnh thấp dần và đáy thấp dần.
- Hai đường biên tương đối song song và hướng xuống.
- Giá dao động bên trong kênh.

## C5. Parallel / Horizontal Channel

### Hình thái
- Hai biên tương đối song song.
- Có thể nằm ngang hoặc có độ dốc nhẹ.
- Giá dao động giữa hai biên.

Nếu hai biên gần như ngang, cân nhắc phân loại là Rectangle/Range thuộc Group A.

## Quy tắc phân biệt

- Wedge: hai cạnh cùng hướng và hội tụ.
- Triangle: hai cạnh ngược hướng và hội tụ.
- Channel: hai cạnh tương đối song song.
- Một trendline chỉ có một điểm neo không đủ để xác nhận cấu trúc.
- Cần nhiều swing points phân bố trên mỗi cạnh, không chọn các điểm tùy ý chỉ để đường kẻ vừa biểu đồ.

## Kết quả cần trả về
- Tên mô hình.
- Hướng và độ dốc hai cạnh.
- Swing points tạo cạnh.
- Số lần chạm từng cạnh.
- Trạng thái hình thành và điều kiện invalidation.
