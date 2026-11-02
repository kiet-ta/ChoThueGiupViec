# Customer redesign v2 — spec cho agent code (Mobile)

> Owner: Duy (M3, nền Mobile) · Ngày: 2026-10-30 · Trạng thái: **đã code trong ticket #554 và #560; đang nghiệm thu**
> Bản thiết kế (canvas, bấm thử được): https://claude.ai/artifact/98zVVg82Br7C56Zt6KXWuV
> - Hàng 1: Trang chủ (`Main` = chưa có ca, `Booked` = có ca sắp tới)
> - Hàng 2: `Orders`, `OrderDone`, `OrderCancelled`, `Favorites`, `Account`
>
> File này là **nguồn sự thật** cho đợt làm lại luồng customer. Khi spec, canvas và code lệch nhau:
> spec này thắng canvas; `AGENTS.md` thắng tất cả. Thiếu gì thì hỏi trên issue, không đoán.

---

## 0. Phạm vi và nguyên tắc

**Trong phạm vi:** giao diện và copy của luồng customer trên Mobile (Flutter):
shell, Trang chủ, Đơn, Chi tiết đơn, Yêu thích, Tài khoản; sau đó áp cùng ngôn ngữ cho
Đặt ca, Thanh toán, Nghiệm thu, Đánh giá, Khiếu nại, Hồ sơ, Sổ địa chỉ.

**Quyết định đã chốt (không đổi):**

| # | Quyết định |
|---|---|
| D1 | Áp đúng bảng màu mới, một font **Be Vietnam Pro**, card có chiều sâu (`NordicElevation`). Không quay về card phẳng. Token dùng chung nên phía Worker cũng đổi màu/font theo. |
| D2 | **Không có dịch vụ nấu ăn**: `ServiceTier` chỉ có `ECONOMY` / `PREMIUM`. |
| D3 | **Giữ** nút tròn "Đặt ca" ở giữa thanh điều hướng customer. |
| D4 | Thiết bị **không tính tiền**: mọi số tiền lấy từ server. |
| D5 | Không bịa tính năng chưa có API: thông báo, lịch định kỳ, chat, biểu đồ chi tiêu, ví, thời điểm từng bước tiến trình. |
| D6 | Chỉ đổi UI và copy. Không đổi model, route hay luật nghiệp vụ. Ngoại lệ duy nhất được phép: thêm tham số `status` **tuỳ chọn** vào `IOrderService.listOrders` (§4.1), vì contract `booking.md` 3.4 đã có sẵn. |
| D7 | Giữ nguyên mọi `Key` mà test đang dùng (danh sách ở từng màn). Key mới đặt theo mẫu `<màn>-<thứ>`. |

---

## 1. Trạng thái code hiện tại (đã làm, CHƯA chạy analyze/test)

Đang nằm chưa commit trên working tree `main`:

| File | Nội dung |
|---|---|
| `lib/core/theme/nordic_colors.dart` | bảng màu mới (§2.1), thêm `onDarkMuted` |
| `lib/core/theme/nordic_typography.dart` | `fontFamily` = `headlineFontFamily` = `'Be Vietnam Pro'`; `greeting` 28/700; `number*` 700, tracking −0.02 em |
| `lib/core/theme/nordic_elevation.dart` | **mới**: `card`, `raised`, `cta`, `segment` |
| `lib/core/widgets/nordic_card.dart` | bóng `card` mặc định, tham số `elevated` |
| `lib/features/home/customer_home_tab.dart` | Trang chủ viết lại theo canvas |
| `lib/features/home/customer_shell.dart` | nút giữa có bóng `cta`, `elevation: 0` |
| `assets/fonts/BeVietnamPro-*.ttf`, `OFL-BeVietnamPro.txt`, `pubspec.yaml` | font mới; bỏ khai báo Newsreader / Plus Jakarta Sans |
| `test/theme_test.dart`, `test/features/home/customer_tabs_test.dart` | sửa theo token và hành vi mới |
| `DESIGN-SYSTEM.md`, `ARCHITECTURE.md` | §3.1, §3.2, §3.5, §4, §5 Home đã cập nhật |

