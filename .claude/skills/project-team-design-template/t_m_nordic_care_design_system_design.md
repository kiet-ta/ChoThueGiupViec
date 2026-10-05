# DESIGN SYSTEM: TỔ ẤM — Nordic Care (Haisey Template Edition)

> **Mục đích tài liệu:** Quy chuẩn toàn diện về Thiết kế Giao diện (UI), Hệ thống Nhận diện Thương hiệu, Quy tắc Bố cục (Layout Architecture), Bảng Màu (Color Tokens), Kiểu Chữ (Typography) và Bộ Thành phần (Component Library) dựa trên giao diện trang chủ **TỔ ẤM — Trang Chủ Chuẩn Mực Bắc Âu (Theo Template Haisey Home Care)**.
> Tài liệu này được thiết kế để áp dụng đồng bộ cho tất cả các trang nội bộ (Chi tiết Dịch vụ, Đặt lịch, Bảng giá, Hồ sơ nhân sự, Trang thanh toán, Giới thiệu).

---

## 1. Triết lý Thiết kế & Định vị Trực quan (Design Philosophy)

- **Phong cách chủ đạo:** **Nordic Warmth & Domestic Sanctuary (Hygge & Hearth)** lai ghép với **Cấu trúc Thấu cảm & Rõ ràng (Empathy & Clarity)** lấy cảm hứng từ cấu trúc template của **Haisey Home Care Australia**.
- **Cảm giác mang lại:** An tâm tuyệt đối, gọn gàng, ấm cúng, tinh tế, không phô trương công nghệ gây choáng ngợp mà tập trung vào sự chăm sóc và minh bạch.
- **Tỷ lệ thị giác (The Three Dials):**
  - **Visual Density:** `4/10` (Rộng rãi, thoáng đãng, macro-whitespace từ `py-16` đến `py-24`).
  - **Design Variance:** `6/10` (Bố cục xen kẽ nhịp nhàng giữa thẻ trắng thanh mảnh và các mảng nền tương phản điểm nhấn).
  - **Motion Intensity:** `4/10` (Chuyển động nhẹ nhàng, transition `ease-out` 150ms–200ms, hover nhẹ tránh xao nhãng).

---

## 2. Hệ Thống Màu Sắc (Color Tokens & Palette)

### 2.1 Bảng màu cốt lõi (Semantic Color Tokens)
| Token Name | Hex Code | Vai trò & Ứng dụng |
| :--- | :--- | :--- |
| `color-canvas` | `#FAF8F5` | Nền canvas tổng thể toàn trang (Warm Ivory / Giấy thủ công ấm áp, dịu mắt). |
| `color-surface` | `#FFFFFF` | Nền các thẻ Card, Container, Bảng biểu, Khối tính toán. |
| `color-surface-subtle` | `#F4F2EB` | Nền các khối phụ, khung viền nhạt, badge nền. |
| `color-primary` | `#2D5A43` | Màu xanh rừng thông Bắc Âu đặc trưng (Deep Forest Pine) cho nút CTA chính, tiêu đề nhấn. |
| `color-primary-hover` | `#224432` | Trạng thái hover cho nút chính, border active. |
| `color-secondary-accent` | `#6B8F71` | Xanh xô thơm (Sage Green) cho icon tròn, pill tags, dấu kiểm thành công. |
| `color-accent-amber` | `#D99B26` | Vàng hổ phách tự nhiên cho đánh giá 5 sao, thẻ khuyến mãi nổi bật. |
| `color-contrast-dark` | `#1A2F25` | Khối tương phản đậm (Section 4 Trụ Cột, Footer) tạo chiều sâu cao cấp. |
| `color-text-title` | `#1F2923` | Màu chữ tiêu đề chính (Dark Evergreen Charcoal, không dùng đen tuyền `#000000`). |
| `color-text-body` | `#4B5563` | Màu chữ nội dung, diễn giải, ghi chú phụ (Muted Slate). |
| `color-border` | `#E5E7EB` | Viền hairline 1px siêu mảnh định hình các thẻ và bảng biểu. |

---

## 3. Hệ Thống Kiểu Chữ (Typography Scale)

- **Font Family Chính:** `Plus Jakarta Sans`, sans-serif (Hỗ trợ tiếng Việt hoàn hảo, hiện đại, thân thiện, rõ ràng).
- **Font Tiêu Đề Cảm Xúc (Tùy chọn cho Hero/Quote):** Có thể kết hợp `Newsreader` hoặc giữ đồng bộ `Plus Jakarta Sans` trọng số cao (`font-bold`, `tracking-tight`).

