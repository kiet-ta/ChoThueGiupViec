# Customer v2 · khớp template — spec cho agent code (Mobile)

> Owner: Duy · Ngày: 2026-10-30 · Trạng thái: **chưa code**
> Template (nguồn hình): `Mobile/ui-templates/customer-v2.html` (trang tổng quan) và `Mobile/ui-templates/customer-v2/*.html` (18 màn, 390×844).
> Bản có link cho Leader: https://claude.ai/artifact/PuCGGvMajVVGdTc16PrvtQ
> File liên quan: `design-system/customer-redesign-v2.md` (spec v2, phần lớn đã code ở PR #557).

Mục tiêu duy nhất của đợt này: **app Flutter phía customer nhìn giống template**, màn nào cũng vậy.
Phần lớn §1–§4.4 của spec v2 đã đúng; file này chỉ liệt kê **chỗ còn lệch** và cách sửa, kèm code tham khảo.

---

## 0. Luật chơi

### 0.1 Thứ tự ưu tiên khi các nguồn lệch nhau

1. `AGENTS.md` (analyze sạch, test xanh, `harness verify L3`).
2. **File này.**
3. Template HTML (`ui-templates/customer-v2/*.html`): bố cục, khoảng cách, cỡ chữ, màu, copy cố định.
4. `customer-redesign-v2.md`.

### 0.2 Quyết định đã chốt (không hỏi lại)

| # | Quyết định |
|---|---|
| P1 | Template thắng code hiện tại về **bố cục và copy cố định**. Dữ liệu (giá, nhãn trạng thái, mô tả gói, lý do huỷ, tên, số liệu) vẫn lấy từ server và các file logic như hiện nay (`orderStatusLabel`, `tierDescription`, `cancelReasonView`, `assignmentStatusLabel`, `categoryLabel`, `statusLabel`…). Số trong template là dữ liệu mẫu, **không** hard-code. |
| P2 | Không đổi model, service, route, polling, luật nghiệp vụ. Ngoại lệ được phép: (a) thêm tham số **tuỳ chọn** cho widget/args màn hình (`RatingScreenArgs.scheduleText`, `NordicPage.bottomBar`, `NordicPage.lightAppBar`…); (b) widget dùng chung mới trong `lib/core/widgets/`. |
| P3 | **Thanh toán:** dùng bố cục template nhưng giữ cổng thanh toán thật. `gateway == 'SEPAY'` → ảnh QR từ `payUrl ?? qrPayload` như hiện tại + thông tin chuyển khoản. `MOMO`/`FAKE` → giữ ô "Nội dung mã QR" (chưa có package QR, **không** thêm package). Chữ dưới QR: SePay "Quét bằng ứng dụng ngân hàng", MoMo "Quét bằng ứng dụng MoMo". |
| P4 | **Giữ** các phần có chức năng mà template không vẽ vì dữ liệu mẫu không cần tới: bước "Người làm" (chọn người yêu thích) trong Đặt ca khi gói Cơ bản và có người yêu thích; danh sách nhiều địa chỉ trong Đặt ca; ô ghim vị trí (GPS) trong form địa chỉ; các trạng thái loading / lỗi / trống / đã thanh toán / hết hạn / hoàn tiền. Tất cả vẽ theo ngôn ngữ template. |
| P5 | **Bỏ** các phần template không có: thẻ "Thanh toán để hệ thống bắt đầu…" ở Chi tiết đơn; dòng "Thanh toán sau khi đặt. Không phụ phí." ở Đặt ca; thanh tiến trình 4 vạch và sao điểm trên thẻ "Ca sắp tới" ở Trang chủ; dòng trạng thái làm việc trên thẻ yêu thích ở Trang chủ; pill trạng thái tài khoản + điểm uy tín và nút Đăng xuất trong màn Hồ sơ (Đăng xuất vẫn ở tab Tài khoản); hàng chip chọn góc ở Nghiệm thu (thay bằng danh sách góc xếp dọc). |
| P6 | Giữ mọi `Key` hiện có. Key nào đi theo widget sang chỗ khác (ví dụ `rate-<id>` từ "Việc khác" xuống thanh dưới) thì **giữ nguyên tên** và chỉ tồn tại ở một chỗ. Key mất vì P5 liệt kê ở §6, test tương ứng sửa theo. |
| P7 | Phía Worker không đổi giao diện, trừ những thay đổi tự nhiên của widget dùng chung (§2: `GroupedListCard`, `StepDots`, `StatusPill`). Màn dùng chung (Đánh giá, Khiếu nại) chỉ áp bố cục mới khi `role` là customer. |

### 0.3 Cách đọc template

Mở `ui-templates/customer-v2/<màn>.html` trong Chrome, DevTools → device 390×844. Mọi giá trị nằm trong `customer-v2/assets/v2.css`. Bảng quy đổi:

| CSS template | Flutter |
|---|---|
| `--primary #0F3D2E` | `NordicColors.primary` / `contrastDark` |
| `--accent #1F7A55` | `NordicColors.primaryContainer` |
| `--soft #DCEDE3` | `NordicColors.primaryFixed` |
| `--canvas #EEF1EC` | `NordicColors.canvas` |
| `--subtle #F4F6F2` | `NordicColors.surfaceSubtle` |
| `--container #E6EBE7` | `NordicColors.surfaceContainer` |
| `--title / --body / --muted` | `textTitle / textBody / textSecondary` |
| `--on-dark-muted` | `NordicColors.onDarkMuted` |
| `--amber / --cream / --on-cream` | `accentAmber / amberSoft / onAmberSoft` |
| `--border` | `NordicColors.border` |
| `--sh-card / --sh-raised / --sh-cta` | `NordicElevation.card / raised / cta` |
| `.dh` | `DarkHeader` |
| `.raised` | `RaisedPanel` |
| `.glass` | `GlassPanel` |
| `.card` | `NordicCard` (bo 20; template 22 → dùng token 20, sai số cho phép) |
| `.glist` | `GroupedListCard` (bản mới §2.2) |
| `.cta` / `.cta.split` / `.cta.sm` | `CtaButton` (§2.1) |
| `.ghost` | `CtaButton(variant: CtaVariant.outline)` |
| `.abar` | `BottomActionBar.stack` (§2.3) |
| `.appbar` (nút back trắng + tiêu đề giữa) | `NordicPage.pushed(lightAppBar: true)` (§2.4) |
| `.sec h2` | `SectionHeading` (§2.5) |
| `.codebox` | `OrderCodeBox` (§2.6) |
| `.tl` | `StepDots` (bản mới §2.7) |
| `.pill`, `.pill.ok/.warn` | `StatusPill(tone:)`; `size: PillSize.regular` (26/12) mặc định của màn customer, `compact` (24/11) trong dòng đơn |
| `.tile`, `.tile.on` | `ChoiceTile` (§2.8) |
| `.datebox` | `DateBox` (§2.9) |
| `.display` 30/700 | `NordicTypography.display` |
| `.h1` 28/700 | `NordicTypography.greeting` |
| `.h2` 19/700 | `NordicTypography.titleMedium.copyWith(fontSize: 19)` |
| `.h3` 15/600 | `NordicTypography.titleSmall` |
| `.num-xl` 44 / `.num-l` 34 / `.num-m` 26 / `.num-s` 20 | `numberLarge.copyWith(fontSize: 44)` / `numberLarge` / `numberMedium` / `numberSmall` |
| `.caps` 12/600 tracking .08em | `NordicTypography.caps.copyWith(fontSize: 12)` |
| `.small` 13 / `.xs` 12 / `.xxs` 11 | `bodyRegular.copyWith(fontSize: 13)` / `bodySmall` / `bodySmall.copyWith(fontSize: 11)` |
| Icon `.ms` (Material Symbols Outlined) | `Icons.<tên>_outlined`; đã chọn/sao/tim: bản không hậu tố |

Khoảng cách: lề ngang màn 20 (`NordicSpace.gutter`); card trong danh sách lề 16 (`NordicSpace.md`); tiêu đề mục cách trên 26, cách dưới 12.

---

## 1. Bảng lệch theo màn (đọc trước khi code)

| Màn (template) | File | Mức lệch | Việc chính |
|---|---|---|---|
| `home.html`, `home-booked.html` | `home/customer_home_tab.dart` | nhỏ | Thẻ ca sắp tới theo template; dòng phụ ô gói; `CtaButton.split`; thẻ yêu thích |
| `orders.html` | `booking/screens/order_history_screen.dart` | nhỏ | `SectionHeading`; dòng đơn dùng `DateBox`, pill compact; thẻ Sắp tới thêm vạch chia |
| `favorites.html` | `customers/widgets/favorite_worker_card.dart` | nhỏ | Nút "Đặt với …" thành `CtaButton.small`; nút tim bo 14 |
| `account.html` | `home/customer_account_tab.dart` | nhỏ | Nhãn "Tham gia" + `MM/yyyy`; Đăng xuất là card riêng có ô icon đỏ |
| `order-detail-*.html` (4 trạng thái) | `booking/screens/order_detail_screen.dart` | vừa | Khối tiền; `OrderCodeBox`; `StepDots` mới; Huỷ đơn vào "Việc khác"; thanh dưới theo bảng §3.5 |
| `booking.html` | `booking/screens/booking_screen.dart` | vừa | Tiêu đề bước đánh số; ô ca có icon; thẻ giá; một nút "Đặt ca · tổng" |
| `payment.html` | `payments/screens/payment_screen.dart` | lớn | Header tối + `RaisedPanel`; thanh dưới "Để sau, mở đơn" |
| `acceptance.html` | `workers/screens/customer_acceptance_screen.dart` | vừa | Các góc xếp dọc; "Xác nhận nghiệm thu" / "Yêu cầu dọn lại" |
| `rating.html` | `ratings/screens/rating_screen.dart` | vừa (chỉ customer) | Đầu màn có avatar + pill đếm giờ; app bar sáng |
| `disputes.html`, `dispute-create.html` | `disputes/screens/*.dart` | vừa (chỉ customer) | Card theo template; ô mã đơn + pill hạn |
| `addresses.html` | `customers/screens/address_list_screen.dart` | vừa | Một `GroupedListCard`; sửa/xoá vào bottom sheet |
| `address-form.html` | `customers/screens/address_form_screen.dart` | nhỏ | Nhãn trên ô; loại nhà dạng segmented; thẻ diện tích |
| `profile.html` | `customers/screens/customer_profile_screen.dart` | nhỏ | Avatar giữa, 3 ô, số điện thoại khoá + gợi ý; bỏ P5 |

---

## 2. Widget dùng chung (làm trước, một commit)

### 2.1 `CtaButton` — mới, `lib/core/widgets/cta_button.dart`

Template: `.cta` cao 56, bo 18, nền `primary`, chữ 16/600, bóng `cta`; `.cta.split` chữ trái + ô mũi tên 40×40 bo 13 bên phải; `.cta.sm` cao 44, bo 14, chữ 14; `.ghost` cao 48, bo 16, viền `border`, nền trắng, chữ `primary` 600.

```dart
import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_elevation.dart';
import 'pressable.dart';

enum CtaVariant { primary, outline }

/// The main button of a customer screen (ui-templates v2 `.cta`, `.cta.split`, `.cta.sm`, `.ghost`).
class CtaButton extends StatelessWidget {
  final String label;
  final VoidCallback? onPressed;
  final IconData? icon;
  final CtaVariant variant;
  final bool split;
  final bool small;
  final bool isLoading;
  final Key? labelKey; // gán cho Text nhãn (Đặt ca dùng 'booking-total')

  const CtaButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.icon,
    this.variant = CtaVariant.primary,
    this.split = false,
    this.small = false,
    this.isLoading = false,
    this.labelKey,
  });

  bool get _enabled => onPressed != null && !isLoading;

  @override
  Widget build(BuildContext context) {
    final primary = variant == CtaVariant.primary;
    final height = small ? 44.0 : (primary ? 56.0 : 48.0);
    final radius = BorderRadius.circular(small ? 14 : (primary ? 18 : 16));
    final fg = primary
        ? NordicColors.textInverse
        : NordicColors.primary;
    final bg = !_enabled && primary
        ? NordicColors.surfaceContainer
        : (primary ? NordicColors.primary : NordicColors.surface);
    final text = Text(
      label,
      key: labelKey,
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
      style: TextStyle(
        fontSize: small ? 14 : 16,
        fontWeight: FontWeight.w600,
        color: _enabled || !primary ? fg : NordicColors.textSecondary,
      ),
    );
    final Widget content;
    if (isLoading) {
      content = SizedBox(
        width: 20,
        height: 20,
        child: CircularProgressIndicator(strokeWidth: 2, color: fg),
      );
    } else if (split) {
      content = Row(
        children: [
          Expanded(child: text),
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: NordicColors.textInverse.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(13),
            ),
            child: const Icon(Icons.arrow_forward, size: 20, color: NordicColors.textInverse),
          ),
        ],
      );
    } else {
      content = Row(
        mainAxisSize: MainAxisSize.min,
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          if (icon != null) ...[Icon(icon, size: 20, color: fg), const SizedBox(width: 10)],
          Flexible(child: text),
        ],
      );
    }
    final button = Material(
      color: bg,
      shape: RoundedRectangleBorder(
        borderRadius: radius,
        side: primary ? BorderSide.none : const BorderSide(color: NordicColors.border),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: _enabled ? onPressed : null,
        child: SizedBox(
          height: height,
          child: Padding(
            padding: split
                ? const EdgeInsets.fromLTRB(20, 0, 8, 0)
                : EdgeInsets.symmetric(horizontal: small ? 16 : 20),
            child: Center(child: content),
          ),
        ),
      ),
    );
    return Semantics(
      button: true,
      enabled: _enabled,
      label: label,
      child: ExcludeSemantics(
        child: Pressable(
          enabled: _enabled,
          child: DecoratedBox(
            decoration: BoxDecoration(
              borderRadius: radius,
              boxShadow: primary && _enabled ? NordicElevation.cta : null,
            ),
            child: button,
          ),
        ),
      ),
    );
  }
}
```

Test có `find.widgetWithText(NordicButton, …)` cho nút đã đổi sang `CtaButton` thì đổi thành `find.byKey(...)` (key không đổi) hoặc `find.widgetWithText(CtaButton, …)`.

### 2.2 `GroupedListCard` — sửa `lib/core/widgets/grouped_list_card.dart`

Template `.glist`: card trắng bo 20, bóng `card`, **không viền**; mỗi dòng ≥ 64 px, padding 12/16; icon trong ô 40×40 bo 12 nền `surfaceSubtle`, icon màu `primary`; tiêu đề 15/600; dòng phụ 12 `textSecondary` cách 4; mũi tên `textSecondary`; vạch chia lùi vào 70 px, màu `surfaceContainer`. Dòng `danger`: ô icon nền `errorContainer`, icon + chữ `error`, không mũi tên. API giữ nguyên.

```dart
class GroupedListCard extends StatelessWidget {
  final List<GroupedListItem> items;

  const GroupedListCard({super.key, required this.items});

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: const BoxDecoration(
        borderRadius: NordicRadius.cardAll,
        boxShadow: NordicElevation.card,
      ),
      child: Material(
        color: NordicColors.surface,
        clipBehavior: Clip.antiAlias,
        shape: const RoundedRectangleBorder(borderRadius: NordicRadius.cardAll),
        child: Column(
          children: [
            for (var i = 0; i < items.length; i++) ...[
              if (i > 0)
                const Padding(
                  padding: EdgeInsets.only(left: 70, right: 16),
                  child: Divider(height: 1, thickness: 1, color: NordicColors.surfaceContainer),
                ),
              _row(items[i]),
            ],
          ],
        ),
      ),
    );
  }

  Widget _row(GroupedListItem item) {
    final color = item.danger ? NordicColors.error : NordicColors.textTitle;
    return InkWell(
      key: item.key,
      onTap: item.onTap,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 64),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          child: Row(
            children: [
              Container(
                width: 40,
                height: 40,
                decoration: BoxDecoration(
                  color: item.danger ? NordicColors.errorContainer : NordicColors.surfaceSubtle,
                  borderRadius: NordicRadius.tileAll,
                ),
                child: Icon(
                  item.icon,
                  size: 22,
                  color: item.danger ? NordicColors.error : NordicColors.primary,
                ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(item.title, style: NordicTypography.titleSmall.copyWith(color: color)),
                    if (item.subtitle != null) ...[
                      const SizedBox(height: 4),
                      Text(item.subtitle!, style: NordicTypography.bodySmall),
                    ],
                  ],
                ),
              ),
              if (item.trailing != null)
                Padding(
                  padding: const EdgeInsets.only(left: 8),
                  child: Text(item.trailing!, style: NordicTypography.bodySmall),
                ),
              if (!item.danger)
                const Icon(Icons.chevron_right, color: NordicColors.textSecondary),
            ],
          ),
        ),
      ),
    );
  }
}
```

### 2.3 `BottomActionBar.stack` — thêm vào `lib/core/widgets/bottom_action_bar.dart`

Template `.abar`: nền trắng, viền trên `border`, padding 12/20/(safe area), xếp dọc: dòng gợi ý 12 px căn giữa (tuỳ chọn) → nút chính → nút phụ, cách nhau 8. Constructor cũ giữ nguyên cho Worker.

```dart
class BottomActionBar extends StatelessWidget {
  final List<Widget> children;
  final String? hint;
  final bool _stacked;

  const BottomActionBar({super.key, required this.children})
      : hint = null,
        _stacked = false;

  /// Customer v2 `.abar`: white, hairline on top, an optional hint, then the buttons stacked.
  const BottomActionBar.stack({super.key, required this.children, this.hint})
      : _stacked = true;

  @override
  Widget build(BuildContext context) {
    if (!_stacked) return _row(); // the existing build, unchanged
    return DecoratedBox(
      decoration: const BoxDecoration(
        color: NordicColors.surface,
        border: Border(top: BorderSide(color: NordicColors.border)),
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 12),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (hint != null) ...[
                Text(hint!, textAlign: TextAlign.center, style: NordicTypography.bodySmall),
                const SizedBox(height: 8),
              ],
              for (var i = 0; i < children.length; i++) ...[
                if (i > 0) const SizedBox(height: 8),
                children[i],
              ],
            ],
          ),
        ),
      ),
    );
  }

  Widget _row() => /* nội dung build hiện tại */;
}
```

### 2.4 `NordicPage` — thêm `lightAppBar`, `closeButton`, `bottomBar`

Template `.appbar`: nền `canvas`, nút trái 44×44 nền trắng bo 14 bóng `card` (icon `arrow_back`, hoặc `close` cho màn mở dạng "làm một việc rồi đóng": Đặt ca, Đánh giá, Tạo khiếu nại), tiêu đề 17/600 căn giữa, bên phải chừa 44.

Thêm vào **cả hai** constructor: `final Widget? bottomBar;` (nếu có thì thay cho `BottomActionBar(children: actions)`); chỉ `pushed`: `final bool lightAppBar; final bool closeButton;` (mặc định `false`, Worker không đổi).

```dart
// trong build(), nhánh pushed:
final bar = bottomBar ?? (hasActions ? BottomActionBar(children: actions!) : null);
final body = Column(children: [Expanded(child: list), ?bar]);
if (!large && lightAppBar) {
  return Scaffold(
    backgroundColor: NordicColors.canvas,
    body: SafeArea(
      bottom: false,
      child: Column(
        children: [
          _LightAppBar(title: title, titleKey: titleKey, close: closeButton),
          Expanded(child: body),
        ],
      ),
    ),
  );
}

class _LightAppBar extends StatelessWidget {
  final String title;
  final Key? titleKey;
  final bool close;
  const _LightAppBar({required this.title, this.titleKey, this.close = false});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
      child: Row(
        children: [
          Semantics(
            button: true,
            label: close ? 'Đóng' : 'Quay lại',
            child: Pressable(
              child: DecoratedBox(
                decoration: const BoxDecoration(
                  borderRadius: BorderRadius.all(Radius.circular(14)),
                  boxShadow: NordicElevation.card,
                ),
                child: Material(
                  color: NordicColors.surface,
                  shape: const RoundedRectangleBorder(
                    borderRadius: BorderRadius.all(Radius.circular(14)),
                  ),
                  clipBehavior: Clip.antiAlias,
                  child: InkWell(
                    onTap: () => Navigator.of(context).maybePop(),
                    child: SizedBox(
                      width: 44,
                      height: 44,
                      child: ExcludeSemantics(
                        child: Icon(
                          close ? Icons.close : Icons.arrow_back,
                          size: 22,
                          color: NordicColors.textTitle,
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
          Expanded(
            child: Text(title, key: titleKey, textAlign: TextAlign.center, style: NordicTypography.appBarTitle),
          ),
          const SizedBox(width: 44),
        ],
      ),
    );
  }
}
```

Mọi màn pushed của customer trong file này dùng `lightAppBar: true`.

### 2.5 `SectionHeading` — mới, `lib/core/widgets/section_heading.dart`

Template `.sec`: tiêu đề 17/700, bên phải là chữ phụ 13 `textSecondary` hoặc link 14/600 `primary` có mũi tên; padding trên 26, dưới 12. `SectionTitle` giữ cho Worker.

```dart
class SectionHeading extends StatelessWidget {
  final String title;
  final String? trailing;
  final VoidCallback? onAction;
  final Key? trailingKey;

  const SectionHeading(this.title, {super.key, this.trailing, this.onAction, this.trailingKey});

  @override
  Widget build(BuildContext context) {
    final style = NordicTypography.titleMedium.copyWith(fontSize: 17, letterSpacing: -0.17);
    Widget? right;
    if (trailing != null) {
      right = onAction == null
          ? Text(trailing!, key: trailingKey, style: NordicTypography.bodyRegular.copyWith(fontSize: 13, color: NordicColors.textSecondary))
          : InkWell(
              key: trailingKey,
              onTap: onAction,
              child: ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 44),
                child: Row(mainAxisSize: MainAxisSize.min, children: [
                  Text(trailing!, style: NordicTypography.labelMedium.copyWith(fontWeight: FontWeight.w600, color: NordicColors.primary)),
                  const Icon(Icons.chevron_right, size: 18, color: NordicColors.primary),
                ]),
              ),
            );
    }
    return Padding(
      padding: const EdgeInsets.only(top: 26, bottom: 12),
      child: Row(children: [Expanded(child: Text(title, style: style)), ?right]),
    );
  }
}
```

### 2.6 `OrderCodeBox` — mới, `lib/core/widgets/order_code_box.dart`

Template `.codebox`: nền `surfaceSubtle` bo 14, padding 6/6/6/14; trái: "Mã đơn" 11 `textSecondary` trên, mã 12/600 dưới; phải: nút 44×36 nền trắng bo 10 bóng `card`, icon `content_copy` 18 `primary`.

```dart
class OrderCodeBox extends StatelessWidget {
  final String code;
  final VoidCallback onCopy;
  final Key? codeKey;
  final Key? copyKey;
  final Widget? trailing; // dispute-create: pill "còn 21 giờ" thay cho nút sao chép

  const OrderCodeBox({super.key, required this.code, required this.onCopy, this.codeKey, this.copyKey, this.trailing});

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(14, 6, 6, 6),
      decoration: const BoxDecoration(color: NordicColors.surfaceSubtle, borderRadius: NordicRadius.fieldAll),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Mã đơn', style: NordicTypography.bodySmall.copyWith(fontSize: 11)),
                Text(code, key: codeKey, style: NordicTypography.bodySmall.copyWith(
                  fontWeight: FontWeight.w600, color: NordicColors.textTitle,
                  fontFeatures: const [FontFeature.tabularFigures()],
                )),
              ],
            ),
          ),
          trailing ??
              Semantics(
                button: true,
                label: 'Sao chép mã đơn',
                child: DecoratedBox(
                  decoration: const BoxDecoration(borderRadius: BorderRadius.all(Radius.circular(10)), boxShadow: NordicElevation.card),
                  child: Material(
                    color: NordicColors.surface,
                    shape: const RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(10))),
                    clipBehavior: Clip.antiAlias,
                    child: InkWell(
                      key: copyKey,
                      onTap: onCopy,
                      child: const SizedBox(
                        width: 44,
                        height: 36,
                        child: ExcludeSemantics(child: Icon(Icons.content_copy_outlined, size: 18, color: NordicColors.primary)),
                      ),
                    ),
                  ),
                ),
              ),
        ],
      ),
    );
  }
}
```

`order-code` giờ chỉ chứa mã (không còn chữ "Mã đơn " phía trước). Test nào so `find.text('Mã đơn GV…')` sửa thành `find.text('GV…')`.

### 2.7 `StepDots` — đổi giao diện, giữ API (`StepItem`, `DotState`)

Template `.tl`: mỗi bước chiếm phần bằng nhau; chấm 28 px; đường nối 2 px giữa hai chấm (nửa trái của bước i nối với bước i−1); nhãn 12/600 dưới chấm cách 8, căn giữa.
- `done`: chấm nền `primaryContainer`, dấu `check` trắng 16, nhãn `textTitle`.
- `current`: chấm trắng viền 2 px `primaryContainer`, nhãn `primary`.
- `todo`: chấm trắng viền 2 px `border`, nhãn `textSecondary`.
- Đường nối vào bước i có màu `primaryContainer` khi bước i là `done` hoặc `current`, ngược lại `border`.

```dart
@override
Widget build(BuildContext context) {
  return Row(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      for (var i = 0; i < steps.length; i++)
        Expanded(
          child: Column(
            children: [
              SizedBox(
                height: 28,
                child: Row(
                  children: [
                    Expanded(child: _line(i == 0 ? null : steps[i].state != DotState.todo)),
                    _dot(steps[i].state),
                    Expanded(child: _line(i == steps.length - 1 ? null : steps[i + 1].state != DotState.todo)),
                  ],
                ),
              ),
              const SizedBox(height: 8),
              Text(
                steps[i].label,
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                  color: switch (steps[i].state) {
                    DotState.done => NordicColors.textTitle,
                    DotState.current => NordicColors.primary,
                    DotState.todo => NordicColors.textSecondary,
                  },
                ),
              ),
            ],
          ),
        ),
    ],
  );
}

/// null = no line (first/last half), true = reached, false = not yet.
Widget _line(bool? reached) => reached == null
    ? const SizedBox.shrink()
    : Container(height: 2, color: reached ? NordicColors.primaryContainer : NordicColors.border);

Widget _dot(DotState s) => Container(
  width: 28,
  height: 28,
  decoration: BoxDecoration(
    shape: BoxShape.circle,
    color: s == DotState.done ? NordicColors.primaryContainer : NordicColors.surface,
    border: s == DotState.done
        ? null
        : Border.all(color: s == DotState.current ? NordicColors.primaryContainer : NordicColors.border, width: 2),
  ),
  child: s == DotState.done ? const Icon(Icons.check, size: 16, color: NordicColors.textInverse) : null,
);
```

### 2.8 `ChoiceTile` — mới, `lib/core/widgets/choice_tile.dart`

Template `.tile`: nền trắng, viền 1 `border`, bo 18, padding 14; khi chọn: viền 2 `primaryContainer`, nền `#F1F8F4` (dùng `NordicColors.surfaceSubtle`), padding 13, dấu tích 22×22 nền `primaryContainer` góc phải trên (cách 10); `unavailable`: mờ 0.45, không bấm được.

```dart
class ChoiceTile extends StatelessWidget {
  final bool selected;
  final bool unavailable;
  final VoidCallback onTap;
  final Widget child;
  final double? minHeight;

  const ChoiceTile({super.key, required this.selected, required this.onTap, required this.child, this.unavailable = false, this.minHeight});

  @override
  Widget build(BuildContext context) {
    final tile = Material(
      color: selected ? NordicColors.surfaceSubtle : NordicColors.surface,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(18),
        side: BorderSide(color: selected ? NordicColors.primaryContainer : NordicColors.border, width: selected ? 2 : 1),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: unavailable ? null : onTap,
        child: ConstrainedBox(
          constraints: BoxConstraints(minHeight: minHeight ?? 0),
          child: Stack(
            children: [
              Padding(padding: EdgeInsets.all(selected ? 13 : 14), child: child),
              if (selected)
                Positioned(
                  top: 10,
                  right: 10,
                  child: Container(
                    width: 22,
                    height: 22,
                    decoration: const BoxDecoration(color: NordicColors.primaryContainer, shape: BoxShape.circle),
                    child: const Icon(Icons.check, size: 15, color: NordicColors.textInverse),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
    return Semantics(
      selected: selected,
      button: true,
      enabled: !unavailable,
      child: Opacity(opacity: unavailable ? 0.45 : 1, child: Pressable(enabled: !unavailable, child: tile)),
    );
  }
}
```

Ô gói ở Trang chủ (`_tierTile`) và các ô ở Đặt ca (`_TierTile`, `_ChoiceCard`, `_ShiftTile`) chuyển sang `ChoiceTile` (giữ key trên `ChoiceTile`).

### 2.9 `DateBox` — mới, `lib/core/widgets/date_box.dart`

Template `.datebox`: 48×52, bo 14, nền `primaryFixed` chữ `primary`; `muted` (đơn huỷ): nền `surfaceSubtle`, chữ `textSecondary`. Số ngày 18/700, thứ 11/600 cách 3.

```dart
class DateBox extends StatelessWidget {
  final String day;
  final String weekday;
  final bool muted;
  const DateBox({super.key, required this.day, required this.weekday, this.muted = false});

  @override
  Widget build(BuildContext context) {
    final fg = muted ? NordicColors.textSecondary : NordicColors.primary;
    return Container(
      width: 48,
      height: 52,
      decoration: BoxDecoration(
        color: muted ? NordicColors.surfaceSubtle : NordicColors.primaryFixed,
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Text(day, style: TextStyle(fontSize: 18, height: 1, fontWeight: FontWeight.w700, color: fg)),
          const SizedBox(height: 3),
          Text(weekday, style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: fg)),
        ],
      ),
    );
  }
}
```

### 2.10 `StatusPill` — thêm `size`

```dart
enum PillSize { compact, regular }
// field: final PillSize size;  constructor: this.size = PillSize.compact
// build: height: size == PillSize.regular ? 26 : 24; fontSize: size == PillSize.regular ? 12 : 11
```
`OrderStatusPill` thêm tham số `size` truyền xuống. Màn customer dùng `regular`, trừ dòng đơn trong tab Đơn (`compact`). Mặc định `compact` để Worker không đổi.

Trên nền tối, `PillTone.neutral` giữ `surfaceContainer` + `textBody` (spec v2 §2.3): sửa tone neutral của `StatusPill` từ `textTitle` sang `textBody`.

---

## 3. Từng màn

Mỗi mục: **Lệch** (code hiện tại so với template) → **Làm** → code tham khảo khi cần. Key không nhắc tới thì giữ nguyên chỗ cũ.

### 3.1 Trang chủ — `customer_home_tab.dart` (`home.html`, `home-booked.html`)

**Lệch**
1. Thẻ "Ca sắp tới" (`_nextCard`): có thanh 4 vạch tiến trình và sao điểm; dòng lớn là giờ (`numberMedium`) + "đến …"; template là dòng "Thứ Bảy, 31/10 · 08:00" 20/700, dòng phụ "Ca sáng · Cơ bản · đến 12:00" màu `#D3E3DA`, hàng người làm có nút trắng "Chi tiết".
2. Ô gói: dòng cuối "mỗi người, mỗi ca · cần N người"; template "mỗi người · mỗi ca".
3. Nút "Đặt ca · giá" là `NordicButton` pill; template `.cta.split`.
4. Thẻ yêu thích: có dòng `statusDisplayName`; rộng 164; template rộng 152, không dòng trạng thái, nút "Đặt lại" `.ghost.sm`.

**Làm**
- (1) Thay thân `_nextCard` (giữ `Pressable` + `InkWell(key: home-next-order)` bọc ngoài, cả thẻ vẫn bấm được):

```dart
Column(
  crossAxisAlignment: CrossAxisAlignment.start,
  children: [
    Row(children: [
      Expanded(child: Text('CA SẮP TỚI', style: NordicTypography.caps.copyWith(fontSize: 12, color: NordicColors.onDarkMuted))),
      OrderStatusPill(order.orderStatus, size: PillSize.regular),
    ]),
    const SizedBox(height: NordicSpace.sm),
    Text(
      // "Thứ Bảy, 31/10 · 08:00": _dayLabel là hàm đang dựng "Thứ Sáu, 30/10" cho header (_today); tách ra dùng chung.
      [_dayLabel(order.scheduledDate), ?next.startLocal].join(' · '),
      style: NordicTypography.numberSmall.copyWith(color: NordicColors.textInverse, letterSpacing: -0.2),
    ),
    const SizedBox(height: 2),
    Text(
      [shiftLabel(order.shiftCode), tierLabel(order.serviceTier), if (next.endLocal != null) 'đến ${next.endLocal}'].join(' · '),
      style: NordicTypography.bodyRegular.copyWith(fontSize: 13, color: NordicColors.onDarkMuted),
    ),
    if (worker != null) ...[
      const SizedBox(height: NordicSpace.md),
      Row(children: [
        AvatarInitials(workerName(worker), size: 40, background: NordicColors.amberSoft, foreground: NordicColors.onAmberSoft),
        const SizedBox(width: NordicSpace.sm),
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(workerName(worker), style: NordicTypography.titleSmall.copyWith(color: NordicColors.textInverse)),
          Text('Người giúp việc của bạn', style: NordicTypography.bodySmall.copyWith(color: NordicColors.onDarkMuted)),
        ])),
        // Nút trắng "Chi tiết": cùng hành động với cả thẻ.
        Material(
          color: NordicColors.surface,
          shape: const RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(14))),
          child: InkWell(
            key: const Key('home-next-detail'),
            onTap: () => _openOrder(order.orderId),
            borderRadius: const BorderRadius.all(Radius.circular(14)),
            child: SizedBox(height: 44, child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: Center(child: Text('Chi tiết', style: NordicTypography.labelMedium.copyWith(fontWeight: FontWeight.w600, color: NordicColors.primary))),
            )),
          ),
        ),
      ]),
    ],
  ],
)
```
  Bỏ thanh 4 vạch và sao (P5). `steps` không còn dùng thì xoá biến.
- (2) Dòng cuối ô gói: `quote.requiredWorkers > 1 ? 'mỗi người · mỗi ca · cần ${quote.requiredWorkers} người' : 'mỗi người · mỗi ca'`. Ô gói chuyển sang `ChoiceTile(minHeight: 150)`; mô tả vẫn là `tierDescription(...)`.
- (3) `home-book` → `CtaButton(key: const Key('home-book'), label: …, split: true, onPressed: _openBooking)`.
- (4) `_favoriteCard`: `SizedBox(width: 152)`; bỏ `Text(w.statusDisplayName)`; nút → `CtaButton(key: Key('home-book-again-${w.workerId}'), label: 'Đặt lại', variant: CtaVariant.outline, small: true, onPressed: …)`; chiều cao list ngang giảm từ 244 xuống 214 (+80 × (textScale − 1) như cũ).
- Tiêu đề "Người giúp việc yêu thích" dùng `SectionHeading(trailing: 'Tất cả', onAction: widget.onShowFavorites)` thay `SectionTitle`, bọc padding ngang 20.

### 3.2 Tab Đơn — `order_history_screen.dart` (`orders.html`)

**Lệch**: tiêu đề mục "Sắp tới" và "Tháng …" dùng `titleSmall` 15/600 (template 17/700); ô ngày tự dựng (template `DateBox` 48×52); dòng meta `bodyRegular` 14 (template 12 `textSecondary`); pill thường (template compact); thẻ Sắp tới thiếu vạch chia trước dòng gói; danh sách tháng là `NordicCard` có viền (template `.glist`, không viền); chip lọc cao 44, template 38 (giữ 44 vì vùng chạm ≥ 44, **không đổi**).

**Làm**
- "Sắp tới" → `SectionHeading('Sắp tới')`; tháng → `SectionHeading('Tháng ${group.month}/${group.year}', trailing: '${group.orders.length} đơn')`. Bỏ padding/Text cũ.
- Card nhóm tháng: thay `NordicCard(padding: zero)` bằng:

```dart
DecoratedBox(
  decoration: const BoxDecoration(borderRadius: NordicRadius.cardAll, boxShadow: NordicElevation.card),
  child: Material(
    color: NordicColors.surface,
    shape: const RoundedRectangleBorder(borderRadius: NordicRadius.cardAll),
    clipBehavior: Clip.antiAlias,
    child: Column(children: [
      for (var i = 0; i < group.orders.length; i++) ...[
        if (i > 0) const Padding(
          padding: EdgeInsets.only(left: 76, right: 16),
          child: Divider(height: 1, thickness: 1, color: NordicColors.surfaceContainer),
        ),
        _row(group.orders[i]),
      ],
    ]),
  ),
)
```
- `_row`: padding `EdgeInsets.fromLTRB(16, 12, 12, 12)`, minHeight 76; ô ngày → `DateBox(day: …, weekday: _weekdays[…], muted: cancelled)`; khoảng cách 12; tiêu đề giữ; meta → `NordicTypography.bodySmall.copyWith(fontFeatures: const [FontFeature.tabularFigures()])` một dòng (`maxLines: 1, softWrap: false`); dòng "Cần thanh toán / Cần bạn chọn lịch" giữ (màu `primary`, 12/600); dòng mã 11 giữ; pill → `OrderStatusPill(order.orderStatus, textKey: …, size: PillSize.compact)`.
- Thẻ Sắp tới: thêm trước dòng `'${tierLabel} · N người'` một vạch `Container(height: 1, color: NordicColors.textInverse.withValues(alpha: .14))` cách trên 16, dưới 14; caps 11; pill `regular`. Không gọi thêm API (giữ như spec v2).
- Danh sách tháng và thẻ Sắp tới không còn lề trong `NordicPage` riêng: giữ padding 20 của `NordicPage.tab` (template lề 16 cho card; sai số 4 px chấp nhận).

### 3.3 Yêu thích — `favorite_worker_card.dart`, `favorite_workers_screen.dart` (`favorites.html`)

**Lệch**: nút tim là `IconButton` tròn (template 44×44 bo 14); nút "Đặt với …" là `NordicButton` pill (template `.cta.sm`); vạch chia `Divider(height: 28)` đúng; ghi chú cuối danh sách đã đúng.

**Làm**
- Tim:

```dart
Semantics(
  button: true,
  label: 'Bỏ yêu thích ${worker.fullName}',
  child: Material(
    color: NordicColors.primaryFixed,
    shape: const RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(14))),
    child: InkWell(
      key: Key('favorite-remove-${worker.workerId}'),
      onTap: onRemove,
      borderRadius: const BorderRadius.all(Radius.circular(14)),
      child: const SizedBox(width: 44, height: 44, child: ExcludeSemantics(child: Icon(Icons.favorite, color: NordicColors.primary))),
    ),
  ),
)
```
  Nếu test đang tìm `find.byTooltip('Bỏ yêu thích')` thì đổi sang key mới `favorite-remove-<id>` (key **mới**, thêm vào danh sách §6).
- Nút đặt: `CtaButton(label: 'Đặt với $short', small: true, onPressed: onBookJob)`.
- Tên: 16/600 (`titleSmall.copyWith(fontSize: 16)`).
- Pill "Đang làm việc" (khi `busy`) giữ: đây là dữ liệu thật, template chỉ không có ca mẫu bận.

### 3.4 Tài khoản — `customer_account_tab.dart` (`account.html`)

**Lệch**: nhãn cột 3 "Ngày tham gia" + `formattedCreatedAt`; template "Tham gia" + `MM/yyyy`. Số cột cỡ 26 (template 24). Tiêu đề nhóm dùng `SectionTitle`. Dòng "Địa chỉ của tôi" chưa có dòng phụ mặc định; "Người giúp việc yêu thích" chưa có "N người". Đăng xuất là một `GroupedListCard` danger (đúng ý, giờ tự có ô icon đỏ nhờ §2.2).

**Làm**
- Cột 3: `_count(c == null ? '—' : '${c.month.toString().padLeft(2, '0')}/${c.year}', 'Tham gia')` với `c = profile?.createdAt`.
- `_count` dùng `NordicTypography.numberMedium.copyWith(fontSize: 24)`.
- `SectionTitle('Đặt ca')`, `SectionTitle('Hỗ trợ')` → `SectionHeading(...)`.
- Dòng phụ "Địa chỉ của tôi": `'Mặc định: ${a.addressLine} · ${formatArea(a.totalAreaM2)}'` nếu màn đã có địa chỉ mặc định trong dữ liệu đang tải (đang đếm `_addressCount` từ danh sách địa chỉ → lấy phần tử `isDefault`); không có thì bỏ dòng phụ. Không gọi thêm API.
- Dòng phụ "Người giúp việc yêu thích": `'$n người'` chỉ khi màn đã có số này; không có thì bỏ.
- Đăng xuất cách nhóm Hỗ trợ 16 px.

### 3.5 Chi tiết đơn — `order_detail_screen.dart` (`order-detail-pending/assigned/done/cancelled.html`)

**Lệch**
1. Khối tiền: "Tổng tiền" + pill nằm cột trái, số tiền bên phải. Template: hàng 1 "Tổng tiền" (13 `textSecondary`) trái + pill "Đã thanh toán" phải; hàng 2 số tiền `numberMedium`; vạch chia; lưới 3 cột có vạch dọc.
2. Ô mã đơn tự dựng → `OrderCodeBox` (cách lưới 16).
3. Lưới 3 cột không có vạch dọc; cột 3 ghi "1 người", template ghi "1".
4. Thẻ "Thanh toán để hệ thống bắt đầu…" khi chờ thanh toán (P5: bỏ).
5. Khi `PENDING_PAYMENT` code vẫn hiện mục "Người làm" với câu `no-assignments`; template không có mục này.
6. "Huỷ đơn" là `_cancelSection()` riêng phía dưới; template là dòng đỏ cuối "Việc khác".
7. Thanh dưới chỉ có ở chờ thanh toán / đã huỷ; template có nút chính cho đánh giá, nghiệm thu, làm thêm.
8. Tiêu đề mục `SectionTitle` → `SectionHeading`. Tiến trình dùng `StepDots` mới (§2.7) nằm trong `NordicCard`.

**Làm**
- (1)(3) Thay phần `if (!cancelled) ...[Row…]` và `Row(cell…)` trong `_facts`:

```dart
if (!cancelled) ...[
  Row(children: [
    Expanded(child: Text('Tổng tiền', style: NordicTypography.bodyRegular.copyWith(fontSize: 13, color: NordicColors.textSecondary))),
    if (_paidStatuses.contains(order.orderStatus))
      const StatusPill('Đã thanh toán', key: Key('order-paid-pill'), tone: PillTone.ok, size: PillSize.regular),
  ]),
  const SizedBox(height: NordicSpace.xxs),
  amount, // Text(key: order-amount, numberMedium)
  const Padding(padding: EdgeInsets.symmetric(vertical: 14), child: Divider(height: 1, thickness: 1, color: NordicColors.surfaceContainer)),
],
IntrinsicHeight(
  child: Row(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    cell('Gói', text(tierLabel(order.serviceTier))),
    const VerticalDivider(width: 21, thickness: 1, color: NordicColors.surfaceContainer),
    cell('Diện tích', text(formatArea(order.areaSnapshotM2))),
    const VerticalDivider(width: 21, thickness: 1, color: NordicColors.surfaceContainer),
    cell(cancelled ? 'Số tiền' : 'Người làm', cancelled ? amount : text('${order.requiredWorkers}')),
  ]),
),
```
  `cell` đổi nhãn sang `bodySmall` (12) và giá trị `titleSmall` cách 4 (đã đúng).
  Đơn huỷ: khối kem đứng đầu đã đúng; bo 16 (`BorderRadius.circular(16)`), padding 14.
- (2) `OrderCodeBox(code: order.orderCode, codeKey: const Key('order-code'), copyKey: const Key('order-code-copy'), onCopy: () => _copyCode(order.orderCode))`, đặt sau `SizedBox(height: 16)`.
- (4) Xoá khối `if (canPay(order.orderStatus)) ...[NordicCard(...)]` trong `_sections`.
- (5) Điều kiện hiện mục "Người làm" thành: `!(cancelled && progress.assignments.isEmpty) && order.orderStatus != OrderStatus.pendingPayment`.
- (6) Bỏ `_cancelSection()` dạng nút riêng. Thêm vào **cuối** `_actions(...)`:

```dart
if (canCancel(order.orderStatus) && !_cancelOpen)
  GroupedListItem(
    key: const Key('open-cancel'),
    icon: Icons.event_busy_outlined,
    title: 'Huỷ đơn',
    danger: true,
    onTap: () => setState(() {
      _cancelOpen = true;
      _cancelError = null;
    }),
  ),
```
  (`_cancelOpen` là cờ đã có, dòng 103; `_cancelSection()` giờ chỉ còn trả về form khi `_cancelOpen`.) Form huỷ (`cancel-form`, `cancel-reason-input`, `submit-cancel`, `cancel-error`) vẫn hiện ngay dưới card "Việc khác" khi mở, đổi nút sang `CtaButton` (Xác nhận huỷ: `CtaButton`, Không huỷ nữa: `CtaVariant.outline`).
- (7) Thay `_bar(order)` bằng bảng dưới; đúng **một** nút chính. Hành động nào đã lên thanh dưới thì **không** còn dòng trong "Việc khác" (key là duy nhất).

| Điều kiện (xét theo thứ tự) | `hint` | Nút chính (key) | Nút phụ |
|---|---|---|---|
| `canPay(status)` | "Mã QR hết hạn sau 15 phút." | `CtaButton(icon: Icons.qr_code_2, label: 'Thanh toán · ${formatVnd(total)}')` (`pay-order`) | — |
| `status == CANCELLED` | — | "Chọn lịch khác", icon `calendar_month` (`order-rebook`) | outline "Về danh sách đơn" (`order-back-to-list`) |
| có phân công `awaitingAcceptance` (lấy người đầu tiên `a`) | — | "Xác nhận nghiệm thu", icon `task_alt` (`accept-${a.assignmentId}`) → mở `customerAcceptance` | outline "Xem ảnh" (`accept-photos-${a.assignmentId}`, cùng đích) |
| `assignmentActions(a)` có `rate` (người đầu tiên) | "Bạn có 48 giờ sau khi xong ca để đánh giá." | "Đánh giá người làm", icon `star` (`rate-${a.assignmentId}`) | — |
| `canExtend(progress) && status == ASSIGNED && !_extOpen` | — | "Làm lần 2", icon `more_time` (`open-extension`) | — |
| còn lại | không có thanh dưới | | |

```dart
Widget? _bar(BookingOrder order, OrderProgress progress) {
  if (canPay(order.orderStatus)) {
    return BottomActionBar.stack(
      hint: 'Mã QR hết hạn sau 15 phút.',
      children: [
        CtaButton(key: const Key('pay-order'), icon: Icons.qr_code_2, label: 'Thanh toán · ${formatVnd(order.totalAmount)}', onPressed: _pay),
      ],
    );
  }
  if (order.orderStatus == OrderStatus.cancelled) {
    return BottomActionBar.stack(children: [
      CtaButton(key: const Key('order-rebook'), icon: Icons.calendar_month_outlined, label: 'Chọn lịch khác',
          onPressed: () => Navigator.of(context).pushNamed(BookingRoutes.booking)),
      CtaButton(key: const Key('order-back-to-list'), variant: CtaVariant.outline, label: 'Về danh sách đơn',
          onPressed: () => Navigator.of(context).maybePop()),
    ]);
  }
  final accept = progress.assignments.where((a) => assignmentActions(a.assignmentStatus).contains(AssignmentAction.reviewAndAccept)).firstOrNull;
  if (accept != null) {
    void open() => _open(WorkerRoutes.customerAcceptance, CustomerAcceptanceArgs(assignmentId: accept.assignmentId));
    return BottomActionBar.stack(children: [
      CtaButton(key: Key('accept-${accept.assignmentId}'), icon: Icons.task_alt, label: 'Xác nhận nghiệm thu', onPressed: open),
      CtaButton(key: Key('accept-photos-${accept.assignmentId}'), variant: CtaVariant.outline, label: 'Xem ảnh', onPressed: open),
    ]);
  }
  final rate = progress.assignments.where((a) => assignmentActions(a.assignmentStatus).contains(AssignmentAction.rate)).firstOrNull;
  if (rate != null) {
    return BottomActionBar.stack(
      hint: 'Bạn có 48 giờ sau khi xong ca để đánh giá.',
      children: [
        CtaButton(key: Key('rate-${rate.assignmentId}'), icon: Icons.star_outline, label: 'Đánh giá người làm',
            onPressed: () => _open(RatingRoutes.ratings, RatingScreenArgs(
              role: RatingRole.customer,
              assignmentId: rate.assignmentId,
              ratedName: workerName(rate),
              scheduleText: scheduleText(order.scheduledDate, order.shiftCode),
            ))),
      ],
    );
  }
  if (canExtend(progress) && order.orderStatus == OrderStatus.assigned && !_extOpen) {
    return BottomActionBar.stack(children: [
      CtaButton(key: const Key('open-extension'), icon: Icons.more_time, label: 'Làm lần 2', onPressed: _openExtension),
    ]);
  }
  return null;
}
```
  Trong `_actions`: bỏ dòng `open-extension`; với `reviewAndAccept` và `rate`, **chỉ bỏ dòng của người đã lên thanh dưới** (người đầu tiên); người thứ hai trở đi vẫn là dòng trong "Việc khác" (key của họ khác nên không trùng). `_ready` gọi `_bar(order, progress)`.
  Nhãn dòng "Đặt lại với …": dòng phụ đổi thành "Cùng gói, cùng địa chỉ" (template); hành vi `_rebook` giữ (vẫn thêm vào yêu thích trước).
- (8) `SectionTitle('Tiến trình')` → `SectionHeading('Tiến trình')`; `SectionTitle('Người làm', trailing: 'x/y')` → `SectionHeading('Người làm', trailing: 'x/y')`; `SectionTitle('Việc khác')` → `SectionHeading('Việc khác')`. Card người làm: avatar 52 nền `amberSoft`/chữ `onAmberSoft`, pill `regular` không chấm (`dot: false`).
- Header tối: `OrderStatusPill(displayStatus, textKey: order-status, size: PillSize.regular)`; dòng ngày `bodyRegular.copyWith(fontSize: 13)`. Đơn huỷ: cả hàng giờ `Opacity(0.55)` thay vì đổi màu số.

### 3.6 Đặt ca — `booking_screen.dart` (`booking.html`)

**Lệch**: app bar Material; tiêu đề nhóm `titleSmall` không đánh số; ô gói/địa chỉ/ca là widget riêng chưa theo `ChoiceTile`; ô ngày; thẻ giá có viền highlight và số `primary`; thanh dưới hai cột (tổng bên trái, nút "Đặt ca" bên phải); có dòng "Thanh toán sau khi đặt…" (P5).

**Làm**
- `NordicPage.pushed(title: 'Đặt ca', lightAppBar: true, closeButton: true, bottomBar: ready ? _bar(quote) : null, children: _content())`.
- Tiêu đề bước: dùng hàm đánh số động, vì bước "Người làm" chỉ có khi gói Cơ bản và có người yêu thích:

```dart
int _step = 0;
Widget _heading(String text, {double top = 0.0, String? trailing}) {
  _step++;
  return Padding(
    padding: EdgeInsets.only(top: top, bottom: 8),
    child: Row(children: [
      Expanded(child: Text('$_step · $text'.toUpperCase(),
          style: NordicTypography.caps.copyWith(fontSize: 12, color: NordicColors.textSecondary))),
      if (trailing != null) Text(trailing, style: NordicTypography.bodySmall),
    ]),
  );
}
// đầu _content(): _step = 0;
```
  Thứ tự và chữ: `Gói` → (`Người làm`) → `Địa chỉ` → `Ngày` (trailing "Tháng 10–11" khi dải ngày qua hai tháng, ngược lại "Tháng 10") → `Ca` → `Ghi chú`. Khoảng trên mỗi bước 24.
- Ô gói: `ChoiceTile(key: Key('tier-$t'), selected:…, onTap:…, child: Column(crossAxisAlignment: start, children: [Text(tierLabel(t), style: titleSmall), SizedBox(8), Text(formatVnd(price), style: numberSmall), Text(dòng phụ, style: bodySmall)]))`. Giá: lấy `quote.unitPrice` khi `quote.serviceTier == t`, nếu không có thì chỉ hiện `tierDescription(t, options)` (không bịa giá). Dòng phụ: Cơ bản "mỗi người · mỗi ca"; Cao cấp: câu luật đặt trước đang có trong `tierDescription`.
- Bước "Người làm": các `_ChoiceCard` giữ key (`favorite-none`, `favorite-<id>`), vẽ bằng `ChoiceTile` dạng hàng: icon 22 `primary` + cột (tên 15/600, dòng phụ 12). Ghi chú `favorite-note` giữ.
- Địa chỉ: mỗi địa chỉ một `ChoiceTile` dạng hàng (icon `home_outlined` 22 `primary`, tên 15/600, dòng `addressLine · m²` 12 một dòng), cách nhau 8; "Quản lý địa chỉ" giữ dưới danh sách.
- Ngày (`_DayTile`): 52×64, bo 16, viền `border`; chọn: nền `primary`, chữ trắng, thứ `onDarkMuted`; thứ 12/500 trên, số 18/700 dưới; ngày không rảnh của người yêu thích: mờ 0.35 như cũ.
- Ca (`_ShiftTile`): `ChoiceTile`, ba cột cách 8; nội dung: icon 18 (`SHIFT_MORNING` → `wb_sunny_outlined`, `SHIFT_AFTERNOON` → `light_mode_outlined`, `SHIFT_EVENING` → `bedtime_outlined`; màu `primaryContainer` khi chọn, `textSecondary` khi không), `shiftLabel(code)` 15/600, dòng 12 `"${start}–${end}"`; ca không chọn được: `unavailable: true`, dòng 12 đổi thành "Hết chỗ".
- Ghi chú: `NordicTextInput` giữ, giữ hint hiện tại ("Có thú cưng, chìa khoá ở bảo vệ…"; chữ trong template chỉ là mẫu); bộ đếm `0/500` canh phải dưới ô (nếu `NordicTextInput` chưa có thì thêm `Text('${_noteController.text.length}/500')`, cập nhật bằng `onChanged`).
- Thẻ giá (`_quoteCard`, trạng thái có giá): bỏ tiêu đề "Giá"; thay bằng card trắng bo 24 bóng `raised`, cách bước Ghi chú 20:

```dart
DecoratedBox(
  decoration: const BoxDecoration(borderRadius: BorderRadius.all(Radius.circular(24)), boxShadow: NordicElevation.raised),
  child: Material(
    color: NordicColors.surface,
    shape: const RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(24))),
    child: Padding(
      padding: const EdgeInsets.all(NordicSpace.md),
      child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text('Giá server báo', style: NordicTypography.bodyRegular.copyWith(fontSize: 13, color: NordicColors.textSecondary)),
        const SizedBox(height: NordicSpace.xs),
        Row(children: [
          Expanded(child: Text('${quote.requiredWorkers} người × ${formatVnd(quote.unitPrice)}', key: const Key('quote-breakdown'),
              style: NordicTypography.bodyRegular.copyWith(fontSize: 13))),
          Text(formatVnd(quote.totalAmount), key: const Key('quote-total'), style: NordicTypography.numberSmall),
        ]),
        const SizedBox(height: NordicSpace.xxs),
        Text(workersNote(quote, options), key: const Key('quote-workers-note'), style: NordicTypography.bodySmall),
      ]),
    ),
  ),
)
```
  Pill "Khung 31–80 m²" của template: chỉ vẽ nếu `PriceQuote` có trường khung diện tích; không có thì **bỏ** (không tự tính).
  Các trạng thái loading / lỗi / trống của thẻ giá giữ chữ và key, vẽ bằng `NordicCard`.
- Bỏ `Text('Thanh toán sau khi đặt. Không phụ phí.')` (P5). `booking-hint`, `booking-error` giữ.
- Thanh dưới:

```dart
Widget _bar(PriceQuote? quote) {
  final missing = missingChoice(tier: _tier, addressId: _addressId, day: _day, shiftCode: _shiftCode, hasQuote: quote != null);
  return BottomActionBar.stack(
    hint: missing, // null khi đã đủ
    children: [
      CtaButton(
        key: const Key('submit-order'),
        label: quote == null ? 'Đặt ca' : 'Đặt ca · ${formatVnd(quote.totalAmount)}',
        isLoading: _submitting,
        onPressed: _submitting ? null : _submit,
      ),
    ],
  );
}
```
  Key `booking-total` (trước nằm trên chữ tổng ở thanh dưới) chuyển sang **chữ nhãn** của nút: `CtaButton` có thêm `labelKey` (§2.1), truyền `labelKey: const Key('booking-total')`. Nhãn là "Đặt ca · 260.000 đ" (hoặc "Đặt ca" khi chưa có giá); test đọc `booking-total` sửa cho khớp (§6). `quote-total` giữ trên số tiền trong thẻ giá.

### 3.7 Thanh toán — `payment_screen.dart` (`payment.html`)

**Lệch**: là `NordicPage.pushed` nền sáng, banner sandbox trên cùng, số tiền trong khối tối nhỏ (chỉ nhánh không phải SePay), SePay không có số tiền lớn; template: `DarkHeader` (app bar "Thanh toán") chứa "Số tiền cần trả", số 44/700, pill kem "Hết hạn sau mm:ss"; `RaisedPanel` chứa banner sandbox → QR → chữ dưới QR → vạch → hàng "Trạng thái" + pill → thông tin chuyển khoản (SePay) → `OrderCodeBox`; dưới panel: hộp ghi chú xanh nhạt "Màn tự cập nhật…"; thanh dưới nút outline "Để sau, mở đơn".

**Làm** — dựng lại `build()` theo khung của Chi tiết đơn (giữ toàn bộ state, timer, polling, sandbox confirm, `_done`, `_returnToPaidOrder`):

```dart
static const double _overlap = 64;

@override
Widget build(BuildContext context) {
  final args = _args;
  final payment = _payment;
  final now = _now();
  final phase = payment == null ? null : phaseOf(payment, now);
  return Scaffold(
    backgroundColor: NordicColors.canvas,
    body: Column(children: [
      Expanded(
        child: ListView(padding: EdgeInsets.zero, children: [
          DarkHeader(
            overlap: _overlap,
            appBarTitle: 'Thanh toán',
            child: SizedBox(
              width: double.infinity,
              child: Column(children: [
                Text(payment == null ? 'Thanh toán' : purposeLabel(payment),
                    style: NordicTypography.bodyRegular.copyWith(fontSize: 13, color: NordicColors.onDarkMuted)),
                const SizedBox(height: NordicSpace.xs),
                Text(payment == null ? '—' : formatVnd(payment.amount), key: const Key('payment-amount'),
                    style: NordicTypography.numberLarge.copyWith(fontSize: 44, color: NordicColors.textInverse)),
                if (phase == PaymentPhase.waiting) ...[
                  const SizedBox(height: NordicSpace.sm),
                  _expiryPill(payment!, now),
                ],
              ]),
            ),
          ),
          Transform.translate(
            offset: const Offset(0, -_overlap),
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              RaisedPanel(child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                const _SandboxBanner(),
                const SizedBox(height: NordicSpace.md),
                ..._body(args), // các nhánh cũ, vẽ lại bên trong panel (dưới)
              ])),
              if (phase == PaymentPhase.waiting) ...[
                const SizedBox(height: 20),
                Padding(padding: const EdgeInsets.symmetric(horizontal: NordicSpace.md), child: _autoNote()),
              ],
            ]),
          ),
        ]),
      ),
      if (args != null && phase != PaymentPhase.paid)
        BottomActionBar.stack(children: [
          CtaButton(key: const Key('payment-later'), variant: CtaVariant.outline, label: 'Để sau, mở đơn',
              onPressed: () => Navigator.of(context).maybePop()),
        ]),
    ]),
  );
}

Widget _expiryPill(Payment payment, DateTime now) => Container(
  height: 26,
  padding: const EdgeInsets.symmetric(horizontal: 10),
  decoration: const BoxDecoration(color: NordicColors.amberSoft, borderRadius: NordicRadius.pill),
  child: Row(mainAxisSize: MainAxisSize.min, children: [
    const Icon(Icons.timer_outlined, size: 15, color: NordicColors.onAmberSoft),
    const SizedBox(width: 6),
    Text('Hết hạn sau ', style: NordicTypography.bodySmall.copyWith(fontWeight: FontWeight.w600, color: NordicColors.onAmberSoft)),
    Text(countdownText(timeLeft(payment, now)), key: const Key('payment-countdown'),
        style: NordicTypography.bodySmall.copyWith(fontWeight: FontWeight.w600, color: NordicColors.onAmberSoft,
            fontFeatures: const [FontFeature.tabularFigures()])),
  ]),
);

Widget _autoNote() => Container(
  padding: const EdgeInsets.all(14),
  decoration: BoxDecoration(color: NordicColors.primaryFixed, borderRadius: BorderRadius.circular(18)),
  child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
    const Icon(Icons.sync, color: NordicColors.primary),
    const SizedBox(width: 12),
    Expanded(child: Text(
      _pollFailures >= 3 ? pollTroubleMessage : 'Màn tự cập nhật khi thanh toán xong, bạn không cần bấm gì thêm.',
      key: const Key('payment-waiting-note'),
      style: NordicTypography.bodyRegular.copyWith(fontSize: 13, color: NordicColors.textBody),
    )),
  ]),
);
```

  Nhánh **chờ thanh toán** (SePay và MoMo/FAKE gộp một khung), thay `_sePayWaiting` + `_waiting`:

```dart
List<Widget> _waitingPanel(Payment payment) {
  final sePay = payment.gateway.toUpperCase() == 'SEPAY';
  final qrUrl = payment.payUrl ?? payment.qrPayload;
  final uri = Uri.tryParse(payment.qrPayload ?? '');
  final code = uri?.queryParameters['des'];
  final bank = uri?.queryParameters['bank'];
  final account = uri?.queryParameters['acc'];
  return [
    Center(
      child: SizedBox(
        width: 188,
        height: 188,
        child: sePay
            ? (qrUrl == null || qrUrl.isEmpty
                ? const _QrUnavailable()
                : Image.network(qrUrl, key: const Key('sepay-qr'), fit: BoxFit.contain, semanticLabel: 'Mã QR chuyển khoản',
                    loadingBuilder: (c, child, p) => p == null ? child : const Center(child: CircularProgressIndicator(color: NordicColors.primary)),
                    errorBuilder: (c, e, s) => const _QrUnavailable()))
            // MOMO / FAKE: chưa có package vẽ QR (P3) → hộp nội dung, giữ key cũ.
            : Container(
                padding: const EdgeInsets.all(12),
                decoration: const BoxDecoration(color: NordicColors.surfaceSubtle, borderRadius: NordicRadius.tileAll),
                child: SingleChildScrollView(child: SelectableText(payment.qrPayload ?? '—', key: const Key('payment-qr-payload'), style: NordicTypography.bodySmall)),
              ),
      ),
    ),
    const SizedBox(height: NordicSpace.xs),
    Row(mainAxisAlignment: MainAxisAlignment.center, children: [
      const Icon(Icons.qr_code_scanner_outlined, size: 18, color: NordicColors.textSecondary),
      const SizedBox(width: 8),
      Text(sePay ? 'Quét bằng ứng dụng ngân hàng' : 'Quét bằng ứng dụng MoMo',
          style: NordicTypography.bodyRegular.copyWith(fontSize: 13, color: NordicColors.textSecondary)),
    ]),
    if (!sePay && payment.payUrl != null) ...[
      const SizedBox(height: NordicSpace.xs),
      SelectableText(payment.payUrl!, key: const Key('payment-pay-url'), textAlign: TextAlign.center, style: NordicTypography.bodySmall),
    ],
    const Padding(padding: EdgeInsets.symmetric(vertical: 14), child: Divider(height: 1, thickness: 1, color: NordicColors.surfaceContainer)),
    Row(children: [
      Expanded(child: Text('Trạng thái', style: NordicTypography.bodyRegular.copyWith(fontSize: 13, color: NordicColors.textSecondary))),
      const StatusPill('Đang chờ thanh toán', tone: PillTone.warn, size: PillSize.regular),
    ]),
    if (sePay) ...[
      const SizedBox(height: NordicSpace.sm),
      if (bank != null && bank.isNotEmpty) _transferDetail('Ngân hàng', bank, bank, 'copy-bank'),
      if (account != null && account.isNotEmpty) _transferDetail('Số tài khoản', account, account, 'copy-account'),
      _transferDetail('Số tiền', formatVnd(payment.amount), payment.amount.toString(), 'copy-amount'),
      _transferDetail('Nội dung chuyển khoản', code == null || code.isEmpty ? 'Chưa có nội dung chuyển khoản' : code, code, 'copy-content'),
    ],
    if (_args?.orderCode != null) ...[
      const SizedBox(height: NordicSpace.md),
      OrderCodeBox(code: _args!.orderCode!, copyKey: const Key('payment-order-code-copy'),
          onCopy: () => _copy(_args!.orderCode!, 'mã đơn')),
    ],
    if (_confirmerFor(payment) != null) ...[
      const SizedBox(height: NordicSpace.xs),
      Text(sandboxAutoConfirmNote(widget.sandboxAutoConfirmDelay!), key: const Key('payment-auto-confirm-note'),
          textAlign: TextAlign.center, style: NordicTypography.bodySmall),
    ],
  ];
}
```
  `_transferDetail` đổi thành một dòng cao ≥ 48: nhãn 12 `textSecondary` trên, giá trị `titleSmall` dưới (`SelectableText`), bên phải nút icon 44×36 giống nút trong `OrderCodeBox` (icon `content_copy_outlined`, `Semantics(label: 'Sao chép $label')`, giữ key `copy-*`), các dòng cách nhau 8. `_copy(text, label)` = phần `Clipboard.setData` + SnackBar đang có.
  Bỏ: `_statBox`, thanh `LinearProgressIndicator`, câu "Chuyển đúng số tiền…" (SePay) và "Sao chép nội dung trên để thanh toán thử." (MoMo/FAKE) — template không có; thông tin này đã nằm ở nhãn các dòng.
  Các nhánh `_paid`, `_expired`, `_refunded`, loading, lỗi, `args == null`: giữ chữ và key, đặt trong `RaisedPanel` (thay `NordicCard` bằng `Column` trực tiếp), nút dùng `CtaButton`.

### 3.8 Nghiệm thu — `customer_acceptance_screen.dart` (`acceptance.html`)

**Lệch**: tiêu đề "Nghiệm thu ca" (template "Nghiệm thu"); chip chọn góc + một thẻ so sánh; nút "Nghiệm thu" / "Dọn lại" nằm ngang.

**Làm**
- `NordicPage.pushed(title: 'Nghiệm thu', lightAppBar: true, bottomBar: awaiting ? BottomActionBar.stack(children: [...]) : null, …)`.
- Thanh dưới:

```dart
CtaButton(key: const Key('acceptance-accept'), icon: Icons.task_alt, label: 'Xác nhận nghiệm thu', isLoading: _isLoading, onPressed: _handleAcceptCompletion),
CtaButton(key: const Key('acceptance-redo'), variant: CtaVariant.outline, label: 'Yêu cầu dọn lại',
    onPressed: () => setState(() { _showRedoForm = true; _errorMessage = null; })),
```
  (Hai key mới, ghi ở §6. Test đang tìm theo chữ "Nghiệm thu"/"Dọn lại" thì đổi sang key.)
- Thân màn, thay `Row` đếm góc + "So sánh ảnh…" + `_buildAngleSelector()` + `_buildPhotoComparisonCard()` bằng **một** `NordicCard`:

```dart
NordicCard(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
  Row(children: [
    Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Text(_loadingPhotos ? '–/5' : '$_pairedAngles/5', key: const Key('acceptance-paired'), style: NordicTypography.numberMedium),
      const SizedBox(height: 4),
      Text('góc có ảnh trước và sau', style: NordicTypography.bodySmall),
    ])),
    _legend(), // ô 10×10 bo 3: surfaceContainer "Trước", primaryFixed "Sau", chữ 12 textSecondary
  ]),
  // lỗi / loading ảnh: giữ _notice(photos-error) + photos-retry như cũ, đặt ở đây
  for (final angle in _visibleAngles) ...[
    const SizedBox(height: NordicSpace.md),
    Text('$angle · ${_angleName(angle)}', style: NordicTypography.bodyRegular.copyWith(fontSize: 13, fontWeight: FontWeight.w600)),
    const SizedBox(height: 6),
    Row(children: [
      Expanded(child: _buildPhotoBox(/* BEFORE, angle — tham số như hàm hiện có */)),
      const SizedBox(width: 8),
      Expanded(child: _buildPhotoBox(/* AFTER, angle */)),
    ]),
  ],
  if (!_showAllAngles && _hiddenAngleCount > 0)
    TextButton.icon(
      key: const Key('acceptance-more-angles'),
      onPressed: () => setState(() => _showAllAngles = true),
      icon: const Icon(Icons.expand_more, size: 18),
      label: Text('Thêm $_hiddenAngleCount góc'),
    ),
])),
```
  `_visibleAngles` = `[1,2,3]` khi chưa mở, `[1..5]` khi `_showAllAngles`; `_hiddenAngleCount = 2`. `_angleName(a)`: tách phần sau dấu ":" của nhãn góc đang có (`'Góc 1: Toàn cảnh phòng'` → `'Toàn cảnh phòng'`). Ô ảnh cao 96, bo 14; chưa có ảnh: nền `surfaceContainer` (trước) / `primaryFixed` (sau), chữ "Trước"/"Sau" 11/600 góc dưới trái; giữ key `open-photo-<id>` và hành vi mở ảnh.
  Xoá `_buildAngleSelector` và biến góc đang chọn (P5).
- `_buildCompletionSummaryCard` (khi có kết quả): vẽ lưới 3 cột như §3.5 (Gói / Người làm / Số tiền) trong `NordicCard`, cách 12; giữ nội dung dòng mà hàm đang có.
- "Phản hồi (không bắt buộc)" và form dọn lại giữ nguyên chữ, key, `ErrorShake`; nút trong form dọn lại dùng `CtaButton`.

### 3.9 Đánh giá — `rating_screen.dart` (`rating.html`), **chỉ khi `widget.role == RatingRole.customer`**

**Lệch**: app bar Material có tiêu đề "Đánh giá <tên>"; avatar 72 không tint; dòng đếm giờ là chữ; sao tổng có nhãn "Tổng thể"; dòng "Đánh giá được lưu vĩnh viễn…".

**Làm**
- Thêm `final String? scheduleText;` (tuỳ chọn) vào `RatingScreenArgs` và `RatingScreen` (P2). Chi tiết đơn truyền `scheduleText(order.scheduledDate, order.shiftCode)` (đã có ở §3.5).
- `build()` khi customer: thay `Scaffold(appBar: AppBar…)` bằng `NordicPage.pushed(title: 'Đánh giá', lightAppBar: true, closeButton: true, bottomBar: _phase == _Phase.form ? BottomActionBar.stack(children: [CtaButton(key: const Key('rating-submit'), label: 'Gửi đánh giá', isLoading: _sending, onPressed: _complete && !_sending ? _submit : null)]) : null, children: [_body()])` (với `_body()` trả về widget không cuộn khi ở trong `NordicPage`: đổi `ListView` của `_form()` thành `Column`). Nhánh Worker giữ nguyên.
- Đầu form (customer):

```dart
Center(child: AvatarInitials(widget.ratedName ?? '', size: 60, background: NordicColors.amberSoft, foreground: NordicColors.onAmberSoft)),
const SizedBox(height: 8),
if (widget.ratedName != null) Text(widget.ratedName!, textAlign: TextAlign.center, style: NordicTypography.titleMedium.copyWith(fontSize: 19)),
if (widget.scheduleText != null) Text(widget.scheduleText!, textAlign: TextAlign.center, style: NordicTypography.bodyRegular.copyWith(fontSize: 13, color: NordicColors.textSecondary)),
const SizedBox(height: 8),
Center(child: Container(
  height: 26,
  padding: const EdgeInsets.symmetric(horizontal: 10),
  decoration: const BoxDecoration(color: NordicColors.surfaceContainer, borderRadius: NordicRadius.pill),
  child: Row(mainAxisSize: MainAxisSize.min, children: [
    const Icon(Icons.timer_outlined, size: 15, color: NordicColors.textBody),
    const SizedBox(width: 6),
    Text(countdownText(window.secondsRemaining), key: const Key('rating-countdown'),
        style: NordicTypography.bodySmall.copyWith(fontWeight: FontWeight.w600, color: NordicColors.textBody)),
  ]),
)),
const SizedBox(height: 20),
Center(child: StarRow(keyPrefix: 'stars', label: 'Tổng thể', value: _stars, size: 44, showLabel: false, onChanged: (v) => setState(() => _stars = v))),
```
  `countdownText` hiện trả gì thì giữ (không đổi logic); nếu nó đã có chữ "còn …" thì không thêm tiền tố.
  `StarRow` (cùng file, dòng ~383) thêm `final bool showLabel;` (mặc định `true`, Worker không đổi): khi `false` thì không vẽ chữ nhãn nhưng vẫn giữ `Semantics(label: '$label: $i sao')`.
- Card tiêu chí: giữ, padding `16/4`, vạch chia `surfaceContainer`; mỗi dòng cao ≥ 52, tên tiêu chí `titleSmall`, sao 22 bên phải.
- Ô nhận xét: `NordicTextInput` (hoặc `TextField` có `InputDecoration` bo 14 viền `border`, nền trắng) cao tối thiểu 96, hint "Nhận xét thêm (không bắt buộc)"; bộ đếm "`n`/500" canh phải (bỏ chữ "ký tự").
- Bỏ câu "Đánh giá được lưu vĩnh viễn…" ở nhánh customer (P5 — template không có).

### 3.10 Khiếu nại — `dispute_screen.dart`, `dispute_create_screen.dart` (`disputes.html`, `dispute-create.html`), **chỉ khi role là customer**

**Danh sách** (`_tile(d)`)
- `NordicPage.pushed(title: 'Khiếu nại', lightAppBar: true, …)`; nút "Khiếu nại ca này" (`dispute-new`) → `bottomBar: BottomActionBar.stack(children: [CtaButton(key: const Key('dispute-new'), …)])`.
- Mỗi khiếu nại một `NordicCard`, cách nhau 12:
  - Hàng 1: `Text(categoryLabel(d.category), style: titleSmall)` + `StatusPill(statusLabel(d.disputeStatus), size: PillSize.regular, tone: …)` (tone giữ cách chọn hiện tại; "Đang xử lý" → `warn`, "Đã có phán quyết" → `ok`, "Đã bác bỏ" → `neutral`).
  - Hàng 2 (12 `textSecondary`, cách 4): `'Đơn ${mã đơn} · gửi ${dd/MM}'` — dùng đúng giá trị mã đơn mà `_tile` đang hiện (nếu `_tile` chỉ có `orderId` thì giữ cách hiện đang hiện; **không** gọi thêm API), ngày từ `d.createdAt`.
  - Đang xử lý: `StepDots` 3 bước (`_steps(status)` hiện có → map sang `StepItem`), cách trên 16.
  - Có phán quyết: vạch chia 14, lưới 2 cột: "Bồi thường" + `formatVnd(d.compensationAmount)` (`numberSmall`, key `dispute-compensation-<id>`) | "Kết luận" + `faultLabel(d.faultParty)` (`titleSmall`, key `dispute-fault-<id>`); bỏ cột nào không có dữ liệu.
  - Đã bác bỏ: chỉ hai hàng đầu (câu "Khiếu nại đã bị bác bỏ…" giữ dưới dạng 12 `textSecondary` cách 8).

**Tạo khiếu nại**
- `NordicPage.pushed(title: 'Khiếu nại', lightAppBar: true, closeButton: true, bottomBar: BottomActionBar.stack(children: [CtaButton(key: const Key('dispute-submit'), label: 'Gửi khiếu nại', …)]))`.
- Đầu form: `OrderCodeBox(code: <mã đơn đang hiện>, onCopy: …, trailing: StatusPill('còn $h giờ', tone: PillTone.warn, size: PillSize.regular))` nếu màn đang có hạn 24 giờ; không có dữ liệu hạn thì bỏ pill.
- Tiêu đề nhóm (`_caps`) đổi sang kiểu §3.6 (caps 12 `textSecondary`, không đánh số): "Vấn đề", "Mô tả", "Ảnh bằng chứng" (trailing "n/10").
- Chip loại (`dispute-category-<code>`): cao 38 (vùng chạm 44 bằng padding ngoài), nền trắng viền `border`; chọn: nền `primary` chữ trắng; chữ 14/600.
- Mô tả: ô bo 14, cao tối thiểu 110; đang gõ viền 2 `primaryContainer`; bộ đếm "n/1000" canh phải.
- Ảnh: lưới 4 cột cách 8, ô 74 bo 14; ô thêm ảnh (`dispute-add-photo`) viền đứt 1.5 `border`, nền trắng, icon `add_a_photo_outlined` `primary`; nút xoá ảnh `dispute-remove-<i>` giữ (góc trên phải ô).

### 3.11 Địa chỉ của tôi — `address_list_screen.dart`, `address_card.dart` (`addresses.html`)

**Lệch**: mỗi địa chỉ một card có hai nút sửa / xoá; template: một `.glist`, mỗi dòng icon loại nhà + tên (+ pill "Mặc định") + địa chỉ một dòng + diện tích bên phải, không nút.

**Làm**
- `NordicPage.pushed(lightAppBar: true, bottomBar: BottomActionBar.stack(children: [CtaButton(key: const Key('address-add'), variant: _addresses.isEmpty ? CtaVariant.primary : CtaVariant.outline, icon: Icons.add, label: 'Thêm địa chỉ', onPressed: _navigateToAdd)]))`.
- Danh sách: một card như §3.2 (DecoratedBox bóng `card` + Material bo 20), mỗi địa chỉ một `AddressCard` dạng dòng, vạch chia lùi 70.
- `AddressCard.build` thành dòng (giữ constructor và các callback):

```dart
InkWell(
  key: Key('address-card-${address.addressId}'),
  onTap: onSelect, // đang chọn cho Đặt ca: chọn; còn lại: mở sheet (dưới)
  child: ConstrainedBox(
    constraints: const BoxConstraints(minHeight: 64),
    child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Row(children: [
        Container(
          width: 40, height: 40,
          decoration: BoxDecoration(color: selected ? NordicColors.primaryFixed : NordicColors.surfaceSubtle, borderRadius: NordicRadius.tileAll),
          child: Icon(selected ? Icons.check : _getHousingIcon(),
              key: selected ? Key('address-selected-${address.addressId}') : null, color: NordicColors.primary),
        ),
        const SizedBox(width: 14),
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Flexible(child: Text(address.label, style: NordicTypography.titleSmall, overflow: TextOverflow.ellipsis)),
            if (address.isDefault) ...[const SizedBox(width: 8), const StatusPill('Mặc định', tone: PillTone.ok)],
          ]),
          const SizedBox(height: 4),
          Text(address.fullAddress, style: NordicTypography.bodySmall, maxLines: 1, overflow: TextOverflow.ellipsis),
        ])),
        const SizedBox(width: 8),
        Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
          Text(_area, key: Key('address-area-${address.addressId}'), style: NordicTypography.titleSmall),
          Text('m²', style: NordicTypography.bodySmall.copyWith(fontSize: 11)),
        ]),
      ]),
    ),
  ),
)
```
- Sửa / xoá / đặt mặc định không còn là nút trên dòng. Ở `address_list_screen.dart`, khi **không** ở chế độ chọn (`_pick == null`), `onSelect` mở bottom sheet:

```dart
Future<void> _openActions(CustomerAddress a) => showModalBottomSheet<void>(
  context: context,
  backgroundColor: NordicColors.surface,
  shape: const RoundedRectangleBorder(borderRadius: NordicRadius.sheetTop),
  builder: (sheet) => SafeArea(child: Padding(
    padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
    child: GroupedListCard(items: [
      if (!a.isDefault)
        GroupedListItem(key: Key('address-default-${a.addressId}'), icon: Icons.check_circle_outline, title: 'Đặt làm mặc định',
            onTap: () { Navigator.pop(sheet); _select(a); }),
      GroupedListItem(key: Key('address-edit-${a.addressId}'), icon: Icons.edit_outlined, title: 'Sửa địa chỉ',
          onTap: () { Navigator.pop(sheet); _navigateToEdit(a); }),
      GroupedListItem(key: Key('address-delete-${a.addressId}'), icon: Icons.delete_outline, title: 'Xoá địa chỉ', danger: true,
          onTap: () { Navigator.pop(sheet); _confirmDelete(a); }),
    ]),
  )),
);
// trong build: onSelect: () => _pick != null ? _select(_addresses[i]) : _openActions(_addresses[i]),
```
  `AddressCard` không còn dùng `onEdit`, `onDelete` trong build; giữ tham số để không đổi chữ ký (đánh dấu `// used by the actions sheet`) **hoặc** xoá tham số và sửa các nơi gọi — chọn cách xoá nếu `grep -rn "AddressCard(" lib test` chỉ ra ≤ 2 nơi gọi.
  Key `address-edit-<id>` chuyển vào sheet (test cần mở sheet trước: tap `address-card-<id>` rồi tap `address-edit-<id>`). Key mới: `address-default-<id>`, `address-delete-<id>`, `address-add` (§6).

### 3.12 Form địa chỉ — `address_form_screen.dart` (`address-form.html`)

**Làm**
- `NordicPage.pushed(lightAppBar: true, bottomBar: BottomActionBar.stack(children: [CtaButton(label: 'Lưu địa chỉ', …)]))` (giữ key nút lưu hiện có).
- Mọi trường: nhãn 13/600 `textBody` ở **trên** ô, cách 6; ô cao 52 bo 14 viền `border` nền trắng; đang gõ viền 2 `primaryContainer`. Thứ tự: Tên gợi nhớ → (Tỉnh / Thành phố | Phường / Xã, hai cột cách 12) → Số nhà, đường → Loại nhà → Diện tích. Khoảng cách giữa các trường 16.
- Loại nhà: segmented như template (`.seg`): nền `surfaceContainer` bo 16 padding 4; mục chọn nền trắng chữ `primary` bóng `card`, cao 40 bo 12; chữ 14/600 `textSecondary` cho mục còn lại. Giữ key và giá trị `HousingType` hiện có.
- Diện tích (`AreaCalculatorCard`): card trắng: caps "DIỆN TÍCH", hàng "29 m² × 2 tầng = 58 m²" (số cuối `numberLarge`), hàng "Khung giá" + pill ok **chỉ khi** màn đang có dữ liệu khung; các ô nhập diện tích sàn / số tầng giữ như hiện tại, đặt trên hàng phép nhân.
- GPS picker (P4): giữ, đặt sau thẻ diện tích dưới tiêu đề caps "VỊ TRÍ".

### 3.13 Hồ sơ — `customer_profile_screen.dart` (`profile.html`)

**Làm**
- `NordicPage.pushed(title: 'Hồ sơ', lightAppBar: true, bottomBar: ready ? BottomActionBar.stack(children: [CtaButton(key: const Key('saveProfileButton'), label: 'Lưu', isLoading: …, onPressed: …)]) : null)`.
- Thân:

```dart
Center(child: AvatarInitials(profile.fullName, size: 60, background: NordicColors.primaryFixed, foreground: NordicColors.primary)),
const SizedBox(height: 20),
_field('Họ và tên', /* TextFormField fullNameField hiện có */),
const SizedBox(height: 16),
_field('Email', /* emailField hiện có, hint giữ */),
const SizedBox(height: 16),
_field('Số điện thoại', /* ô khoá hiện có: nền surfaceSubtle, chữ textSecondary, icon lock_outline */),
const SizedBox(height: 4),
Text('Số điện thoại dùng để đăng nhập nên không đổi ở đây.', style: NordicTypography.bodySmall),
```
  Nhãn: "Họ và tên", "Email", "Số điện thoại" (bỏ phần trong ngoặc; "không bắt buộc" của Email chuyển thành hint của ô nếu ô chưa có hint).
- Bỏ (P5): `NordicCard` đầu màn với tên + pill trạng thái + pill điểm uy tín; `GroupedListCard` Đăng xuất (`logoutButton`).

### 3.14 Thanh điều hướng — `customer_shell.dart`

Đã khớp (5 mục, nút giữa 60×60 viền 4 `canvas` bóng `cta`, nhãn "Đặt ca"). Chỉ kiểm: nhãn nút giữa màu `primary` 12/600; tab đang chọn pill `primaryFixed` 56×30, icon bản đặc; nền thanh trắng 96 % có viền trên `border`. Lệch thì sửa theo `v2.css` `.bnav`.

---

## 4. Không làm

Package mới (QR, chart, map); dịch vụ nấu ăn; chuông, chat, lịch định kỳ; thay đổi phía Worker ngoài widget dùng chung; chỉnh template HTML để khớp code (chiều ngược lại).

## 5. Thứ tự commit (một ticket, một PR)

1. §2 widget dùng chung + test widget cho `CtaButton`, `StepDots`, `GroupedListCard` (render, key, danger row) → `flutter analyze`, `flutter test`.
2. §3.5 Chi tiết đơn (thanh dưới theo trạng thái) + sửa test.
3. §3.7 Thanh toán.
4. §3.6 Đặt ca.
5. §3.1–§3.4 bốn tab.
6. §3.8–§3.13 các màn còn lại.
7. Cập nhật `DESIGN-SYSTEM.md` §4 (widget mới) và §5 (mẫu thanh dưới `.stack`), đánh dấu file này "đã code".

## 6. Key: mới, chuyển chỗ, bỏ

| Key | Thay đổi | Test cần sửa |
|---|---|---|
| `pay-order`, `order-rebook`, `order-back-to-list` | giữ, nút đổi sang `CtaButton` | finder theo kiểu widget |
| `rate-<id>` (người đầu tiên), `accept-<id>` (người đầu tiên), `open-extension` | chuyển từ "Việc khác" xuống thanh dưới | test tìm trong `GroupedListCard` |
| `accept-photos-<id>` | **mới** (nút "Xem ảnh") | — |
| `open-cancel` | thành dòng đỏ trong "Việc khác" | finder theo `NordicButton` |
| `order-code` | chỉ còn mã, không có chữ "Mã đơn " | `find.text` |
| `home-next-detail` | **mới** | — |
| `favorite-remove-<id>` | **mới** (nút tim) | test dùng tooltip |
| `booking-total` | chuyển sang nhãn của nút `submit-order` ("Đặt ca · …") | test đọc số tiền |
| `payment-later`, `payment-order-code-copy` | **mới** | — |
| `payment-countdown`, `payment-amount`, `payment-waiting-note` | chuyển vào header / hộp ghi chú | — |
| `acceptance-accept`, `acceptance-redo`, `acceptance-more-angles` | **mới** | test tìm theo chữ "Nghiệm thu"/"Dọn lại" |
| `address-edit-<id>` | chuyển vào bottom sheet | tap card trước |
| `address-default-<id>`, `address-delete-<id>`, `address-add` | **mới** | — |
| `logoutButton` (Hồ sơ) | **bỏ** (P5) | xoá test đăng xuất ở màn Hồ sơ; giữ test `home-sign-out` |
| `payment` `_statBox` (không key), thanh tiến độ QR | bỏ | — |

Sau mỗi commit: `grep -rn "Key('<key>')" lib | wc -l` phải ra 1 cho mọi key ở bảng (trừ key dạng `-<id>` trong vòng lặp).

## 7. Done cho mỗi màn

- [ ] Đặt ảnh chụp Flutter (390×844, `flutter run -d chrome --web-browser-flag=--window-size=390,844` hoặc golden test) cạnh template cùng trạng thái: thứ bậc, thứ tự khối, copy cố định khớp; sai số khoảng cách ±4 px.
- [ ] Chỉ dùng token và widget §2; không hex mới.
- [ ] Loading / lỗi có "Thử lại" / trống vẫn có.
- [ ] 360 px và 428 px không tràn; `textScaler` 1.3; bàn phím mở thì thanh dưới nằm trên bàn phím.
- [ ] Vùng chạm ≥ 44; nút chỉ có icon có `Semantics`.
- [ ] `flutter analyze` 0 issue, `flutter test` xanh, `harness verify L3` xanh.