Việc đầu tiên của agent: làm phần này chạy xanh (§7, bước 3).

---

## 2. Ngôn ngữ thiết kế dùng chung

### 2.1 Token (đã có trong code — chỉ dùng token, không viết hex)

| Vai trò | Token | Giá trị |
|---|---|---|
| Xanh đậm (nút chính, header tối, link) | `primary`, `contrastDark` | `#0F3D2E` |
| Xanh nhấn (viền chọn, check, chữ nhấn) | `primaryContainer`, `secondaryAccent` | `#1F7A55` |
| Xanh nhạt (pill ok, tab đang chọn) | `primaryFixed` | `#DCEDE3` |
| Nền màn | `canvas` | `#EEF1EC` |
| Ô đã chọn, ô mã đơn | `surfaceSubtle` | `#F4F6F2` |
| Nền pill trung tính, avatar | `surfaceContainer` | `#E6EBE7` |
| Chữ | `textTitle` / `textBody` / `textSecondary` | `#101814` / `#34433B` / `#5E6A63` |
| Chữ phụ trên nền tối | `onDarkMuted` | `#A9C7B8` |
| Vàng (sao, chấm) / kem (cảnh báo) | `accentAmber` / `amberSoft` + `onAmberSoft` | `#F2B544` / `#FFF6C0` + `#5F4100` |
| Viền | `border` | `#DDE3DF` |
| Lỗi | `error` / `errorContainer` | `#BA1A1A` / `#FFDAD6` |

Nếu cần màu chưa có token (ví dụ đường kẻ trong card `#E6EBE7`): dùng token gần nhất
(`surfaceContainer`). Chỉ thêm token mới khi thật cần, và ghi lại vào `DESIGN-SYSTEM.md` §3.1.

### 2.2 Các mẫu dựng sẵn — nên tách thành widget dùng chung trong `lib/core/widgets/`

| Widget đề xuất | Mô tả | Dùng ở |
|---|---|---|
| `DarkHeader` | Nền `contrastDark`, bo dưới `NordicRadius.sheet + 4`, ba vòng tròn trang trí trắng 7 % ở góc phải trên (`CustomPainter`, `ExcludeSemantics`), tràn lên mép trên (tự cộng `MediaQuery.padding.top`). Đáy chừa `overlap + 28` để card nổi đè lên. Tham số: `child`, `overlap` (mặc định 64), `appBar` tuỳ chọn (nút back 44×44 nền trắng 8 % + tiêu đề giữa 17/600 trắng). | Home, Chi tiết đơn, Tài khoản |
| `RaisedPanel` | Card trắng bo `NordicRadius.sheet`, padding 20, bóng `NordicElevation.raised`, lề ngang 16. Đặt trong `Transform.translate(Offset(0, -overlap))` cùng phần nội dung bên dưới; danh sách bớt `overlap` ở padding đáy. | Home, Chi tiết đơn, Tài khoản |
| `GlassPanel` | Trên nền tối: fill trắng 8 %, viền trắng 14 %, bo `card`. Không đổ bóng. | ca sắp tới trên Home, pill địa chỉ, nút "Sửa hồ sơ" |
| `GroupedListCard` (đã có) | Một card trắng chứa nhiều dòng, chia bằng hairline; mỗi dòng ≥ 64 px, icon trong ô 40×40 bo `tile` nền `surfaceSubtle`, tiêu đề 15/600, dòng phụ 12 `textSecondary`, mũi tên phải. Cập nhật cho có bóng `card` và dòng phụ. | Đơn (mỗi tháng), Việc khác, Tài khoản |
| `CtaButton` (hoặc `NordicButton` + `DecoratedBox` bóng `cta`) | Nút pill tối cao 56, chữ 16/600, icon trái tuỳ chọn. | thanh dưới các màn chi tiết, khung đặt ca |