### 3.1 Quy chuẩn cấp bậc (Type Scale)
- **H1 (Hero Heading):** `clamp(2rem, 3.5vw, 3rem)` | `font-bold` | `leading-[1.2]` | `tracking-tight`
  - *Ví dụ:* *“Thảnh thơi trở về tổ ấm sạch trong lành.”* (Từ khóa quan trọng điểm xuyết bằng màu xanh `color-primary`).
- **H2 (Section Heading):** `1.875rem (30px)` – `2.25rem (36px)` | `font-bold` | `leading-snug`
  - *Cấu trúc:* Đi kèm một **Top Eyebrow Pill Badge** phía trên (`text-xs uppercase tracking-wider font-semibold`).
- **H3 (Card Title):** `1.25rem (20px)` – `1.5rem (24px)` | `font-bold`
- **Body Regular:** `1rem (16px)` | `font-normal` | `leading-relaxed` (`line-height: 1.625`)
- **Body Small / Meta:** `0.875rem (14px)` | `font-medium` | `text-gray-500`
- **Price / Highlight Monospace:** `1.75rem (28px)` – `2.25rem (36px)` | `font-extrabold`

---

## 4. Ngôn Ngữ Hình Học & Đổ Bóng (Geometry & Elevation)

- **Bo góc (Border Radius Hierarchy):**
  - Nút bấm chính & Eyebrow Badge: `rounded-full` (Dạng viên thuốc Pill tròn trịa, mềm mại).
  - Thẻ dịch vụ & Container chính: `rounded-2xl` hoặc `rounded-3xl` (`16px` – `24px`).
  - Ô input, chip chọn diện tích: `rounded-xl` (`12px`) hoặc `rounded-full`.
- **Đổ bóng (Shadows & Layering):**
  - **Default Card:** `shadow-sm border border-gray-200/80` (Tối giản viền mảnh, sạch sẽ).
  - **Elevated Card (Thẻ tâm điểm):** `border-2 border-[#2D5A43] shadow-md` (Ví dụ thẻ Nhà Phố hoặc ca khuyến nghị).
  - **Dark Section:** Không đổ bóng, sử dụng nền màu `#1A2F25` tạo cảm giác neo đậu vững chãi và an toàn.

---

## 5. Quy Tắc Bố Cục Đặc Trưng (Haisey-Inspired Layout Patterns)

Khi thiết kế bất kỳ trang con mới nào, tuân thủ các khối cấu trúc sau:

### 5.1 Khối Điều Hướng (Top Navigation)
- Nền trắng tinh gọn hoặc kính mờ (`bg-white/95 backdrop-blur-md`).
- Logo bên trái: Biểu tượng mái nhà/bàn tay mộc mạc + chữ `TỔ ẤM`.
- Trung tâm: Danh sách liên kết tối giản, có gạch chân hoặc đổi màu nhẹ khi active.
- Bên phải: Hotline nổi bật (dạng text click-to-call) + Nút CTA viên thuốc bo tròn màu xanh rêu (`Đặt Lịch Ngay`).

### 5.2 Khối Cam Kết Trực Quan (4 Trust Pill Badges)
- Luôn đặt một cụm badge gồm 4 cam kết ngay dưới mô tả Hero hoặc phần tóm tắt form:
  1. `✓ Thợ mang 100% đồ nghề`
  2. `✓ Nghiệm thu tại chỗ & dọn lại`
  3. `✓ Bảo hiểm an tâm 50.000.000đ`
  4. `✓ Minh bạch - Không phụ phí`
- Định dạng: Thẻ nền trắng bo tròn, chữ nhỏ tinh gọn, icon tick xanh lá nhạt.

### 5.3 Thẻ Giới Thiệu Thiết Bị 3 Cột (ADR-0010 Kit Cards)
- Bố cục 3 cột đồng đều:
  - Khối biểu tượng icon tròn màu pastel dịu nhẹ (`w-12 h-12 rounded-xl bg-emerald-50 text-emerald-700`).
  - Tiêu đề thiết bị in đậm (Máy hút bụi khe hẹp, Khăn microfiber 4 màu, Dung dịch sinh học).
  - Thẻ tag phụ ở chân card khẳng định cam kết (VD: `Chống lây nhiễm chéo`, `100% Organic & Thú cưng`).

