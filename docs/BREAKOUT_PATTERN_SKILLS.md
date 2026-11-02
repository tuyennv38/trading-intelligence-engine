# Breakout Pattern Skills

Bộ Skill dành cho Antigravity tập trung vào đúng hai nhiệm vụ:
1. Nhận diện mô hình cấu trúc giá theo từng nhóm (`pattern-recognition`).
2. Lập kịch bản sau khi mô hình được nhận diện (`breakout-scenario-planning`).

Không bao gồm tự động đặt lệnh, quản lý vốn hoặc quản lý giao dịch.

## Cấu trúc chuẩn cho Antigravity

Trong Antigravity, các skill cấp dự án (Workspace Skills) được đặt tại thư mục `.agents/skills/<skill-name>/`:

```text
breakout_pattern_skills/
├── README.md
└── .agents/
    └── skills/
        ├── pattern-recognition/
        │   ├── SKILL.md
        │   └── references/
        │       ├── group-a-horizontal-structures.md
        │       ├── group-b-triangle-structures.md
        │       ├── group-c-trendline-structures.md
        │       └── group-d-continuation-structures.md
        └── breakout-scenario-planning/
            └── SKILL.md
```

> **Mẹo triển khai toàn cục (Global Skills):**  
> Nếu bạn muốn sử dụng các skill này ở mọi workspace trong Antigravity thay vì chỉ trong thư mục dự án hiện tại, hãy sao chép thư mục `pattern-recognition` và `breakout-scenario-planning` vào:  
> `~/.gemini/config/skills/` (trên Windows: `%USERPROFILE%\.gemini\config\skills\`).

## Các nhóm mô hình

| Nhóm | Tên nhóm | Mô hình |
|---|---|---|
| A | Horizontal Structures | Rectangle/Range, Double/Multiple Top, Double/Multiple Bottom, Support/Resistance |
| B | Triangle Structures | Symmetrical, Ascending, Descending Triangle |
| C | Trendline & Channel Structures | Rising/Falling Wedge, Ascending/Descending/Parallel Channel |
| D | Continuation Structures | Bull Flag, Bear Flag, Impulse + Consolidation |

## Cách hoạt động

1. **`pattern-recognition`**: Skill điều phối nhận diện mô hình. Tự động tra cứu tài liệu tham khảo trong thư mục `references/`, đối chiếu với dữ liệu nến OHLCV để xác định mô hình và đưa ra bằng chứng (swing points, số lần chạm, vùng giá).
2. **`breakout-scenario-planning`**: Nhận kết quả từ `pattern-recognition` để lập các kịch bản tiếp theo: Breakout lên, Breakdown xuống, Tiếp tục tích lũy (Range continues), hoặc False breakout (phá vỡ giả).

## Nguyên tắc quan trọng

- Một đỉnh/đáy đơn lẻ chỉ là điểm quan sát, chưa đủ xác nhận mô hình.
- Hai lần phản ứng độc lập tạo cấu trúc tiềm năng; từ lần thứ ba trở đi có thêm bằng chứng về vùng giá.
- Không bắt buộc đúng ba lần chạm. Chất lượng phản ứng và hình học quan trọng hơn việc đếm máy móc.
- Không ép dữ liệu vào mô hình. Nếu chưa đủ, trả về `DEVELOPING` hoặc `NO_CLEAR_PATTERN`.
- Chỉ sử dụng dữ liệu đã có tại thời điểm phân tích; không nhìn trước nến tương lai.