### 2.3 Pill trạng thái đơn — dùng `StatusPill` với tone, chữ lấy từ `orderStatusLabel`

Quy tắc của owner (2026-10-30): **hoàn thành = xanh lá, mọi giai đoạn ở giữa = vàng, huỷ = đỏ.**

| `orderStatus` | Chữ (đã có) | Tone |
|---|---|---|
| `PENDING_PAYMENT`, `PAID`, `DISPATCHING`, `AWAITING_FALLBACK`, `ASSIGNED` | Chờ thanh toán / Đã thanh toán / Đang tìm người làm / Chờ bạn chọn / Đã có người làm | warn (vàng) |
| `COMPLETED` | Hoàn thành | ok (xanh lá) |
| `CANCELLED` | Đã huỷ | bad (đỏ) |

Pill trên nền tối giữ màu của tone (nền sáng, chữ đậm), không đổi theo nền.

### 2.4 Thanh điều hướng và thanh hành động
- Thanh tab customer: nền trắng (mờ như hiện tại cũng được), viền trên `border`. Tab đang chọn có pill `primaryFixed`, chữ 12/700 `primary`; tab khác 12/500 `textSecondary`. Nút giữa 60×60 `primary`, viền 4 px màu `canvas`, bóng `cta`, nhãn "Đặt ca" bên dưới.
- Mọi tab screen chừa đáy ≥ 120 px + safe area để nút giữa không che nội dung (đây là lỗi ở ảnh chụp cũ: chữ "Hà" bị che).
- Màn pushed có việc chính: `BottomActionBar` trắng, viền trên, nút chính là `CtaButton`, có thể kèm một dòng gợi ý 12 px phía trên và một nút ghost phía dưới.

---

## 3. Trang chủ — đã code, chỉ cần xác minh

Theo canvas `Main` / `Booked` và §5 "Home" trong `DESIGN-SYSTEM.md`. Key giữ:
`home-greeting`, `home-next-order`, `home-no-order`, `home-next-error`, `home-quote-ECONOMY`,
`home-quote-PREMIUM`, `quick-quote-address`. Key mới: `home-book`, `home-address`,
`home-add-address`, `home-favorite-<id>`, `home-book-again-<id>`.
Nếu có `DarkHeader` / `RaisedPanel` (§2.2) thì refactor Home sang dùng chúng, hành vi giữ nguyên.

---

## 4. Các màn cần làm lại

### 4.1 Đơn — `lib/features/booking/screens/order_history_screen.dart` (canvas `Orders`)

**Lỗi đang có (bắt buộc sửa):** hàm nhóm theo tháng (dòng ~171) chỉ gom các đơn **liền kề** theo
thứ tự server. Server trả đơn mới tạo trước (`createdAt` desc, `booking.md` 3.4), nên một tháng
bị lặp ("Tháng 10" → "Tháng 11" → "Tháng 10"). Sửa thành:
1. Gom theo khoá `yyyy-MM` của `scheduledDate` qua **toàn bộ** các trang đã tải (Map, không phụ thuộc thứ tự).
2. Các tháng xếp giảm dần. Trong tháng: `scheduledDate` giảm dần, rồi giờ bắt đầu ca giảm dần.
3. "Xem thêm" nạp trang mới thì gom lại từ đầu, không nối thêm nhóm trùng.
4. Viết thêm unit test cho hàm gom (đưa vào logic thuần, ví dụ `groupOrdersByMonth` trong `order_logic.dart`).

**Bố cục (trên → dưới):**
1. Tiêu đề tab "Đơn" (`display`) + bên phải "N đơn" (`total` của server, key `history-count`).
2. Thanh lọc dạng chip pill: **Tất cả · Hoàn thành · Đã huỷ**.
   - Chip được chọn: nền `primary`, chữ trắng. Chip khác: nền trắng, viền `border`.
   - "Hoàn thành" và "Đã huỷ" gọi server với `status=COMPLETED` / `status=CANCELLED`
     (thêm tham số `String? status` tuỳ chọn vào `IOrderService.listOrders` và `OrderService`; mặc định không gửi).
   - Không có chip "Sắp tới" lọc phía server: API chỉ nhận MỘT status. Phần "Sắp tới" là mục 3 bên dưới.
   - Đổi chip thì tải lại từ trang 1; `history-count` hiện `total` của bộ lọc đang chọn.