### 5.4 Bảng Phân Nhóm Dịch Vụ & Thẻ Tâm Điểm (Hero Service Card)
- Cấu trúc 3 thẻ dịch vụ với phân cấp thị giác rõ ràng:
  - Thẻ bên cạnh: Viền hairline mảnh xám, nút xám nhạt `rounded-full`.
  - Thẻ trọng tâm (ở giữa): Viền đậm `border-2 border-[#2D5A43]`, có badge nhãn `GÓI ĐỘC NHẤT` ở trên cùng, nút bấm CTA nền xanh chính.

### 5.5 Bộ Công Cụ Ước Tính Minh Bạch (Estimation Calculator Widget)
- Cấu trúc 2 cột linh hoạt:
  - Cột trái: Tương tác 2 bước chọn kiểu nhà (Nút bấm toggle) và diện tích thực tế (Dưới 50m², 50-80m², 80-140m², Trên 140m²).
  - Cột phải: Bảng chi tiết hóa đơn tạm tính nền tối hoặc viền nổi bật, liệt kê rõ từng dòng: dụng cụ, hóa chất, bảo hiểm, tổng chi phí trọn gói minh bạch.

### 5.6 Section Nền Đậm "Trụ Cột An Tâm" (High-Contrast Dark Green Anchor)
- Sử dụng màu nền `#1A2F25` với lưới 4 cột card nền `#223B2F` mờ:
  - Đánh số thứ tự lớn: `01`, `02`, `03`, `04` màu xanh ngọc nhạt (`#6B8F71`).
  - Mỗi thẻ giải quyết một nỗi sợ lớn nhất của gia chủ: Thợ lạ, gian lận giờ làm, làm ẩu không ai đền, ảnh chụp không rõ ràng.

---

## 6. Bộ Thành Phần Chuẩn (UI Component Library Spec)

### 6.1 Nút bấm (Buttons)
```html
<!-- Primary CTA Button -->
<button class="px-6 py-3.5 rounded-full bg-[#2D5A43] text-white font-medium text-sm hover:bg-[#224432] transition-colors shadow-sm flex items-center justify-center gap-2">
  <span>Ước Tính Chi Phí & Đặt Ca</span>
</button>

<!-- Secondary Ghost Button -->
<button class="px-6 py-3.5 rounded-full bg-white border border-gray-300 text-[#1F2923] font-medium text-sm hover:bg-gray-50 transition-colors">
  <span>Xem Tiêu Chuẩn Phục Vụ</span>
</button>
```

### 6.2 Eyebrow Badge
```html
<div class="inline-flex items-center gap-2 px-3.5 py-1.5 rounded-full bg-[#6B8F71]/10 text-[#2D5A43] text-xs font-semibold tracking-wide uppercase">
  <span class="w-1.5 h-1.5 rounded-full bg-[#2D5A43]"></span>
  Tiêu Chuẩn Dọn Dẹp Bắc Âu
</div>
```

### 6.3 Standard Service Card (Thẻ dịch vụ)
```html
<div class="bg-white rounded-2xl border border-gray-200 p-6 md:p-8 flex flex-col justify-between hover:border-gray-300 transition-all">
  <div>
    <span class="text-xs font-medium text-gray-500 uppercase tracking-wider">Phổ Biến Nhất</span>
    <h3 class="text-xl font-bold text-[#1F2923] mt-1 mb-2">Căn Hộ Chung Cư</h3>
    <div class="text-2xl font-bold text-[#1F2923] mb-4">240.000 <span class="text-sm font-normal text-gray-500">đ / ca 3h</span></div>
    <ul class="space-y-3 text-sm text-gray-600 mb-6">
      <li class="flex items-start gap-2">✓ 1 Chuyên viên dọn sâu chuẩn quy trình</li>
      <li class="flex items-start gap-2">✓ Hút bụi khe hẹp sofa, gầm giường</li>
    </ul>
  </div>
  <button class="w-full py-2.5 rounded-full bg-gray-100 text-gray-800 text-sm font-medium hover:bg-gray-200">
    Chọn Gói Chung Cư
  </button>
</div>
```

---

## 7. Quy Tắc Nội Dung & Giọng Văn (Voice & Tone)

- **Trung thực, minh bạch:** Giá luôn hiển thị trọn gói không phụ phí giấu kín.
- **Thấu hiểu cuộc sống gia đình:** Sử dụng các từ ngữ như *"Thảnh thơi", "Tổ ấm", "Bình yên trở về", "Con trẻ", "Thú cưng", "An tâm tuyệt đối"*.
- **Cam kết kỹ thuật đo lường được:** Tránh dùng từ sáo rỗng; thay vào đó dùng chỉ số chính xác: *Bảo hiểm 50 triệu, Màng lọc HEPA 99.9%, Khăn microfiber 4 màu, Sai số GPS ≤ 100m*.
