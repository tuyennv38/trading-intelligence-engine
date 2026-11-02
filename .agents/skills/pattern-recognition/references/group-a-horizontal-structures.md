# Group A – Horizontal Structures

**Nhóm:** Cấu trúc vùng ngang  
**Mục tiêu:** Nhận diện vùng giá có biên trên/dưới tương đối ngang và các mô hình nhiều đỉnh/nhiều đáy.

## A1. Rectangle / Trading Range

### Hình thái
- Giá dao động giữa vùng kháng cự phía trên và hỗ trợ phía dưới.
- Hai biên tương đối ngang, không hội tụ rõ.
- Có nhiều lần giá tiếp cận và phản ứng ở một hoặc cả hai biên.

### Bằng chứng cần tìm
- Ít nhất hai phản ứng tách biệt ở biên trên và hai phản ứng ở biên dưới để có một range ứng viên đáng tin cậy.
- Giá có các nhịp di chuyển rõ ràng giữa hai biên.
- Vùng không quá rộng hoặc quá hẹp so với biến động hiện tại.

### Không được nhầm
Nếu đỉnh thấp dần và đáy cao dần rõ rệt, kiểm tra Symmetrical Triangle thay vì Rectangle.

## A2. Double Top / Multiple Top

### Hình thái
- Hai hoặc nhiều Swing High tập trung trong cùng một vùng kháng cự.
- Giữa các lần chạm có nhịp giảm tách biệt.
- Có thể có các đỉnh bằng nhau hoặc lệch nhẹ trong dung sai.

### Bằng chứng
- Một đỉnh: chỉ là potential resistance.
- Hai đỉnh: Double Top candidate.
- Từ ba đỉnh: Multiple Top candidate; xem xét phản ứng và cấu trúc đáy xen giữa.

Không gọi là mô hình đảo chiều hoàn chỉnh chỉ vì có hai đỉnh. Cần theo dõi vùng đáy/neckline ở giữa.

## A3. Double Bottom / Multiple Bottom

### Hình thái
- Hai hoặc nhiều Swing Low tập trung trong cùng một vùng hỗ trợ.
- Giữa các lần chạm có nhịp tăng tách biệt.
- Các đáy có thể bằng nhau hoặc lệch nhẹ trong dung sai.

### Bằng chứng
- Một đáy: chỉ là potential support.
- Hai đáy: Double Bottom candidate.
- Từ ba đáy: Multiple Bottom candidate; đánh giá các đỉnh xen giữa.

Không gọi là mô hình đảo chiều hoàn chỉnh chỉ vì có hai đáy. Theo dõi vùng đỉnh/neckline ở giữa.

## A4. Horizontal Support / Resistance

Đây là vùng giá, không nhất thiết là một pattern hoàn chỉnh.
- Resistance: vùng có nhiều phản ứng giảm.
- Support: vùng có nhiều phản ứng tăng.
- Lưu riêng số lần chạm, số lần rejection và độ mạnh phản ứng.

## Kết quả cần trả về
- Loại cấu trúc.
- Biên trên/dưới nếu là Rectangle.
- Vùng neckline nếu có Double Top/Bottom.
- Swing points tạo nên cấu trúc.
- Touch count theo từng biên/vùng.
- Trạng thái: DEVELOPING / POTENTIAL / CONFIRMED / INVALIDATED.