3. Mục **"Sắp tới"** (chỉ ở chip "Tất cả", chỉ khi có): các đơn chưa kết thúc (khác `COMPLETED` / `CANCELLED`)
   có giờ bắt đầu ca ≥ hiện tại (Asia/Ho_Chi_Minh). Cách tính dùng lại logic của Home (`_startsAt`); nên tách
   ra `order_logic.dart` để hai màn dùng chung. Mỗi đơn là một card tối nổi (`contrastDark`, bóng `raised`):
   - Dòng caps `onDarkMuted`: "CHỦ NHẬT, 01/11 · CA SÁNG" + pill trạng thái.
   - Giờ bắt đầu số lớn (`numberLarge` trắng) + "đến hh:mm".
   - Số tiền `totalAmount` bên phải.
   - Nếu `progress` có người làm thì thêm dòng người làm (avatar, tên, gói · diện tích · số người). Không gọi thêm API chỉ để vẽ dòng này; không có thì bỏ dòng.
   - Card này cũng nằm trong nhóm tháng bên dưới (không ẩn).
4. Mỗi tháng: tiêu đề "Tháng 10/2026" (15/700) + "N đơn" bên phải; MỘT card nhóm, mỗi đơn một dòng:
   - Ô ngày 48×52 bo 14: số ngày 18/700 + thứ viết tắt (T2…CN). Nền `primaryFixed` / chữ `primary`; đơn huỷ dùng nền `surfaceLow` / chữ `textSecondary`.
   - Tiêu đề "Ca chiều · Cơ bản", dòng meta "13:00–17:00 · 260.000 đ", mã đơn 11 px "Mã GV…".
   - Pill trạng thái bên phải (§2.3). Đơn huỷ: tiêu đề màu `textBody`.
   - Cả dòng là vùng bấm (≥ 72 px), mở `/booking/detail`.
5. Trạng thái: loading (`LoadingStateWidget`), lỗi có "Thử lại", trống theo bộ lọc
   ("Chưa có đơn nào" + nút "Đặt ca" ở Tất cả; "Không có đơn hoàn thành" / "Không có đơn đã huỷ" ở các chip khác),
   lỗi trang sau giữ danh sách cũ (key `history-more-error`).

**Key giữ:** `history-count`, `order-<id>`, `order-status-<id>`, `history-more`, `history-more-error`.
**Key mới:** `history-filter-all`, `history-filter-COMPLETED`, `history-filter-CANCELLED`, `history-upcoming-<id>`.

### 4.2 Chi tiết đơn — `order_detail_screen.dart`, `fallback_prompt_card.dart` (canvas `OrderDone`, `OrderCancelled`)

**Lỗi đang có (bắt buộc sửa):** `orderTimeline(CANCELLED)` trả về một bước "Đã huỷ" `done: true`,
nên màn vẽ **dấu tích xanh**, trông như hoàn thành. Sửa:
- Thêm trường `cancelled` (hoặc `failed`) cho `TimelineStep`, vẽ chấm đỏ `error` có dấu ✕ và chữ `error`.
- Khi `CANCELLED`: nếu model không có dữ liệu cho biết đơn dừng ở bước nào (hiện **không có**: `Order` chỉ có
  `cancelReason`, không có `paidAt` hay mốc từng bước), thì **chỉ hiện một dòng** "Đơn đã huỷ" với ✕ đỏ.
  Không tự suy ra bước. (Canvas vẽ ✕ ở bước 2 chỉ để minh hoạ; khi nào có dữ liệu thật mới làm như vậy.)
- Cập nhật unit test của `orderTimeline`.

