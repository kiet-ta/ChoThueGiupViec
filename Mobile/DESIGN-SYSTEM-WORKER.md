# TỔ ẤM Mobile — Hướng dẫn đồng bộ UI cho phía Worker

Tài liệu bàn giao cho người (hoặc agent) làm lại giao diện **Worker** (thợ + đại lý), để toàn bộ app Mobile giống nhau. Luồng **Customer** đã làm xong (PR #477 … #500). Bạn làm **y hệt cách đó** cho Worker.

> **Bắt buộc:** mọi UI Mobile mới hoặc sửa phải theo [`DESIGN-SYSTEM.md`](DESIGN-SYSTEM.md) (quy chuẩn) và file này (cách áp dụng cho Worker). Agent (Claude / Codex / Antigravity) đọc hai file này trước khi viết UI. Xem trực quan: mở [`design-system/components.html`](design-system/components.html) bằng trình duyệt.

## 1. Đã làm gì ở Customer (để bạn biết bộ kit có sẵn)

| Hạng mục | Kết quả | Ở đâu |
|---|---|---|
| Bộ màu mới | `#2F6B3F` / `#7FB77E` / `#F7C85C` / `#FFF6C0` + màu suy ra đạt tương phản AA | `lib/core/theme/nordic_colors.dart` |
| Token | khoảng cách 4pt, bo góc, kiểu chữ `display` | `nordic_spacing.dart`, `nordic_radius.dart`, `nordic_typography.dart` |
| Chuyển động | spring (`standard` / `momentum` / `shake`), chuyển màn trượt + vuốt mép quay lại, giảm chuyển động | `lib/core/motion/` |
| Phản hồi khi nhấn | `Pressable` (nút, thẻ nhấn được đều co nhẹ khi chạm) | `lib/core/widgets/pressable.dart` |
| Khung màn | `NordicPage.tab` / `NordicPage.pushed`, nút chính luôn nằm trên bàn phím | `lib/core/widgets/nordic_page.dart` |
| Thẻ | `NordicCard` phẳng, viền mảnh, bo 20 | `nordic_card.dart` |
| Màn chào + đăng nhập | ảnh nền thật, chữ trắng, tấm form bo trên | `home_screen.dart`, `identity/widgets/auth_photo_header.dart` |
| Thanh tab dưới | nền mờ trong suốt, viên xanh nhạt trượt theo tab đang chọn | `home/customer_shell.dart` |
| Câu chữ | ngắn, đời thường, viết hoa kiểu câu | bảng ở §6 |

Quyết định của chủ dự án (đừng đổi): **giữ bo tròn** (đã thử vuông, bỏ), bảng màu như trên, ảnh Unsplash ở `assets/images/welcome/`, nút nằm trên bàn phím, mọi màn đi qua `NordicPage`.

## 2. Dùng gì (cheat sheet)

```dart
// Khung màn đẩy vào (có nút quay lại), nút chính trên bàn phím
NordicPage.pushed(
  title: 'Nhận việc',
  actions: [NordicButton(label: 'Nhận việc', onPressed: _accept)],
  onRefresh: _load,                       // kéo để tải lại (tuỳ chọn)
  children: [ ... ],
);

// Khung tab (tiêu đề lớn, không app bar)
NordicPage.tab(title: 'Việc', headerAction: Text('3 việc'), children: [ ... ]);

// Thẻ nhấn được: tự co khi chạm
NordicCard(onTap: _open, child: Row(...));

// Báo lỗi nhập sai: rung + rung máy
ErrorShake(errors: _errors, child: form);   // _errors++ mỗi lần sai

// Chữ lớn nhất của màn
Text('Hôm nay', style: NordicTypography.display);
```

- **Màu / chữ / khoảng cách / bo góc:** chỉ dùng `NordicColors`, `NordicTypography`, `NordicSpace` (xxs 4 · xs 8 · sm 12 · md 16 · gutter 20 · lg 24 · section 28 · xl 32), `NordicRadius` (card 20 · field 14 · tile 12 · sheet 28 · pill). Không viết số tay (`BorderRadius.circular(16)`, `Color(0xFF…)`, `SizedBox(height: 12)`).
- **Chuyển động:** dùng `NordicMotion.standard` cho thứ tự xuất hiện / di chuyển, `momentum` chỉ sau cú vuốt, `shake` cho nhập sai. Không tự đặt `Duration` + `Curves`. Chuyển màn **chỉ** `Navigator.pushNamed` / `MaterialPageRoute`, không tự làm hiệu ứng.
- **Giảm chuyển động:** `NordicMotion.reduced(context)` → bỏ trượt / co / rung, chỉ mờ dần.

## 3. Mẫu màn

| Mẫu | Dùng cho | Cách làm |
|---|---|---|
| **Màn tab** | Hôm nay, Lịch, Việc, Thu nhập, Tài khoản | `NordicPage.tab`; thanh tab dưới giống `customer_shell.dart` |
| **Màn đẩy vào** | chi tiết việc, eKYC, báo sự cố | `NordicPage.pushed`, tiêu đề là danh từ ("Chi tiết việc"), không để mã `#id` |
| **Form** | eKYC, báo sự cố, vắng mặt | nhãn nhỏ trên ô nhập, báo lỗi dưới ô, nút gửi trong `actions`, lỗi thì `ErrorShake` |
| **Danh sách** | lời mời, lịch sử thu nhập | mỗi mục một `NordicCard` cách nhau `NordicSpace.sm`, trạng thái bằng `StatusPill` |
| **Chi tiết** | việc đang làm | con số lớn trước (giờ / tiền), rồi trạng thái, rồi thông tin, rồi hành động |
| **Trạng thái** | tải / lỗi / trống | `LoadingStateWidget`, `ErrorStateWidget` ("Thử lại"), `EmptyStateWidget` |

Toàn chiều rộng điện thoại (không giới hạn 390px); lề trái phải 20px.

## 4. Bản đồ các màn Worker → mẫu cần áp

Đường dẫn trong `Mobile/lib/features/`.

| Màn | File | Mẫu | Ghi chú |
|---|---|---|---|
| Khung Worker + thanh tab | `home/worker_shell.dart` | thanh tab mờ như `customer_shell.dart` | tab 0–4, nút giữa "Việc" (`worker-shell-job`) giữ key |
| Hôm nay | `home/worker_today_tab.dart` | `NordicPage.tab` | lưới ca giữ nguyên chức năng, bấm ô mở Lịch |
| Lời mời nhận việc | `dispatch/screens/dispatch_offers_screen.dart`, `dispatch/widgets/job_offer_card.dart` | Danh sách + `NordicCard` | đồng hồ đếm ngược 30 giây là con số lớn |
| Check-in | `dispatch/screens/check_in_screen.dart` | Form / Chi tiết | nút chính trên bàn phím |
| Khách vắng mặt | `dispatch/screens/customer_absent_screen.dart` | Form | |
| Báo sự cố | `dispatch/screens/incident_report_screen.dart` | Form | lỗi → `ErrorShake` |
| eKYC | `workers/screens/worker_ekyc_screen.dart` | Form | |
| Lịch rảnh | `workers/screens/worker_slots_screen.dart` | Màn tab | |
| Thực hiện ca, ảnh | `workers/screens/worker_execution_screen.dart`, `worker_photos_screen.dart` | Chi tiết + Form | |
| Làm thêm giờ | `workers/screens/worker_extension_screen.dart` | Form | |
| Thu nhập, lịch sử | `payouts/screens/payout_screen.dart`, `payout_history_screen.dart` | Màn tab / Danh sách | số tiền là con số lớn |
| Đại lý | `agencies/screens/agency_screen.dart`, `agency_schedule_screen.dart` | Màn tab / Danh sách | |
| Đăng ký thợ mới | `identity/screens/worker_registration_screen.dart` | Form | sau khi vào OTP lần đầu; chưa theo mẫu ảnh nền |
| Đánh giá, khiếu nại (dùng chung) | `ratings/…`, `disputes/…` | **đã làm** | giữ nguyên, chỉ kiểm lời thoại phía thợ |

Không đụng: `core/network/*`, `*_service.dart`, `models/`, `routes.dart`, key của widget (test dùng).

## 5. Quy trình làm một màn (công thức)

1. Mở màn, xem nó đang là `Scaffold` + `ListView` + `bottomNavigationBar`.
2. Đổi sang `NordicPage.tab` hoặc `.pushed`; nút chính chuyển vào `actions:`.
3. Đổi số tay → token (`NordicSpace.*`, `NordicRadius.*`, `NordicColors.*`). Xoá `ConstrainedBox(maxWidth: 390)`.
4. Thẻ tự viết (`Material` + `InkWell` + `BorderRadius`) → `NordicCard(onTap:)`.
5. Form: thêm `int _errors` + `ErrorShake`; sai thì `setState(() => _errors++)`.
6. Viết lại câu chữ theo §6.
7. Cập nhật test **chỉ phần câu chữ**; test cũ phải còn chạy (key, hành vi giữ nguyên).
8. Chạy `flutter analyze` (không thêm lỗi), `flutter test`, rồi `sh harness/verify.sh L3`.
9. Mỗi nhóm màn = 1 ticket = 1 PR (đúng `AGENTS.md`: ticket có trạng thái `in-progress`, nhánh `ticket/<số>-<slug>`, chỉ sửa file trong *Allowed paths*).

## 6. Câu chữ (phía Worker)

Tiếng Việt, **viết hoa kiểu câu**, ngắn, từ thông dụng, gọi người dùng là "bạn". Dùng **"thợ"** cho người làm, **"gia chủ"** cho khách (giữ như hiện tại, đã có trong các màn Worker). Số thập phân dùng dấu phẩy (`4,8`), tiền `260.000 đ`, giờ `13:00`.

| Tránh | Viết |
|---|---|
| Xác Nhận Nhận Việc | Nhận việc |
| Vui lòng nhập … hợp lệ | … chưa đúng. Ví dụ: … |
| CHỜ NGHIỆM THU (in hoa) | Chờ nghiệm thu |
| Economy / Premium | Cơ bản / Cao cấp |
| Before / After | Trước / Sau |
| Lỗi + mã kỹ thuật | một câu nói rõ lỗi gì và cách làm tiếp |

Thông báo lỗi do máy chủ trả về thì hiện nguyên văn, không tự dịch.

## 7. Lưu ý đã gặp (tránh mất thời gian)

- Sửa bằng `sed`/`perl` trên file CRLF dễ trượt; dùng công cụ sửa file, rồi `dart format`.
- `dart format` phải sạch trước khi commit (CI kiểm `dart format --set-exit-if-changed`).
- Bàn phím che nút: nút phải nằm **trong body** (`NordicPage(actions:)` đã làm sẵn), không để ở `Scaffold.bottomNavigationBar`.
- Test cuộn: màn đã đổi `SingleChildScrollView` → `ListView` của `NordicPage`; sửa test tìm `ListView`.
- Ảnh trong `assets/` chỉ dùng ảnh miễn phí có ghi nguồn (xem `assets/images/welcome/CREDITS.txt`).
- Xem thử bản web: thư mục kit test local (xem `ARCHITECTURE.md` mục Verification).

## 8. Danh sách "xong" cho mỗi màn

- [ ] Dùng `NordicPage`; tiêu đề bằng chữ, không `#id`.
- [ ] Chỉ token: màu, chữ, khoảng cách, bo góc.
- [ ] Thẻ / nút nhấn được có phản hồi khi chạm (`NordicCard(onTap:)`, `NordicButton`).
- [ ] Nút chính trên bàn phím; form sai có `ErrorShake` + thông báo.
- [ ] Câu chữ theo §6.
- [ ] Có trạng thái tải, lỗi, trống.
- [ ] Đẹp ở 360px và 428px; giảm chuyển động không vỡ.
- [ ] Key, hành vi, lời gọi service không đổi; test đã sửa phần chữ; `verify L3` PASSED.