**Bố cục chung:**
1. `DarkHeader` có app bar: back + "Chi tiết đơn".
2. Trong header: pill trạng thái (§2.3); giờ bắt đầu cỡ 44/700 (key `order-schedule` giữ trên phần giờ / ngày) + "đến hh:mm";
   dòng "Thứ Sáu, 30/10/2026 · Ca sáng" màu `onDarkMuted`. Đơn huỷ: số giờ màu nhạt hơn.
3. `RaisedPanel` đè lên header:
   - Đơn huỷ: khối kem (`amberSoft` / `onAmberSoft`, bo 16) đầu card, tiêu đề + câu giải thích. Câu lấy từ text đã map sẵn
     trong `order_logic.dart` (dòng ~212–263, theo `cancelReason`). Không có lý do khớp thì dùng câu chung "Đơn này đã huỷ.".
   - Hàng "Tổng tiền" + số 26/700 (key `order-amount`). Pill "Đã thanh toán" **chỉ** khi trạng thái là
     `PAID`, `DISPATCHING`, `AWAITING_FALLBACK`, `ASSIGNED` hoặc `COMPLETED`. Đơn huỷ: không hiện pill thanh toán và không nói gì về hoàn tiền, trừ khi câu lý do đã nói.
   - Lưới 3 cột: Gói / Diện tích / Người làm (đơn huỷ: cột 3 là Số tiền thay cho hàng Tổng tiền).
   - Ô mã đơn nền `surfaceSubtle` bo 14, mã 12/600 (key `order-code`), nút sao chép 44×36
     (`Clipboard.setData`, `Semantics` "Sao chép mã đơn", snackbar "Đã sao chép mã đơn").
4. Mục "Tiến trình" (key `order-timeline`): card ngang 4 bước. Chấm 28 px `primaryContainer` có ✓ cho bước xong;
   bước hiện tại viền `primaryContainer` 2 px; bước chưa tới viền `border`. Đường nối 2 px. Đơn huỷ: xem phần sửa lỗi ở trên.
5. Mục "Người làm" + "x/y" bên phải: mỗi người một card (avatar có tint, tên, ★ điểm, pill trạng thái phân công).
   Đơn huỷ không có người: **ẩn cả mục** (thay cho câu "Đơn đã huỷ, không có người làm.").
   Giữ `no-assignments` cho các trạng thái chưa huỷ.
6. Mục "Việc khác": `GroupedListCard`, chỉ hiện các việc đang có ở màn hiện tại. Mỗi dòng giữ key cũ:
   - "Xem lại nghiệm thu" (`review-acceptance-<id>`), dòng phụ "Ảnh trước và sau khi dọn".
   - "Đặt lại với <tên>", dòng phụ "Cùng gói, cùng địa chỉ": **chỉ** khi gói `ECONOMY` (`BookingScreenArgs.preferredWorkerId` chỉ dùng cho Economy).
     Mở `/booking` với `serviceTier`, `addressId` (nếu `Order` có; không có thì bỏ) và `preferredWorkerId`. Đây là đường đi mới nhưng không đổi luật: chỉ mở form với lựa chọn sẵn.
   - "Khiếu nại về đơn này" (`dispute-order`), dòng phụ "Trong 24 giờ sau khi xong ca".
   - "Huỷ đơn" (`open-cancel`) khi được phép: dòng chữ `error`, mở form huỷ như cũ.
7. `BottomActionBar` theo trạng thái, đúng MỘT nút chính:

| Trạng thái | Nút chính | Phụ |
|---|---|---|
| `PENDING_PAYMENT` | "Thanh toán · số tiền" (`pay-order`) | — |
| `AWAITING_FALLBACK` | giữ hai nút của `FallbackPromptCard` (`fallback-accept` / `fallback-decline`); card nằm ngay dưới RaisedPanel | — |
| `ASSIGNED` + có người đang làm | "Làm lần 2" / `open-extension` như hiện tại | — |
| `COMPLETED`, có phân công `COMPLETED` chưa đánh giá | "Đánh giá người làm" (`rate-<id>`), dòng gợi ý "Bạn có 48 giờ sau khi xong ca để đánh giá." | — |
| `AWAITING_ACCEPTANCE` (phân công) | "Xác nhận nghiệm thu" (`accept-<id>`) | "Xem ảnh" |
| `CANCELLED` | "Chọn lịch khác" (`order-rebook`, hành vi giữ nguyên) | ghost "Về danh sách đơn" (pop) |

   Khi có nhiều phân công cùng cần hành động: nút chính áp cho người đầu tiên; các người khác có nút trong card của họ.
8. Card gia hạn, form gia hạn, form huỷ: giữ logic và key, đổi giao diện sang `NordicCard` + token mới.

**Key giữ:** tất cả key đã liệt kê bằng `grep "Key('" order_detail_screen.dart fallback_prompt_card.dart`. Không xoá key nào.

### 4.3 Yêu thích — `lib/features/customers/screens/favorite_workers_screen.dart`, `widgets/favorite_worker_card.dart` (canvas `Favorites`)

1. Tiêu đề "Yêu thích" + dòng phụ "N người giúp việc quen" (key `favorites-count`).
2. Mỗi người một card (`NordicCard`, bo 22, padding 16):
   - Hàng trên: avatar 52 với tint xoay vòng (`amberSoft` / `primaryFixed` / `surfaceContainer`), tên 16/600,
     "★ 4,7 · 41 ca đã làm" (`ratingText`, `completedJobs`), nút tim 44×44 bên phải (nền `primaryFixed`, tim đặc `primary`).
   - Đường kẻ, rồi hàng dưới: chữ 12 "Ngày rảnh của <tên ngắn> hiện ở bước chọn lịch" + nút pill tối nhỏ "Đặt với <tên ngắn>"
     (cao 44, bóng `cta`, gọi `onBookJob` như hiện tại).
   - Bỏ pill "Xem lịch khi đặt ca" và nút viền dài cũ.
3. Bấm tim: gọi `removeFavoriteWorker` như hiện tại. Nếu service có hàm thêm lại (kiểm tra `FavoriteWorkerService` bằng grep)
   thì hiện snackbar "Đã bỏ <tên> khỏi yêu thích" + "Hoàn tác". Không có thì hỏi xác nhận bằng dialog trước khi xoá.
   Không thêm API.
4. Cuối danh sách: hộp thông tin nền `primaryFixed`, icon (i):
   "Đặt với người quen áp dụng cho gói Cơ bản. Thêm người vào đây bằng nút tim sau khi xong ca."
   Câu sau chỉ được giữ nếu app thật sự có nút tim ở chỗ khác (grep `addFavorite`); không có thì chỉ giữ câu đầu.
5. Trạng thái trống: "Chưa có người giúp việc quen" + một câu hướng dẫn; loading và lỗi như chuẩn.

### 4.4 Tài khoản — `lib/features/home/customer_account_tab.dart` (canvas `Account`)

1. `DarkHeader` (không app bar): tiêu đề "Tài khoản" (`display` trắng); hàng hồ sơ gồm avatar 60 nền `primaryFixed`,
   tên 18/700, số điện thoại `onDarkMuted`, nút `GlassPanel` "Sửa hồ sơ" (giữ key `account-profile` trên vùng mở hồ sơ).
2. `RaisedPanel` 3 cột có vạch chia: số đơn đã đặt (`total`), số địa chỉ, tháng/năm tham gia. Số 24/700, nhãn 12.
   Giá trị đọc từ dữ liệu thật như hiện tại. Không có dữ liệu thì hiện "—".
3. Nhóm "Đặt ca" (`GroupedListCard`): Địa chỉ của tôi (`account-addresses`; dòng phụ "Mặc định: <địa chỉ> · <m²>" nếu có),
   Đơn đã đặt (`account-orders`, dòng phụ "Theo dõi, thanh toán, đánh giá"), Người giúp việc yêu thích (chuyển sang tab Yêu thích; dòng phụ "N người" nếu đã có số).
4. Nhóm "Hỗ trợ": Khiếu nại (`account-disputes`, dòng phụ "Theo dõi khiếu nại đã gửi").
5. Card riêng "Đăng xuất" chữ `error` (`home-sign-out`, dialog như cũ).

### 4.5 Các màn còn lại — áp cùng ngôn ngữ, không có canvas riêng

| Màn | File | Hướng |
|---|---|---|
| Đặt ca | `booking/screens/booking_screen.dart` | Nền sáng. Các bước (gói → địa chỉ → ngày → ca → ghi chú) là ô chọn viền `primaryContainer` + check, giống ô gói trên Home. Giá báo của server là `RaisedPanel` ở cuối. Nút "Đặt ca · giá" trong `BottomActionBar`. |
| Thanh toán | `payments/screens/payment_screen.dart` | `DarkHeader` với số tiền lớn + đếm ngược; `RaisedPanel` chứa nội dung QR / chuyển khoản và nút sao chép; banner sandbox giữ nguyên. |
| Nghiệm thu | `workers/screens/customer_acceptance_screen.dart` | Lưới ảnh trước/sau trong `NordicCard`; bảng tiền dạng lưới như Chi tiết đơn; nút chính "Xác nhận nghiệm thu", phụ "Yêu cầu dọn lại". |
| Đánh giá, Khiếu nại | `ratings/screens/**`, `disputes/screens/**` | Dùng chung với Worker: chỉ đổi bố cục/token; copy phía Worker giữ nguyên. |
| Hồ sơ, Sổ địa chỉ, Form địa chỉ | `customers/screens/**` | Nền sáng, `GroupedListCard`, địa chỉ mặc định có pill ok. |

---

## 5. Không làm (ghi rõ trong PR)
Dịch vụ nấu ăn; chuông thông báo; lịch định kỳ; chat với người làm; thời điểm từng bước tiến trình;
thông tin hoàn tiền khi API không trả; chip "Sắp tới" lọc phía server.

## 6. Done cho mỗi màn
- [ ] Đúng bố cục ở §3/§4 và khớp canvas về thứ bậc (khoảng cách ±4 px là được).
- [ ] Chỉ dùng token và component: không hex, bán kính hay bóng viết tay; không `elevation` số của Material.
- [ ] Loading / lỗi có "Thử lại" / trống.
- [ ] Không overflow ở 360 và 428 px, `textScaler` 1.3, khi bàn phím mở; tôn trọng reduced motion.
- [ ] Vùng chạm ≥ 44 px; nút chỉ có icon thì có `Semantics` label; tương phản chữ ≥ 4.5:1.
- [ ] Key cũ còn nguyên; test cũ chỉ sửa theo copy hoặc hành vi UI mới; test mới cho logic thuần mới (gom tháng, timeline huỷ).
- [ ] `flutter analyze` sạch, `flutter test` pass.

## 7. Thứ tự làm và cách nghiệm thu
1. Đọc `AGENTS.md`, `Mobile/ARCHITECTURE.md`, `Mobile/DESIGN-SYSTEM.md`, `Mobile/DESIGN-SYSTEM-WORKER.md`, file này.
2. Ticket + nhánh theo prompt kèm theo (một ticket, một PR, nhiều commit).
3. Cho phần §1 chạy xanh → commit.
4. Widget dùng chung §2.2 (+ refactor Home sang dùng chúng) → commit.
5. §4.1 Đơn → commit. §4.2 Chi tiết đơn → commit. §4.3 + §4.4 → commit. §4.5 → 1–2 commit.
6. Cập nhật `Mobile/design-system/components.html` và `DESIGN-SYSTEM.md` (§4 thêm widget mới, §5 thêm mẫu màn chi tiết có header tối).
7. `sh harness/verify.sh L3`; ảnh 390×844 của mọi màn ở mọi trạng thái chính; mở PR.
